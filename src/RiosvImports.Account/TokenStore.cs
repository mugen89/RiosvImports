using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RiosvImports.Account;

/// <summary>Каквото програмата помни между стартиранията за влезлия потребител.</summary>
public sealed record StoredSession(
    string RefreshToken,
    string EntitlementToken,
    string Email);

/// <summary>
/// Пази данните за влизане в профила на потребителя, защитени с DPAPI.
///
/// DPAPI връзва шифроването с акаунта в Windows: копиран на друга машина или
/// отворен от друг потребител, файлът не се разчита. Това не пази от самия
/// собственик на машината — и не се и опитва. Целта е файлът да не е полезен,
/// ако попадне другаде.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TokenStore
{
    /// <summary>Допълнителен вход към DPAPI — вързва шифрограмата за тази програма.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RIOSV Imports / account / v1");

    private readonly string _path;

    public TokenStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RIOSV Imports",
            "account.dat");
    }

    public StoredSession? Read()
    {
        if (!File.Exists(_path)) return null;

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<StoredSession>(plain);
        }
        catch
        {
            // Повреден или пренесен от друга машина файл — все едно го няма.
            // Потребителят просто влиза наново.
            return null;
        }
    }

    public void Write(StoredSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var protectedBytes = ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(session), Entropy, DataProtectionScope.CurrentUser);

        // Пише се през временен файл, за да не остане половин запис при спиране на тока.
        var temporary = _path + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, _path, overwrite: true);
    }

    public void Clear()
    {
        try { File.Delete(_path); } catch { /* няма какво да се направи */ }
    }
}
