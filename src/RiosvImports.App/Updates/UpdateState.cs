using System.Reflection;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace RiosvImports.App.Updates;

/// <summary>Докъде е стигнало обновяването в това пускане.</summary>
internal enum UpdateStage
{
    /// <summary>Не е питано още.</summary>
    Idle,

    Checking,

    /// <summary>Няма по-ново.</summary>
    Current,

    /// <summary>Има по-ново, още не е свалено.</summary>
    Available,

    Downloading,

    /// <summary>Свалено е и чака рестарт.</summary>
    Ready,

    /// <summary>Не се стигна до изданията — мрежа, GitHub, повреден файл.</summary>
    Failed
}

/// <summary>Как е пусната програмата — от това зависи дали може да се обнови сама.</summary>
internal enum InstallKind
{
    /// <summary>От инсталатора, в профила на потребителя.</summary>
    Installed,

    /// <summary>От преносимия архив на изданието — също се обновява сама.</summary>
    Portable,

    /// <summary>От папка: сглобена тук или стар самостоятелен exe. Само казва, че има ново.</summary>
    Unpacked
}

/// <summary>Едно издание, както го описва GitHub или инсталаторът.</summary>
internal sealed record ReleaseInfo(string Version, string Notes, string Page);

/// <summary>
/// Състоянието, което екраните показват. Едно за всички: началният екран и
/// настройките казват едно и също, защото четат едно и също.
/// </summary>
internal sealed record UpdateState(
    UpdateStage Stage,
    ReleaseInfo? Release = null,
    int Progress = 0,
    string? Problem = null,
    DateTime? CheckedAt = null)
{
    /// <summary>Има ли по-ново — намерено, сваляно или свалено.</summary>
    public bool HasNewer => Stage is UpdateStage.Available or UpdateStage.Downloading or UpdateStage.Ready ||
                            (Stage == UpdateStage.Failed && Release is not null);

    public string? Version => Release?.Version;
}

/// <summary>
/// Номерът на изданието и сравняването на номера.
///
/// По SemVer, а не като текст: като текст „1.10.0“ е преди „1.9.0“, а пробното
/// „1.3.0-beta.2“ — след окончателното „1.3.0“.
/// </summary>
internal static class AppVersion
{
    /// <summary>
    /// Номерът, с който е сглобена програмата — от Directory.Build.props или от
    /// „-p:Version“ при издание. Сглобката добавя „+“ и коммита; той не е за
    /// гледане и се маха.
    /// </summary>
    public static string Build { get; } = ReadBuild();

    /// <summary>Пълният номер с коммита — за данните за поддръжка.</summary>
    public static string BuildFull { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Build;

    private static string ReadBuild()
    {
        var full = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(full))
            return typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var plus = full.IndexOf('+');
        return plus >= 0 ? full[..plus] : full;
    }

    /// <summary>„v1.3.0“, „1.3.0“ и „ 1.3.0 “ са едно и също. Нечетимото е null.</summary>
    public static SemanticVersion? Parse(string? text)
    {
        var clean = text?.Trim();
        if (string.IsNullOrEmpty(clean)) return null;

        if (clean[0] is 'v' or 'V') clean = clean[1..];

        return SemanticVersion.TryParse(clean, out var version) ? version : null;
    }

    /// <summary>По-ново ли е първото от второто. Нечетимото никога не е по-ново.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var next = Parse(candidate);
        if (next is null) return false;

        var now = Parse(current);
        return now is null || next.CompareTo(now) > 0;
    }
}

/// <summary>
/// Бележките на изданието като обикновен текст.
///
/// Velopack и GitHub ги дават в Markdown, а надписът в програмата не рисува
/// Markdown. Затова знаците за форматиране се махат, а списъците стават точки
/// — четимо и без нито една библиотека.
/// </summary>
internal static partial class ReleaseNotes
{
    public static string Plain(string? markdown, int maxChars = 700)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var lines = new List<string>();

        // Може ли следващият обикновен ред да продължи предишния. В Markdown
        // пренесеният ред е същият абзац — „- Известия…“ и „  от услугата…“ са
        // една точка, а не две, и надписът ги пренася сам.
        var open = false;

        foreach (var raw in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = raw.Trim();

            if (trimmed.Length == 0)
            {
                lines.Add(string.Empty);
                open = false;
                continue;
            }

            // Правото тире под заглавие („---“) е украса.
            if (trimmed.Length >= 3 && trimmed.All(c => c is '-' or '=' or '*' or '_')) continue;

            // Заглавие „## 1.3.0“ — остава думата, без решетките.
            if (trimmed.StartsWith('#'))
            {
                lines.Add(trimmed.TrimStart('#').Trim());
                open = false;
                continue;
            }

            // Списък: „- “, „* “, „+ “ и номериран „1. “ стават точка.
            if (Bullet().Match(trimmed) is { Success: true } bullet)
            {
                lines.Add("• " + trimmed[bullet.Length..].Trim());
                open = true;
                continue;
            }

            if (open && lines.Count > 0) lines[^1] += " " + trimmed;
            else lines.Add(trimmed);

            open = true;
        }

        // Удебеленото и връзките — след сливането: могат да минават през пренесен ред.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = Link().Replace(lines[i], "$1");
            line = Emphasis().Replace(line, "$2");
            lines[i] = line.Replace("`", string.Empty);
        }

        // Повече от един празен ред подред е празно място, не смисъл.
        var text = Regex.Replace(string.Join("\n", lines), "\n{3,}", "\n\n").Trim();

        if (text.Length > maxChars)
        {
            // Реже се на край на ред, за да не свърши по средата на дума.
            var cut = text.LastIndexOf('\n', maxChars);
            text = (cut > maxChars / 2 ? text[..cut] : text[..maxChars]).TrimEnd() + "\n…";
        }

        return text.Replace("\n", Environment.NewLine);
    }

    [GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    /// <summary>
    /// Удебелено и наклонено — само когато знакът стои на граница на дума.
    /// Иначе имената на таблиците от схемата („r_yr_generated_waste“) губеха
    /// долните си черти: „_yr_“ изглежда точно като наклонено „yr“.
    /// </summary>
    [GeneratedRegex(@"(?<!\w)(\*\*|__|\*|_)(?=\S)(.+?)(?<=\S)\1(?!\w)")]
    private static partial Regex Emphasis();
}
