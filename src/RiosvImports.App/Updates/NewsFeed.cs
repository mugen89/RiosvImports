using System.Globalization;
using System.Text.Json;
using RiosvImports.Data;

using RiosvImports.Core.Localization;

namespace RiosvImports.App.Updates;

/// <summary>
/// Новините — файлът news.json в публичното хранилище (виж <see cref="UpdateFeed"/>).
///
/// Пише се на ръка, през сайта на GitHub, и затова се чете снизходително:
/// съобщение без белег или заглавие се прескача, а не проваля останалите.
/// Повреден файл не пипа нищо — по-добре вчерашните новини, отколкото никакви.
///
/// <code>
/// {
///   "news": [
///     {
///       "id": "2026-11-14-github",          задължително, не се сменя
///       "date": "2026-11-14",               задължително
///       "severity": "info",                 info | important | critical
///       "title": "Програмата се обновява сама",
///       "body": "…",
///       "link": "https://…",                по избор, само https
///       "until": "2026-12-31",              по избор — след тази дата не се показва
///       "from": "1.3.0",                    по избор — само за това издание и по-новите
///       "below": "1.3.1"                    по избор — само за по-старите от това
///     }
///   ]
/// }
/// </code>
/// </summary>
internal static class NewsFeed
{
    /// <summary>Повече от това не е новина, а бюлетин — и не се събира на екрана.</summary>
    private const int MaxItems = 50;

    /// <summary>
    /// Съобщенията, които се отнасят до това издание днес. Null значи, че
    /// файлът не се чете изобщо — тогава базата не се пипа.
    /// </summary>
    public static IReadOnlyList<NewsItem>? Parse(string json, string currentVersion, DateOnly today)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            // И „{ "news": [...] }“, и направо масив — първото е за догодина, когато
            // във файла влезе и нещо друго.
            var list = root.ValueKind switch
            {
                JsonValueKind.Array => root,
                JsonValueKind.Object when root.TryGetProperty("news", out var news) &&
                                          news.ValueKind == JsonValueKind.Array => news,
                _ => default
            };

            if (list.ValueKind != JsonValueKind.Array) return null;

            var items = new List<NewsItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in list.EnumerateArray())
            {
                if (items.Count >= MaxItems) break;
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var id = Text(entry, "id", int.MaxValue);
                var title = Text(entry, "title", 200);

                // Белегът е ключът в базата: дълъг не се реже, а се прескача —
                // отрязан, той би бил друг белег. Местните са на програмата —
                // файлът не може да ги подмени.
                if (id.Length is 0 or > 100 || title.Length == 0) continue;
                if (id.StartsWith(NewsStore.LocalPrefix, StringComparison.Ordinal)) continue;
                if (!seen.Add(id)) continue;

                if (Date(entry, "date") is not { } published) continue;
                if (Date(entry, "until") is { } until && today > until) continue;

                if (Text(entry, "from", 40) is { Length: > 0 } from &&
                    AppVersion.Parse(from) is { } lowest &&
                    (AppVersion.Parse(currentVersion) is not { } current || current.CompareTo(lowest) < 0)) continue;

                if (Text(entry, "below", 40) is { Length: > 0 } below &&
                    AppVersion.Parse(below) is { } ceiling &&
                    AppVersion.Parse(currentVersion) is { } running && running.CompareTo(ceiling) >= 0) continue;

                var severity = Text(entry, "severity", 20).ToLowerInvariant() switch
                {
                    "critical" => NewsSeverity.Critical,
                    "important" => NewsSeverity.Important,
                    _ => NewsSeverity.Info
                };

                // Само https: връзката се отваря в браузъра с едно щракване.
                var link = Text(entry, "link", 500);
                if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) link = string.Empty;

                items.Add(new NewsItem(id, published.ToDateTime(TimeOnly.MinValue), severity, title,
                    Text(entry, "body", 2000), false, link));
            }

            return items;
        }
    }

    /// <summary>
    /// Тегли файла и привежда базата към него. Връща колко съобщения са нови
    /// или null, ако файлът не се стигна или не се прочете.
    ///
    /// Без ConfigureAwait(false) нарочно: записът в базата става на нишката, от
    /// която е повикано — връзката към SQLite не се дели между нишки.
    /// </summary>
    public static async Task<int?> RefreshAsync(Workspace workspace, HttpClient http, string currentVersion)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateFeed.NewsUrl);
            request.Headers.UserAgent.ParseAdd($"RIOSV-Imports/{AppVersion.Build}");

            using var response = await http.SendAsync(request);

            if (!response.IsSuccessStatusCode) return null;

            var items = Parse(await response.Content.ReadAsStringAsync(), currentVersion,
                DateOnly.FromDateTime(DateTime.Today));

            return items is null ? null : workspace.News.Sync(items);
        }
        catch (Exception ex)
        {
            // Без мрежа новини няма — това не е грешка на човека.
            if (ex is not (HttpRequestException or TaskCanceledException)) Program.WriteCrashLog(ex);
            return null;
        }
    }

    /// <summary>
    /// „Обновено до …“ — веднъж, при първото пускане на новото издание.
    ///
    /// По номера, с който програмата е пускана последно: така се познава и
    /// обновеното от Velopack, и инсталираното на ръка. Първото пускане изобщо
    /// не казва нищо — тогава няма „преди“.
    /// </summary>
    public static bool NoteUpgrade(Workspace workspace, string currentVersion)
    {
        var settings = workspace.Settings;
        var last = settings.Get(SettingsStore.Keys.UpdateLastRun);

        settings.Set(SettingsStore.Keys.UpdateLastRun, currentVersion);

        if (last is null || !AppVersion.IsNewer(currentVersion, last)) return false;

        // Бележките са записани преди рестарта — от проверката, която е
        // намерила изданието. Чужди бележки (за друго издание) не се показват.
        var saved = settings.Get(SettingsStore.Keys.UpdateNotes) ?? string.Empty;
        var split = saved.IndexOf('\n');
        var notes = split > 0 && saved[..split].Trim() == currentVersion ? saved[(split + 1)..] : string.Empty;

        var body = ReleaseNotes.Plain(notes);

        // Само последното „Обновено до …“: предишните вече са казали своето,
        // а пет поредни обновявания не бива да бъдат пет карти за махане.
        foreach (var old in workspace.News.List(50)
                     .Where(n => n.Id.StartsWith(NewsStore.LocalPrefix + "updated:", StringComparison.Ordinal)))
            workspace.News.Hide(old.Id);

        workspace.News.Add(new NewsItem(
            NewsStore.LocalPrefix + "updated:" + currentVersion,
            DateTime.Now,
            NewsSeverity.Info,
            Lang.T($"Обновено до издание {currentVersion}", $"Updated to version {currentVersion}"),
            body.Length > 0
                ? body
                : Lang.T($"Преди това: {last}. Какво е новото — в страницата на изданието.",
                         $"Before this: {last}. What is new — on the release page."),
            false,
            UpdateFeed.IsGitHub ? UpdateFeed.ReleasePage(currentVersion) : string.Empty));

        return true;
    }

    private static string Text(JsonElement entry, string name, int max)
    {
        if (!entry.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return string.Empty;

        var text = (value.GetString() ?? string.Empty).Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    private static DateOnly? Date(JsonElement entry, string name)
    {
        var text = Text(entry, name, 40);

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
