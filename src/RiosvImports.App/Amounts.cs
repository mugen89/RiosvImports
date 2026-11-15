using System.Globalization;
using RiosvImports.Core.Localization;

namespace RiosvImports.App;

/// <summary>
/// Как се изписва количество на екрана.
///
/// На едно място, защото едно и също число се показва в таблицата, в надписа
/// под ситото, в диаграмите и в изнесения файл. Различен брой знаци на две
/// места кара човек да търси разлика, каквато няма.
/// </summary>
internal static class Amounts
{
    /// <summary>Числата се пишат по български: запетая за десетичен знак.</summary>
    private static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");

    /// <summary>Пълното количество — до грам, както го приема схемата.</summary>
    public static string Text(decimal value) => value.ToString("0.###", Bulgarian);

    /// <summary>Количество в клетка: нулата е празна, иначе таблицата е стена от нули.</summary>
    public static string Cell(decimal value) => value == 0 ? string.Empty : Text(value);

    /// <summary>
    /// Кратко число за тесни места — оста на диаграма, средата на пръстен.
    ///
    /// Хилядите се съкращават, а знаците след запетаята падат: там се чете
    /// големина, не стойност. Точното число си стои в таблицата.
    /// </summary>
    public static string Short(decimal value)
    {
        var size = Math.Abs(value);

        if (size >= 1_000_000) return Round(value / 1_000_000) + " " + Lang.T("млн.", "M");
        if (size >= 10_000) return Round(value / 1_000) + " " + Lang.T("хил.", "k");

        // До хиляда се пази един знак: „224“ вместо „224,3“ разминава едрото
        // число на плочката с надписа под ситото, а те стоят на един екран.
        if (size >= 1_000) return Math.Round(value).ToString("0", Bulgarian);

        return value.ToString("0.#", Bulgarian);
    }

    private static string Round(decimal value) =>
        Math.Round(value, 1).ToString("0.#", Bulgarian);
}
