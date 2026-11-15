namespace RiosvImports.App.Updates;

/// <summary>
/// Откъде идват изданията и новините — едно място за двете.
///
/// Кодът стои в ЧАСТНО хранилище. Изданията и новините — в отделно, ПУБЛИЧНО
/// хранилище, в което няма нищо друго: README, файл с новини и изданията под
/// „Releases“. Така програмата пита GitHub без ключ, а кодът си остава частен.
/// Ключ, вграден в програмата, би отворил и кода — правото „чети съдържанието“
/// в GitHub не различава изданията от изходния код.
///
/// Смяната на адреса е смяна на един ред. Адрес извън GitHub също върви: тогава
/// изданията се търсят като обикновени файлове зад него (Velopack ги чете с
/// SimpleWebSource), а новините — в news.json до тях.
/// </summary>
internal static class UpdateFeed
{
    /// <summary>Публичното хранилище с изданията.</summary>
    public const string Repository = "https://github.com/mugen89/RiosvImports-releases";

    /// <summary>Клонът, от който се чете news.json.</summary>
    private const string Branch = "main";

    private const string GitHubPrefix = "https://github.com/";

    public static bool IsGitHub => Repository.StartsWith(GitHubPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>„собственик/име“ — така го иска GitHub в API-то.</summary>
    public static string Slug => IsGitHub ? Repository[GitHubPrefix.Length..].Trim('/') : string.Empty;

    /// <summary>Новините — обикновен файл в същото хранилище.</summary>
    public static string NewsUrl => IsGitHub
        ? $"https://raw.githubusercontent.com/{Slug}/{Branch}/news.json"
        : Repository.TrimEnd('/') + "/news.json";

    /// <summary>Страницата с всички издания — за човек, не за програмата.</summary>
    public static string ReleasesPage => IsGitHub ? Repository.TrimEnd('/') + "/releases" : Repository;

    /// <summary>Страницата на едно издание. Етикетът е „v“ и номерът — така го прави release.ps1.</summary>
    public static string ReleasePage(string version) => IsGitHub
        ? $"{Repository.TrimEnd('/')}/releases/tag/v{version}"
        : Repository;

    /// <summary>Изданията през API-то, най-новото отгоре. Ползва се, когато програмата не е инсталирана.</summary>
    public static string ReleasesApi => $"https://api.github.com/repos/{Slug}/releases?per_page=20";
}
