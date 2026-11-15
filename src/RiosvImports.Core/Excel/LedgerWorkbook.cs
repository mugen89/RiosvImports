using ClosedXML.Excel;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;

namespace RiosvImports.Core.Excel;

/// <summary>Прочетеното от наш шаблон: редовете и това, което не се е разчело.</summary>
public sealed record LedgerSheetFile(
    IReadOnlyList<Dictionary<string, string>> Rows,
    IReadOnlyList<string> Problems);

/// <summary>
/// Нашият шаблон за движения — един лист, една таблица.
///
/// Колоните НЕ се изброяват тук. Те идват от същия <see cref="SheetPlan"/>, по
/// който се прави и таблицата на екрана, тоест в крайна сметка от схемата на
/// НИСО (правило 1). Затова изнесеното, залепеното и въведеното на ръка имат
/// едни и същи колони по устройство, а не по внимание — и добавена от НИСО
/// колона се появява и в трите наведнъж.
///
/// Устройството на листа повтаря шаблоните за отчети: ред 1 са българските
/// надписи, ред 2 са техническите имена, данните тръгват от ред 3. Човек чете
/// първия ред, програмата — втория, и преместена колона не разваля нищо.
/// </summary>
public static class LedgerWorkbook
{
    public const string DataSheet = "ДВИЖЕНИЯ";

    public const int LabelRow = 1;
    public const int HeaderRow = 2;
    public const int FirstDataRow = 3;

    /// <summary>Колко реда се оглеждат за шапката, преди да се откаже.</summary>
    private const int HeaderSearchRows = 8;

    /// <summary>
    /// Записва шаблон — празен или с вече въведеното.
    ///
    /// Един и същ файл служи за двете: шаблон за попълване и извадка за
    /// поправяне в Excel. Разликата е само в това дали има редове, а два
    /// различни файла за едно и също щяха да се разминат.
    /// </summary>
    public static void Save(
        string path,
        SheetPlan plan,
        IReadOnlyList<(string Name, string Header)> extras,
        IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(DataSheet);

        var columns = Columns(plan, extras);

        for (var i = 0; i < columns.Count; i++)
        {
            var (name, header, required) = columns[i];

            var label = sheet.Cell(LabelRow, i + 1);
            label.Value = required ? header + " *" : header;
            label.Style.Font.Bold = true;
            label.Style.Fill.BackgroundColor = required ? XLColor.MistyRose : XLColor.WhiteSmoke;

            var technical = sheet.Cell(HeaderRow, i + 1);
            technical.Value = name;
            technical.Style.Font.FontColor = XLColor.Gray;
            technical.Style.Font.FontSize = 8;
        }

        var line = FirstDataRow;

        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
                if (row.TryGetValue(columns[i].Name, out var value) && value.Length > 0)
                    sheet.Cell(line, i + 1).SetValue(value);

            line++;
        }

        sheet.SheetView.FreezeRows(HeaderRow);

        // Изрично от първата до последната НАША колона: без граници ClosedXML
        // подравнява всички шестнайсет хиляди колони на листа и записът увисва.
        sheet.Columns(1, columns.Count).AdjustToContents(LabelRow, Math.Max(line, FirstDataRow), 10, 42);

        workbook.SaveAs(path);
    }

    /// <summary>
    /// Чете попълнен шаблон.
    ///
    /// Търпеливо по същите причини, по които е търпеливо и четенето на
    /// отчетните шаблони: файлът е минал през човешка ръка. Шапката се търси в
    /// първите няколко реда, познава се и по техническите имена, и по
    /// надписите, а колона, която схемата не познава, се пропуска мълчаливо —
    /// хората добавят свои колони за собствени нужди.
    /// </summary>
    public static LedgerSheetFile Read(
        string path,
        SheetPlan plan,
        IReadOnlyList<(string Name, string Header)> extras)
    {
        var problems = new List<string>();
        var columns = Columns(plan, extras);

        using var workbook = Open(path);

        var sheet = workbook.Worksheets.FirstOrDefault(s =>
                        s.Name.Equals(DataSheet, StringComparison.CurrentCultureIgnoreCase))
                    ?? workbook.Worksheets.FirstOrDefault();

        if (sheet is null)
        {
            problems.Add(Lang.T("Файлът няма нито един лист.", "The file has no sheets."));
            return new LedgerSheetFile([], problems);
        }

        var (header, found) = Header(sheet, columns);

        if (found.Count == 0)
        {
            problems.Add(Lang.T(
                "В листа няма разпознати колони. Изнесете шаблон и попълнете него.",
                "No known columns were found. Export a template and fill that in."));

            return new LedgerSheetFile([], problems);
        }

        var rows = new List<Dictionary<string, string>>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? header;

        for (var line = header + 1; line <= last; line++)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (name, index) in found)
            {
                var text = Text(sheet.Cell(line, index));
                if (text.Length > 0) values[name] = text;
            }

            // Празен ред по средата е обичаен — не е проблем, просто не е ред.
            if (values.Count > 0) rows.Add(values);
        }

        if (rows.Count == 0)
            problems.Add(Lang.T("В листа няма попълнени редове.", "The sheet has no filled rows."));

        return new LedgerSheetFile(rows, problems);
    }

    // ------------------------------------------------------------- помощни

    /// <summary>Колоните на листа: тези от плана, после нашите служебни.</summary>
    private static List<(string Name, string Header, bool Required)> Columns(
        SheetPlan plan, IReadOnlyList<(string Name, string Header)> extras)
    {
        var columns = plan.Columns
            .Select(c => (c.Name, Header: ReportTitles.ColumnShort(c), c.Required))
            .ToList();

        columns.AddRange(extras.Select(e => (e.Name, e.Header, false)));

        return columns;
    }

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

    /// <summary>
    /// Кой ред е шапката и коя колона къде е.
    ///
    /// Печели редът, който познава най-много колони. Пробва се и по техническо
    /// име, и по надпис: човек, който е направил файла на ръка по образец,
    /// обикновено е преписал надписите, а не имената отдолу.
    /// </summary>
    private static (int Row, Dictionary<string, int> Found) Header(
        IXLWorksheet sheet, List<(string Name, string Header, bool Required)> columns)
    {
        var best = HeaderRow;
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        // Двата реда на шапката се познават еднакво добре: ред 1 носи надписите,
        // ред 2 — техническите имена, и за всяка колона има и двете. Затова
        // печели редът с повече ТЕХНИЧЕСКИ съвпадения, а не с повече съвпадения
        // изобщо — иначе се избира редът с надписите, а редът с имената под него
        // става първи ред с данни.
        var bestExact = -1;
        var bestTotal = -1;

        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 0, HeaderSearchRows);

        for (var line = 1; line <= lastRow; line++)
        {
            var here = new Dictionary<string, int>(StringComparer.Ordinal);
            var exact = 0;

            for (var column = 1; column <= lastColumn; column++)
            {
                var text = Text(sheet.Cell(line, column)).TrimEnd('*', ' ');
                if (text.Length == 0) continue;

                var byName = columns.FirstOrDefault(c =>
                    string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase));

                if (byName.Name is { Length: > 0 })
                {
                    if (here.TryAdd(byName.Name, column)) exact++;
                    continue;
                }

                var byLabel = columns.FirstOrDefault(c =>
                    string.Equals(c.Header, text, StringComparison.CurrentCultureIgnoreCase));

                if (byLabel.Name is { Length: > 0 }) here.TryAdd(byLabel.Name, column);
            }

            if (exact < bestExact) continue;
            if (exact == bestExact && here.Count <= bestTotal) continue;

            best = line;
            found = here;
            bestExact = exact;
            bestTotal = here.Count;
        }

        return (best, found);
    }

    /// <summary>
    /// Текстът на клетка, с датите и числата такива, каквито се четат.
    ///
    /// Датата се изписва по български, защото надолу я чака
    /// <c>ValueConverter</c>, който точно това очаква; числото — без
    /// експонента, каквато Excel слага на дребните количества.
    /// </summary>
    private static string Text(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var moment))
            return moment.ToString(Values.ValueConverter.BulgarianDateFormat);

        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var number))
            return ((decimal)number).ToString("0.#####", System.Globalization.CultureInfo.CurrentCulture);

        return cell.GetString().Trim();
    }
}
