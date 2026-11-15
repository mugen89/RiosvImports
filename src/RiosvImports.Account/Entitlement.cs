using System.Text.Json.Serialization;

namespace RiosvImports.Account;

/// <summary>
/// Какво е правото на ползване в момента. Стойността се пресмята само от
/// подписаното твърдение и часовника — без мрежа.
/// </summary>
public enum EntitlementState
{
    /// <summary>Няма влязъл потребител.</summary>
    SignedOut,

    /// <summary>Има активен абонамент или пробен период.</summary>
    Active,

    /// <summary>Срокът е изтекъл, но гратисният период още тече — само предупреждение.</summary>
    Grace,

    /// <summary>Срокът е изтекъл и гратисният период е минал — платените функции са заключени.</summary>
    Expired
}

/// <summary>
/// Твърдението на сървъра за един потребител: кой е, какво е платил и докога
/// това твърдение важи. Идва подписано и се проверява на място.
/// </summary>
public sealed record Entitlement
{
    [JsonPropertyName("sub")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    /// <summary>„trial“, „monthly“, „yearly“ — както ги нарича сървърът.</summary>
    [JsonPropertyName("plan")]
    public string Plan { get; init; } = string.Empty;

    /// <summary>Кои функции са отключени. Празен списък означава само безплатните.</summary>
    [JsonPropertyName("features")]
    public string[] Features { get; init; } = [];

    /// <summary>
    /// Колко подписа остават в пробния период. Отрицателна стойност означава
    /// „без ограничение“ — така платените планове не носят брояч.
    /// </summary>
    [JsonPropertyName("signatures_left")]
    public int SignaturesLeft { get; init; } = -1;

    /// <summary>Кога изтича самото твърдение — секунди от 1970 г.</summary>
    [JsonPropertyName("exp")]
    public long ExpiresAtUnix { get; init; }

    /// <summary>Кога е издадено — за да се разбере кога си струва подновяване.</summary>
    [JsonPropertyName("iat")]
    public long IssuedAtUnix { get; init; }

    /// <summary>
    /// Колко фирми позволява абонаментът.
    ///
    /// Един счетоводител води по няколко задължени лица и точно това е
    /// разликата между плановете. Стойността идва от сървъра; докато услугата
    /// не е пусната, важи <see cref="AccountOptions.DefaultCompanyLimit"/>.
    /// </summary>
    [JsonPropertyName("companies")]
    public int CompanyLimit { get; init; }

    /// <summary>Версията на правилата, с която сървърът очаква да се работи.</summary>
    [JsonPropertyName("rules_version")]
    public string RulesVersion { get; init; } = string.Empty;

    [JsonIgnore]
    public DateTimeOffset ExpiresAt => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix);

    [JsonIgnore]
    public DateTimeOffset IssuedAt => DateTimeOffset.FromUnixTimeSeconds(IssuedAtUnix);

    [JsonIgnore]
    public bool IsUnlimitedSignatures => SignaturesLeft < 0;

    /// <summary>Състоянието спрямо подадения момент — гратисният период е включен.</summary>
    public EntitlementState StateAt(DateTimeOffset now)
    {
        if (now < ExpiresAt) return EntitlementState.Active;
        return now < ExpiresAt + AccountOptions.GracePeriod
            ? EntitlementState.Grace
            : EntitlementState.Expired;
    }

    /// <summary>Дали си струва да се опита подновяване по мрежата.</summary>
    public bool ShouldRefreshAt(DateTimeOffset now) =>
        now >= IssuedAt + AccountOptions.RefreshAfter;

    public bool Has(string feature) =>
        Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
}
