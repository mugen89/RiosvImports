using System.Globalization;
using System.Text.Json;
using RiosvImports.Core.Values;

namespace RiosvImports.Data;

/// <summary>
/// Как складът се записва в базата и се чете обратно.
///
/// На едно място, защото трите хранилища пишат едни и същи три неща — дата,
/// количество и торба с колони — и разминаване между тях не се вижда веднага:
/// количество, записано с local decimal separator, се чете като друго число на
/// машина с друг език, а салдото просто става грешно.
///
/// Датата е ISO, за да се подрежда по азбучен ред както по време. Количеството
/// е текст с точка — SQLite няма точен десетичен тип, а тонажите се събират и
/// вадят. Четенето минава през <see cref="ValueConverter.TryAmount"/>, който
/// приема и запетая: част от числата ще са дошли от Excel и от човешка ръка.
/// </summary>
internal static class LedgerText
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Датата, както се пази: „2026-09-03“.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Обратното. Нечетимата дата връща null, вместо да хвърля.</summary>
    public static DateOnly? ReadDate(string? text)
    {
        if (DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        // Заварен запис може да носи и час — четенето му е по-добро от загубата му.
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var moment))
            return DateOnly.FromDateTime(moment);

        return null;
    }

    /// <summary>Количеството, както се пази: с точка, независимо от езика.</summary>
    public static string Amount(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Обратното. Нечетимото количество е нула, не изключение.</summary>
    public static decimal ReadAmount(string? text) => ValueConverter.TryAmount(text) ?? 0m;

    /// <summary>Колоните, които не участват в сметка, но пътуват до отчета.</summary>
    public static string Fields(IReadOnlyDictionary<string, string>? fields) =>
        JsonSerializer.Serialize(fields ?? new Dictionary<string, string>(), Json);

    public static Dictionary<string, string> ReadFields(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
}
