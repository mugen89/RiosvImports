using System.Text.RegularExpressions;
using RiosvImports.Core.Schema;

namespace RiosvImports.Core.Localization;

/// <summary>
/// Заглавията на отчетите, както се показват в програмата.
///
/// Схемите вече носят пълните български наименования в коментари, затова тук
/// не се измисля нищо ново — само се подрежда. Пълното заглавие е дълго ред и
/// половина и не става за списък; краткото е същото, без повтарящото се начало
/// и без номера на приложението, който се показва отделно.
///
/// В интерфейса не се показва нито едно име от схемата. Имената като
/// „r_yr_generated_waste“ съществуват само вътре в XML файла, който получава
/// НИСО.
/// </summary>
public static partial class ReportTitles
{
    /// <summary>Пълното заглавие — за подсказка и за заглавната лента.</summary>
    public static string Full(ReportNode report) =>
        string.IsNullOrWhiteSpace(report.Label) ? report.ElementName : report.Label.Trim();

    /// <summary>
    /// Кратко заглавие за списък: без „Годишен отчет за“ и без скобата с
    /// приложението. Първата буква става главна, защото след отрязването
    /// изречението започва от средата.
    /// </summary>
    public static string Short(ReportNode report)
    {
        var text = Full(report);

        text = LeadIn().Replace(text, string.Empty);
        text = Appendix().Replace(text, string.Empty);
        text = Whitespace().Replace(text, " ").Trim(' ', ',', '.', ';');

        if (text.Length == 0) return Full(report);

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>Номерът на приложението, ако заглавието го споменава — „Прил. 9“.</summary>
    public static string? Appendix(ReportNode report)
    {
        var match = AppendixNumber().Match(Full(report));
        return match.Success ? "Прил. " + match.Groups[1].Value.Trim() : null;
    }

    /// <summary>Как се именува нов отчет, преди потребителят да го преименува.</summary>
    public static string SuggestDocumentName(ReportNode report, int year) =>
        $"{Short(report)} — {year} г.";

    /// <summary>
    /// Надписът на една таблица от отчета — това, което пише на раздела ѝ.
    ///
    /// Едно и също място за екрана и за съобщенията при проверка: човек, който
    /// чете „поправи нещо в „Получен отпадък““, трябва да намери раздел с
    /// точно това име, а не да гадае кое е „r_b_rd_received“.
    /// </summary>
    public static string Table(ReportNode node) =>
        string.IsNullOrWhiteSpace(node.Label) ? node.ElementName : node.Label;

    /// <summary>Надписът на една колона. Схемата има предимство пред резервния списък.</summary>
    public static string Column(ColumnDef column) =>
        FieldLabels.Resolve(column.Name, column.Label);

    /// <summary>
    /// Кратък надпис за заглавен ред на таблица. Дългите описания се режат до
    /// първото тире или скоба — пълният текст остава като подсказка.
    /// </summary>
    public static string ColumnShort(ColumnDef column)
    {
        // Няколко полета имат собствен кратък надпис: изречението от схемата
        // не се реже добре, защото в него няма нито тире, нито скоба.
        if (FieldLabels.Shorter(column.Name) is { } shorthand) return shorthand;

        var text = Column(column);
        var cut = text.IndexOfAny(['—', '(', ',']);

        if (cut > 12) text = text[..cut];

        return Whitespace().Replace(text, " ").Trim(' ', '-', '—', '.');
    }

    [GeneratedRegex(@"^\s*(Годишен|Тримесечен|Месечен)?\s*отчет\s+(за|на)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadIn();

    [GeneratedRegex(@"[\(\.,]?\s*(и\s+)?(Годишен отчет.*)?Приложение\s*№?\s*[\d\-–аА-яЯ\s,и]*\)?", RegexOptions.IgnoreCase)]
    private static partial Regex Appendix();

    [GeneratedRegex(@"Приложение\s*№?\s*([\d]+\s*[а-яА-Я]?)", RegexOptions.IgnoreCase)]
    private static partial Regex AppendixNumber();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Whitespace();
}
