using ClosedXML.Excel;
using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Excel;

/// <summary>
/// Четене на месечния дневник, изнесен от „Деметра“.
///
/// Форматът е един лист с шапка на първия ред и по един ред на движение. Не е
/// наш и не се управлява от нас — затова тук се чете и НЕ се тълкува: „Приети
/// от контрагенти“ стига дотук като низ и чак човекът казва на кое наше
/// движение отговаря (<see cref="LedgerImport.Apply"/>).
///
/// Колоните се намират по надпис, а не по буква. Изнасянето от чужда програма
/// мени реда на колоните между версиите си, а надписите — рядко; и когато ги
/// смени, липсващата колона се казва по име, вместо мълчаливо да се прочете
/// съседната.
///
/// Две неща, научени от истински файл (юли 2026, 583 реда):
///
/// 1. Количеството стои в „Общо приети“ за влизащото и в „Общо предадени“ за
///    излизащото — включително за третирането, което е излизащо. Затова
///    изборът на колона е по посока, а не по вид (виж <c>LedgerImport</c>).
/// 2. Площадката е ИМЕ („Склад гара Искър“), а схемата на НИСО иска номер.
///    Съответствието го прави човекът; тук името се предава както е.
/// </summary>
public static class DemetraWorkbook
{
    private const string SiteHeader = "Площадка";
    private const string ValidHeader = "Валиден запис";
    private const string KindHeader = "Вид запис";
    private const string CodeHeader = "Код на отпадъка";
    private const string NameHeader = "Наименование";
    private const string CategoryHeader = "Категория";
    private const string TypeHeader = "Вид";
    private const string DateHeader = "Дата";
    private const string PartnerHeader = "Контрагент";
    private const string EikHeader = "ЕИК/ЕГН";
    private const string ReasonHeader = "Основание";
    private const string InHeader = "Общо приети отпадъци";
    private const string OutHeader = "Общо предадени отпадъци";
    private const string TransportHeader = "Транспорт";

    /// <summary>Стойността, с която „Деметра“ отбелязва редовете, които се броят.</summary>
    private const string ValidValue = "Валиден";

    /// <summary>Колко реда се оглеждат за шапката, преди да се откаже.</summary>
    private const int HeaderSearchRows = 8;

    /// <summary>
    /// Предложеното съответствие — думите на „Деметра“ към нашите движения.
    ///
    /// Предложение, не правило: то се показва в прозорчето и човек може да го
    /// смени. „Образувани“ например значи различни неща на площадка за събиране
    /// и на площадка за оползотворяване.
    ///
    /// Свойство, а не поле: речникът се пълни веднъж и няма нужда да се прави
    /// наново, но статичното поле би било също толкова добре — тук е свойство
    /// само защото е малък и се чете рядко.
    /// </summary>
    public static IReadOnlyDictionary<string, MoveKind> SuggestedKinds =>
        new Dictionary<string, MoveKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["Приети от контрагенти"] = MoveKind.Received,
            ["Приети"] = MoveKind.Received,
            ["Образувани"] = MoveKind.Generated,
            ["Третирани"] = MoveKind.Treated,
            ["Предадени на контрагенти"] = MoveKind.Submitted,
            ["Предадени"] = MoveKind.Submitted
        };

    /// <summary>
    /// Кои видове по подразбиране са пряко предаване.
    ///
    /// На площадка за събиране предаденото излиза от полученото, а не от
    /// образуваното: в истинския юлски дневник са предадени 1189 тона срещу 94
    /// образувани. Прието като образувано, това би вкарало склада на минус
    /// хиляда тона още с първото внасяне.
    /// </summary>
    public static IReadOnlySet<string> SuggestedDirect =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Предадени на контрагенти",
            "Предадени"
        };

    /// <summary>Чете файла. Не хвърля заради съдържание — само заради самия файл.</summary>
    public static ImportFile Read(string path)
    {
        var problems = new List<string>();

        using var workbook = Open(path);

        var sheet = Find(workbook);

        if (sheet is null)
        {
            problems.Add(Lang.T(
                $"Във файла няма лист с колони „{KindHeader}“ и „{CodeHeader}“.",
                $"The file has no sheet with “{KindHeader}” and “{CodeHeader}” columns."));

            return new ImportFile([], problems);
        }

        var (header, columns) = Header(sheet);

        foreach (var wanted in new[] { KindHeader, CodeHeader, DateHeader })
            if (!columns.ContainsKey(wanted))
                problems.Add(Lang.T($"Липсва колона „{wanted}“.", $"The “{wanted}” column is missing."));

        var rows = new List<ImportRow>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? header;

        for (var line = header + 1; line <= last; line++)
        {
            var row = sheet.Row(line);

            string Text(string name) =>
                columns.TryGetValue(name, out var index) ? row.Cell(index).GetString().Trim() : string.Empty;

            var kind = Text(KindHeader);
            var code = Text(CodeHeader);

            // Празен ред по средата е обичаен в изнесен файл — не е проблем.
            if (kind.Length == 0 && code.Length == 0) continue;

            var valid = !columns.ContainsKey(ValidHeader) ||
                        Text(ValidHeader).Length == 0 ||
                        Text(ValidHeader).Equals(ValidValue, StringComparison.CurrentCultureIgnoreCase);

            // Категорията и видът стигат до отчета, ако изобщо са попълнени.
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            if (Text(CategoryHeader) is { Length: > 0 } category) fields["cat_nuba"] = category;
            if (Text(TypeHeader) is { Length: > 0 } type) fields["origin_nuba"] = type;

            rows.Add(new ImportRow(
                line,
                kind,
                Text(SiteHeader),
                code,
                Text(NameHeader),
                Date(row, columns),
                Amount(row, columns, InHeader),
                Amount(row, columns, OutHeader),
                Text(EikHeader),
                Text(PartnerHeader),
                Text(ReasonHeader),
                Text(TransportHeader),
                valid,
                fields));
        }

        if (rows.Count == 0)
            problems.Add(Lang.T("Във файла няма редове с данни.", "The file has no data rows."));

        return new ImportFile(rows, problems);
    }

    // ------------------------------------------------------------- помощни

    private static XLWorkbook Open(string path)
    {
        try
        {
            return new XLWorkbook(path);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(Lang.T(
                $"Файлът не може да се отвори: {ex.Message}",
                $"The file cannot be opened: {ex.Message}"));
        }
    }

    /// <summary>Листът, който изобщо прилича на дневник.</summary>
    private static IXLWorksheet? Find(XLWorkbook workbook)
    {
        foreach (var sheet in workbook.Worksheets)
        {
            var (_, columns) = Header(sheet);

            if (columns.ContainsKey(KindHeader) && columns.ContainsKey(CodeHeader)) return sheet;
        }

        return null;
    }

    /// <summary>
    /// Кой ред е шапката и коя колона къде е.
    ///
    /// Търси се в първите няколко реда, защото изнесеният файл понякога носи
    /// заглавие или празен ред отгоре. Печели редът, който познава най-много
    /// надписи.
    /// </summary>
    private static (int Row, Dictionary<string, int> Columns) Header(IXLWorksheet sheet)
    {
        var best = 0;
        var found = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 0, HeaderSearchRows);

        for (var line = 1; line <= lastRow; line++)
        {
            var here = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

            for (var column = 1; column <= lastColumn; column++)
            {
                var text = sheet.Cell(line, column).GetString().Trim();
                if (text.Length > 0) here.TryAdd(text, column);
            }

            var score = Known.Count(name => here.ContainsKey(name));

            if (score <= found.Count(pair => Known.Contains(pair.Key))) continue;

            best = line;
            found = here;
        }

        return (best, found);
    }

    private static readonly string[] Known =
    [
        SiteHeader, ValidHeader, KindHeader, CodeHeader, NameHeader, DateHeader,
        PartnerHeader, EikHeader, ReasonHeader, InHeader, OutHeader, TransportHeader
    ];

    /// <summary>
    /// Датата от клетка.
    ///
    /// Excel пази датите като числа и изнасянето често не им слага формат —
    /// затова се чете и като дата, и като число, и чак накрая като текст.
    /// </summary>
    private static DateOnly? Date(IXLRow row, Dictionary<string, int> columns)
    {
        if (!columns.TryGetValue(DateHeader, out var index)) return null;

        var cell = row.Cell(index);

        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var moment))
            return DateOnly.FromDateTime(moment);

        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var serial))
        {
            // Excel брои дните от 30.12.1899. Числа под 1 не са дата.
            if (serial >= 1 && serial < 100_000)
                return DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays(Math.Floor(serial)));
        }

        var text = cell.GetString().Trim();

        if (ValueConverter.TryNormalizeDate(text, out var normalized) &&
            DateOnly.TryParseExact(normalized, ValueConverter.BulgarianDateFormat, out var date))
            return date;

        return null;
    }

    private static decimal Amount(IXLRow row, Dictionary<string, int> columns, string header)
    {
        if (!columns.TryGetValue(header, out var index)) return 0m;

        var cell = row.Cell(index);

        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var number))
            return SplitMath.Round((decimal)number);

        return ValueConverter.TryAmount(cell.GetString()) is { } amount ? SplitMath.Round(amount) : 0m;
    }
}
