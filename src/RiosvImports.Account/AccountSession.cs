using System.Runtime.Versioning;

using RiosvImports.Core.Localization;

namespace RiosvImports.Account;

/// <summary>
/// Състоянието на профила, както го вижда интерфейсът.
///
/// Единствената точка, през която останалата програма пита „може ли“.
/// Проверката е местна и мигновена; мрежата се пипа само при вход и при тихо
/// подновяване, и провалът ѝ никога не пречи на работата.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AccountSession
{
    private readonly AccountClient _client;
    private readonly TokenStore _store;
    private readonly AccountOptions _options;

    private StoredSession? _stored;

    public AccountSession(AccountClient client, TokenStore store, AccountOptions options)
    {
        _client = client;
        _store = store;
        _options = options;

        Reload();
    }

    /// <summary>Твърдението, което важи в момента, или null, ако няма влязъл потребител.</summary>
    public Entitlement? Entitlement { get; private set; }

    public string? Email => _stored?.Email;

    /// <summary>
    /// Докато адресите не са попълнени, програмата работи без ограничения.
    /// Така разработката и тестването не чакат услугата да съществува.
    /// </summary>
    public bool IsGated => _options.IsConfigured;

    public EntitlementState State => !IsGated
        ? EntitlementState.Active
        : Entitlement?.StateAt(DateTimeOffset.UtcNow) ?? EntitlementState.SignedOut;

    /// <summary>
    /// Колко фирми позволява абонаментът в момента.
    ///
    /// Проверката за броя стои тук, а не в базата: хранилището не знае нищо за
    /// абонаменти и не бива да научава.
    /// </summary>
    public int CompanyLimit =>
        Entitlement is { CompanyLimit: > 0 } granted
            ? granted.CompanyLimit
            : AccountOptions.DefaultCompanyLimit;

    /// <summary>Може ли да се подписва — единственото платено действие засега.</summary>
    public bool CanSign => State is EntitlementState.Active or EntitlementState.Grace;

    /// <summary>Изречение за лентата на състоянието.</summary>
    public string Describe() => State switch
    {
        _ when !IsGated => Lang.T("Режим на разработка — без проверка на абонамент.", "Development mode — no subscription check."),
        EntitlementState.SignedOut => Lang.T("Не сте влезли в профила си.", "You are not signed in."),
        EntitlementState.Active when Entitlement!.IsUnlimitedSignatures =>
            Lang.T($"{Entitlement.Email} — абонамент „{Entitlement.Plan}“.", $"{Entitlement.Email} — “{Entitlement.Plan}” subscription."),
        EntitlementState.Active =>
            Lang.T($"{Entitlement!.Email} — пробен период, остават {Entitlement.SignaturesLeft} подписа.", $"{Entitlement!.Email} — trial, {Entitlement.SignaturesLeft} signatures left."),
        EntitlementState.Grace =>
            Lang.T(
                $"Абонаментът изтече на {Entitlement!.ExpiresAt.LocalDateTime:dd.MM.yyyy} г. " +
                "Подновете го, за да продължите да подписвате.",
                $"The subscription ran out on {Entitlement!.ExpiresAt.LocalDateTime:dd.MM.yyyy}. " +
                "Renew it to carry on signing."),
        _ => Lang.T("Абонаментът е изтекъл. Подписването е спряно.", "The subscription has expired. Signing is stopped.")
    };

    // ------------------------------------------------------------- действия

    public Task SignUpAsync(string email, string password, CancellationToken token = default) =>
        _client.SignUpAsync(email, password, token);

    /// <summary>Влиза и веднага взема право на ползване, за да работи после без мрежа.</summary>
    public async Task SignInAsync(string email, string password, CancellationToken token = default)
    {
        var result = await _client.SignInAsync(email, password, token).ConfigureAwait(false);
        await AdoptAsync(result, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Тихо подновяване при стартиране. Никога не хвърля: липсата на мрежа не
    /// е повод програмата да спре да работи.
    /// </summary>
    public async Task<bool> TryRefreshAsync(bool force = false, CancellationToken token = default)
    {
        if (!IsGated || _stored is null) return false;

        if (!force && Entitlement is not null && !Entitlement.ShouldRefreshAt(DateTimeOffset.UtcNow))
            return false;

        try
        {
            var result = await _client
                .RefreshAsync(_stored.RefreshToken, _stored.Email, token).ConfigureAwait(false);

            await AdoptAsync(result, token).ConfigureAwait(false);
            return true;
        }
        catch (AccountException)
        {
            // Изтекъл ключ за подновяване или прекъсната връзка. И в двата
            // случая пазеното твърдение важи, докато не изтече само.
            return false;
        }
    }

    public void SignOut()
    {
        _store.Clear();
        _stored = null;
        Entitlement = null;
    }

    private async Task AdoptAsync(SignInResult result, CancellationToken token)
    {
        var entitlementToken = await _client
            .RequestEntitlementAsync(result.AccessToken, DeviceId.Current, token).ConfigureAwait(false);

        // Проверява се веднага: подписано от чужд ключ твърдение не бива да се записва.
        Entitlement = EntitlementVerifier.Verify(entitlementToken, _options.EntitlementPublicKeyPem);

        _stored = new StoredSession(result.RefreshToken, entitlementToken, result.Email);
        _store.Write(_stored);
    }

    private void Reload()
    {
        _stored = _store.Read();

        Entitlement = _stored is null
            ? null
            : EntitlementVerifier.TryVerify(_stored.EntitlementToken, _options.EntitlementPublicKeyPem);
    }
}
