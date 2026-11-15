using System.Net;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

using RiosvImports.Core.Localization;

namespace RiosvImports.App.Updates;

/// <summary>
/// Кой върши самата работа по обновяването.
///
/// Две страни с един и същ вид: инсталираната програма пита, сваля и слага
/// (Velopack); пуснатата от папка само пита GitHub и казва, че има ново — да
/// сменя файловете си сама тя не може. Състоянието и екраните не знаят коя е
/// от двете; тестът подава трета, която не излиза в мрежата.
/// </summary>
internal interface IUpdateBackend
{
    /// <summary>Може ли да свали и сложи — само инсталираната програма.</summary>
    bool CanApply { get; }

    InstallKind Kind { get; }

    /// <summary>Номерът на пуснатото издание.</summary>
    string CurrentVersion { get; }

    /// <summary>Свалено отпреди издание, което чака рестарт. Пита само диска.</summary>
    ReleaseInfo? Pending { get; }

    /// <summary>По-новото издание или null. Грешката излиза навън — службата я превежда.</summary>
    Task<ReleaseInfo?> CheckAsync(CancellationToken token);

    /// <summary>Сваля намереното при последната проверка. Напредъкът е 0–100.</summary>
    Task DownloadAsync(Action<int> progress, CancellationToken token);

    /// <summary>
    /// Пуска инсталатора да чака програмата да излезе, да смени файловете и да
    /// я отвори наново. Вика се, след като базата е затворена.
    /// </summary>
    void ApplyOnExit();
}

internal static class UpdateBackends
{
    /// <summary>
    /// Инсталираната програма получава Velopack; всяка друга — пробата през
    /// API-то на GitHub. Конструкторът на Velopack само чете диска, мрежата се
    /// пипа чак при проверката.
    /// </summary>
    public static IUpdateBackend Create(bool prerelease, HttpClient http)
    {
        try
        {
            IUpdateSource source = UpdateFeed.IsGitHub
                ? new GithubSource(UpdateFeed.Repository, accessToken: null, prerelease)
                : new SimpleWebSource(UpdateFeed.Repository);

            var manager = new UpdateManager(source);

            if (manager.IsInstalled) return new VelopackBackend(manager);
        }
        catch
        {
            // Повреден инсталатор не бива да спре програмата: тя работи и без обновяване.
        }

        return new ProbeBackend(http, prerelease);
    }

    /// <summary>
    /// Грешката на мрежата с думи за човек. Самото изключение отива в
    /// дневника на грешките — то е за този, който поддържа програмата.
    /// </summary>
    public static string Explain(Exception ex)
    {
        // По кода на отговора, а не по текста: в текста на изключението стоят и
        // номерата на редовете, а „ред 403“ не е отказ от GitHub.
        for (var inner = ex; inner is not null; inner = inner.InnerException)
        {
            if (inner is ReleasesMissingException or HttpRequestException { StatusCode: HttpStatusCode.NotFound })
                return Lang.T(
                    "Хранилището с изданията в GitHub още го няма или не е публично.",
                    "The releases repository on GitHub does not exist yet or is not public.");

            if (inner is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } ||
                inner.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                return Lang.T(
                    "GitHub спря проверките от тази мрежа за около час. Опитайте по-късно.",
                    "GitHub has paused checks from this network for about an hour. Try again later.");

            if (inner is TaskCanceledException or TimeoutException)
                return Lang.T("GitHub не отговори навреме.", "GitHub did not answer in time.");

            if (inner is HttpRequestException)
                return Lang.T("Няма връзка с GitHub — проверете интернета.",
                              "No connection to GitHub — check the internet connection.");
        }

        return ex.Message;
    }
}

/// <summary>Изданията ги няма — 404 от GitHub. Отделен вид, за да се каже с думи.</summary>
internal sealed class ReleasesMissingException() : Exception("releases repository not found");

/// <summary>Инсталираната програма: Velopack пита, сваля и слага.</summary>
internal sealed class VelopackBackend(UpdateManager manager) : IUpdateBackend
{
    private readonly UpdateManager _manager = manager;
    private UpdateInfo? _found;

    public bool CanApply => true;

    public InstallKind Kind => _manager.IsPortable ? InstallKind.Portable : InstallKind.Installed;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? AppVersion.Build;

    public ReleaseInfo? Pending
    {
        get
        {
            try
            {
                return _manager.UpdatePendingRestart is { } asset ? Describe(asset) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken token)
    {
        _found = await _manager.CheckForUpdatesAsync();

        return _found is null ? null : Describe(_found.TargetFullRelease);
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken token) =>
        _found is null
            ? throw new InvalidOperationException("Няма намерено издание за сваляне.")
            : _manager.DownloadUpdatesAsync(_found, progress, token);

    public void ApplyOnExit()
    {
        var asset = _manager.UpdatePendingRestart ?? _found?.TargetFullRelease;
        if (asset is null) return;

        // Не ApplyUpdatesAndRestart: то спира процеса на място, без отворен
        // отчет да се запише и без базата да се затвори. Тук инсталаторът
        // чака програмата да излезе сама.
        _manager.WaitExitThenApplyUpdates(asset, silent: false, restart: true, []);
    }

    private static ReleaseInfo Describe(VelopackAsset asset)
    {
        var version = asset.Version.ToString();
        return new ReleaseInfo(version, asset.NotesMarkdown ?? string.Empty, UpdateFeed.ReleasePage(version));
    }
}

/// <summary>
/// Пусната от папка: само пита GitHub кое е последното издание.
///
/// Без нея проверката щеше да е изключена точно там, където се пробва — в
/// сглобената от Visual Studio програма и в старите самостоятелни exe файлове.
/// </summary>
internal sealed class ProbeBackend(HttpClient http, bool prerelease) : IUpdateBackend
{
    private readonly HttpClient _http = http;
    private readonly bool _prerelease = prerelease;

    public bool CanApply => false;

    public InstallKind Kind => InstallKind.Unpacked;

    public string CurrentVersion => AppVersion.Build;

    public ReleaseInfo? Pending => null;

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken token)
    {
        if (!UpdateFeed.IsGitHub) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateFeed.ReleasesApi);

        // GitHub иска име на програмата и нищо повече. Без поща и без машина.
        request.Headers.UserAgent.ParseAdd($"RIOSV-Imports/{AppVersion.Build}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, token);

        if (response.StatusCode == HttpStatusCode.NotFound) throw new ReleasesMissingException();

        response.EnsureSuccessStatusCode();

        var latest = ReleaseProbe.Latest(await response.Content.ReadAsStringAsync(token), _prerelease);

        return latest is not null && AppVersion.IsNewer(latest.Version, CurrentVersion) ? latest : null;
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken token) =>
        throw new InvalidOperationException("Пуснатата от папка програма не се обновява сама.");

    public void ApplyOnExit()
    {
    }
}

/// <summary>Чете списъка с издания от API-то на GitHub. Отделно, за да се проверява без мрежа.</summary>
internal static class ReleaseProbe
{
    /// <summary>
    /// Най-новото издание от отговора на „/releases“. Черновите не се броят,
    /// пробните — само ако са поискани. Етикет, който не е номер, се прескача.
    /// </summary>
    public static ReleaseInfo? Latest(string json, bool prerelease)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

        ReleaseInfo? best = null;
        NuGet.Versioning.SemanticVersion? bestVersion = null;

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft")) continue;
            if (Flag(release, "prerelease") && !prerelease) continue;

            var tag = Text(release, "tag_name");
            var version = AppVersion.Parse(tag);

            if (version is null) continue;
            if (!prerelease && version.IsPrerelease) continue;
            if (bestVersion is not null && version.CompareTo(bestVersion) <= 0) continue;

            bestVersion = version;
            best = new ReleaseInfo(
                version.ToNormalizedString(),
                Text(release, "body"),
                Text(release, "html_url") is { Length: > 0 } page ? page : UpdateFeed.ReleasesPage);
        }

        return best;
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
