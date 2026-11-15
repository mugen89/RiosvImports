using System.Globalization;

namespace RiosvImports.Core.Values;

/// <summary>
/// Сметките при разделяне на количество.
///
/// Стои отделно от прозореца, защото тук е мястото, където се греши тихо:
/// три равни дяла от петдесет тона правят 16,667 всеки, а сборът им е 50,001.
/// Схемата приема три знака след запетаята и нито един повече, така че
/// закръглянето не е въпрос на вкус — то трябва да се поеме някъде.
///
/// Живее в „Ядро“, а не при редактора, защото същата аритметика държи и
/// разпределянето по партиди в склада: и там количеството се дели на дялове,
/// и там сборът на дяловете трябва да е точно колкото е влязло. Едно
/// закръгляне на две места е две закръгляния, които се разминават.
/// </summary>
public static class SplitMath
{
    /// <summary>Схемата приема количество с точност до грам.</summary>
    public const int Digits = 3;

    /// <summary>
    /// Разпределя количеството по дадените проценти.
    ///
    /// Всички дялове освен последния се закръглят обичайно; последният получава
    /// остатъка. Така сборът е точно колкото е влязло, а разликата — най-много
    /// няколко грама — стои на едно място, вместо да се размаже навсякъде.
    /// </summary>
    public static List<decimal> Shares(decimal total, IReadOnlyList<decimal> percents)
    {
        var shares = new List<decimal>(percents.Count);
        if (percents.Count == 0) return shares;

        decimal running = 0;

        for (var i = 0; i < percents.Count - 1; i++)
        {
            var share = Round(total * percents[i] / 100m);
            shares.Add(share);
            running += share;
        }

        shares.Add(Round(total - running));

        return shares;
    }

    /// <summary>Какъв дял е количеството от цялото. Нула цяло дава нула.</summary>
    public static decimal Percent(decimal amount, decimal total) =>
        total == 0 ? 0 : Math.Round(amount / total * 100m, 2, MidpointRounding.AwayFromZero);

    public static decimal Round(decimal amount) =>
        Math.Round(amount, Digits, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Чете число, писано и с точка, и със запетая.
    ///
    /// Excel лепи ту едното, ту другото, а клавиатурата на цифровия блок дава
    /// точка независимо от езика на Windows. Отказът да се разбере „12.5“ е
    /// каприз, не строгост.
    /// </summary>
    public static bool TryRead(string? text, out decimal value)
    {
        value = 0;

        var raw = text?.Trim().Replace(' ', char.MinValue).Replace('.', ',');
        if (string.IsNullOrEmpty(raw)) return false;

        return decimal.TryParse(
            raw, NumberStyles.Number, CultureInfo.GetCultureInfo("bg-BG"), out value);
    }

    /// <summary>Количество за показване и за записване — винаги с три знака.</summary>
    public static string Amount(decimal value) =>
        value.ToString("0.000", CultureInfo.GetCultureInfo("bg-BG"));

    public static string Share(decimal value) =>
        value.ToString("0.##", CultureInfo.GetCultureInfo("bg-BG"));
}
