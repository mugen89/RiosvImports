namespace RiosvImports.Account;

/// <summary>
/// Устойчив за машината знак, с който сървърът вижда от колко места се ползва
/// един профил.
///
/// Нарочно е обикновен: цели се да се забележи профил, споделен в цял офис,
/// за да се предложи екипен план — не да се спре ползване. Затова не се ровим
/// в хардуерни номера и не се борим с преинсталация; при нова инсталация се
/// появява ново устройство и това е напълно приемливо.
/// </summary>
public static class DeviceId
{
    private static readonly Lazy<string> Value = new(Load);

    public static string Current => Value.Value;

    private static string Load()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RIOSV Imports");

        var path = Path.Combine(folder, "device.id");

        try
        {
            if (File.Exists(path))
            {
                var saved = File.ReadAllText(path).Trim();
                if (Guid.TryParse(saved, out _)) return saved;
            }

            var created = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, created);

            return created;
        }
        catch
        {
            // Ако профилът е само за четене, работим с временен знак: губи се
            // при затваряне, но нищо в програмата не зависи от постоянството му.
            return Guid.NewGuid().ToString("N");
        }
    }
}
