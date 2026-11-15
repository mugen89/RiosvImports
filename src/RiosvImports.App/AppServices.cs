using RiosvImports.Account;
using RiosvImports.Data;

namespace RiosvImports.App;

/// <summary>
/// Едно място, където се сглобяват услугите извън ядрото: профилът, базата и
/// връзката с мрежата.
///
/// Екраните взимат готовия <see cref="Account"/> оттук, без да знаят как е
/// сглобен. Обновяването НЕ е тук: то е на главния прозорец, за да не пита
/// тестът GitHub, когато само сглобява екраните.
/// </summary>
internal static class AppServices
{
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient
    {
        // Повикванията стават при натиснат бутон: по-добре бърза грешка,
        // отколкото програма, която изглежда забита.
        Timeout = TimeSpan.FromSeconds(20)
    });

    /// <summary>
    /// Една връзка за цялата програма — профилът, новините и проверката за
    /// издание. Нова връзка при всяко повикване изчерпва портовете на машината.
    /// </summary>
    public static HttpClient Web => Http.Value;

    private static readonly Lazy<AccountSession> Session = new(() =>
    {
        var options = AccountOptions.Default;

        return new AccountSession(
            new AccountClient(Http.Value, options),
            new TokenStore(),
            options);
    });

    /// <summary>
    /// Профилът и правото на ползване. Докато адресите в
    /// <see cref="AccountOptions.Default"/> не са попълнени, се държи като
    /// „всичко е позволено“, за да не пречи на разработката.
    ///
    /// Примерно ползване от бъдещия екран за вход:
    ///
    ///   await AppServices.Account.SignUpAsync(email, password);
    ///   await AppServices.Account.SignInAsync(email, password);
    ///   if (!AppServices.Account.CanSign) { ... покажи покана за абонамент ... }
    /// </summary>
    public static AccountSession Account => Session.Value;

    /// <summary>
    /// Друга база вместо тази в профила. Задава се от „--db“ при стартиране.
    ///
    /// Съществува заради прегледа: пробната база пътува заедно с програмата и
    /// се отваря, без да се пипа това, което рецензентът има в профила си.
    /// </summary>
    private static string? _path;

    /// <summary>Казва коя база да се отвори. Вика се ПРЕДИ първото обръщение.</summary>
    public static void UseWorkspace(string? path) => _path = path;

    private static readonly Lazy<Workspace> Local = new(() => Workspace.Open(_path));

    /// <summary>
    /// Местната база с работата на потребителя. Отваря се при първото обръщение
    /// и се привежда до последната версия още там.
    /// </summary>
    public static Workspace Workspace => Local.Value;

    public static void Close()
    {
        if (Local.IsValueCreated) Local.Value.Dispose();
    }
}
