using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using RiosvImports.Core.Localization;

namespace RiosvImports.Account;

/// <summary>
/// Проверява подписаното твърдение за право на ползване.
///
/// Форматът е JWT с ES256 — три части, разделени с точка, кодирани base64url.
/// Проверката е нарочно написана на ръка вместо с готова библиотека: нужното
/// е около петдесет реда, а зависимост по-малко значи и по-малко изненади при
/// обфускация и при пакетиране.
///
/// Тук няма мрежа. Ключът е вграден в програмата, затова проверката работи
/// еднакво добре без връзка — това е цялата причина програмата да не се нуждае
/// от интернет след влизане.
/// </summary>
public static class EntitlementVerifier
{
    /// <summary>
    /// Връща твърдението, ако подписът е валиден. Всяка друга изходна точка е
    /// изключение — невалиден подпис не бива да се различава от липсващ.
    /// </summary>
    public static Entitlement Verify(string token, string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var parts = token.Split('.');
        if (parts.Length != 3)
            throw new InvalidOperationException(Lang.T("Правото на ползване е в неразпознат формат.", "The entitlement is in an unrecognised format."));

        EnsureAlgorithmIsEs256(parts[0]);

        // Подписът покрива точно „заглавие.съдържание“ — в изходния им вид,
        // затова се ползват байтовете на низа, а не прекодиран текст.
        var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        var signature = FromBase64Url(parts[2]);

        using var key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);

        // .NET очаква подписа като r‖s — точно каквото JWT/ES256 записва.
        if (!key.VerifyData(signed, signature, HashAlgorithmName.SHA256))
            throw new InvalidOperationException(Lang.T("Подписът на правото на ползване не е валиден.", "The entitlement signature is not valid."));

        var payload = JsonSerializer.Deserialize<Entitlement>(FromBase64Url(parts[1]))
            ?? throw new InvalidOperationException(Lang.T("Правото на ползване е празно.", "The entitlement is empty."));

        if (payload.ExpiresAtUnix == 0)
            throw new InvalidOperationException(Lang.T("Правото на ползване е без срок.", "The entitlement has no expiry."));

        return payload;
    }

    /// <summary>Като <see cref="Verify"/>, но връща null вместо изключение.</summary>
    public static Entitlement? TryVerify(string? token, string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try { return Verify(token, publicKeyPem); }
        catch { return null; }
    }

    /// <summary>
    /// Алгоритъмът се чете от заглавието и се сверява. Без тази проверка
    /// сървър — или подменен файл — би могъл да поиска „alg: none“.
    /// </summary>
    private static void EnsureAlgorithmIsEs256(string header)
    {
        using var document = JsonDocument.Parse(FromBase64Url(header));

        var algorithm = document.RootElement.TryGetProperty("alg", out var value)
            ? value.GetString()
            : null;

        if (!string.Equals(algorithm, "ES256", StringComparison.Ordinal))
            throw new InvalidOperationException(Lang.T("Правото на ползване е подписано с неочакван алгоритъм.", "The entitlement is signed with an unexpected algorithm."));
    }

    /// <summary>base64url няма пълнеж и ползва други два знака от обикновения base64.</summary>
    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');

        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => "",
            _ => throw new InvalidOperationException(Lang.T("Повредено base64url съдържание.", "Corrupt base64url content."))
        };

        return Convert.FromBase64String(padded);
    }
}
