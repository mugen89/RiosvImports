namespace RiosvImports.Account;

/// <summary>
/// Адресите на услугата и публичният ключ, с който се проверява правото на
/// ползване.
///
/// Стойностите стоят в кода, а не във файл до програмата, нарочно: ако
/// публичният ключ може да се подмени отвън, проверката на правото няма
/// стойност. Смяна на адрес означава ново издание — това е приемливо, защото
/// издания и без това се разпространяват автоматично.
/// </summary>
public sealed record AccountOptions(
    Uri AuthBaseUrl,
    Uri FunctionsBaseUrl,
    string PublicApiKey,
    string EntitlementPublicKeyPem)
{
    /// <summary>
    /// Колко време програмата работи без връзка, преди правото да изтече.
    /// Стойността идва от сървъра в самия подпис; тази тук е само за преценка
    /// кога да се опита тихо подновяване.
    /// </summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(3);

    /// <summary>Колко дни след изтичане програмата само предупреждава, без да заключва.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);

    /// <summary>
    /// Колко фирми се допускат, докато услугата още не отговаря.
    ///
    /// Това е и стойността на най-малкия план. Когато сървърът заработи, тя
    /// идва от подписаното право и това число престава да значи каквото и да е.
    /// </summary>
    public const int DefaultCompanyLimit = 3;

    /// <summary>
    /// ПОПЪЛВА СЕ ПРИ ПУСКАНЕ НА УСЛУГАТА.
    ///
    /// Адресите са в устройството на Supabase: /auth/v1 за регистрация и вход,
    /// /functions/v1 за издаването на правото. Ако по-късно се сложи собствен
    /// сървър отпред, се сменят само тези два адреса.
    ///
    /// Публичният ключ се получава от tools/new-signing-key.ps1 — частната му
    /// половина остава на сървъра и не влиза в хранилището.
    /// </summary>
    public static AccountOptions Default { get; } = new(
        AuthBaseUrl: new Uri("https://ПОПЪЛНИ.supabase.co/auth/v1/"),
        FunctionsBaseUrl: new Uri("https://ПОПЪЛНИ.supabase.co/functions/v1/"),
        PublicApiKey: "ПОПЪЛНИ-anon-key",
        EntitlementPublicKeyPem: """
            -----BEGIN PUBLIC KEY-----
            ПОПЪЛНИ
            -----END PUBLIC KEY-----
            """);

    /// <summary>
    /// Дали адресите са попълнени. Докато не са, програмата работи както
    /// досега — без вход и без ограничения — за да може да се разработва и
    /// тества, преди услугата да съществува.
    /// </summary>
    public bool IsConfigured =>
        !AuthBaseUrl.Host.Contains("ПОПЪЛНИ", StringComparison.Ordinal) &&
        !EntitlementPublicKeyPem.Contains("ПОПЪЛНИ", StringComparison.Ordinal);
}
