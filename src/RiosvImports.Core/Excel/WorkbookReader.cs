using ClosedXML.Excel;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;

using RiosvImports.Core.Localization;

namespace RiosvImports.Core.Excel;

/// <summary>Един прочетен ред от Excel, преди валидация.</summary>
public sealed class RawRow
{
    public required int ExcelRow { get; init; }
    public required IReadOnlyDictionary<string, CellValue> Cells { get; init; }

    public CellValue Get(string column) =>
        Cells.TryGetValue(column, out var value) ? value : CellValue.Empty;

    public bool IsBlank => Cells.Values.All(c => c.IsEmpty);
}

public sealed class SheetData
{
    public required SheetPlan Plan { get; init; }
    public required IReadOnlyList<RawRow> Rows { get; init; }

    /// <summary>Колони от листа, които не отговарят на нито едно поле от схемата.</summary>
    public required IReadOnlyList<string> UnknownColumns { get; init; }
}

public sealed class ParsedWorkbook
{
    public required SchemaDefinition Schema { get; init; }
    public required IReadOnlyDictionary<string, CellValue> Config { get; init; }
    public required IReadOnlyList<SheetData> Sheets { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public IReadOnlyList<ReportNode> Reports =>
        Sheets.Select(s => s.Plan.Report).Distinct().ToList();
}

/// <summary>
/// Чете попълнената работна книга. Разчита на скрития лист „_МЕТА“, но ако той
/// липсва (например файлът е правен на ръка), познава схемата и таблиците по
/// имената на листовете.
/// </summary>
public sealed class WorkbookReader
{
    private const int MaxHeaderSearchRows = 8;

    private static readonly string[] ConfigSheetNames =
    [
        WorkbookLayout.ConfigSheet, "CONFIG", "Настройки", "КОНФИГУРАЦИЯ"
    ];

    private readonly IReadOnlyList<SchemaDefinition> _schemas;

    public WorkbookReader(IReadOnlyList<SchemaDefinition> schemas) => _schemas = schemas;

    public ParsedWorkbook Read(string path)
    {
        using var wb = Open(path);
        var warnings = new List<string>();

        var meta = ReadMeta(wb);
        var schema = DetectSchema(wb, meta, warnings);
        var layout = BuildLayoutFor(wb, schema, meta, warnings);

        var sheets = new List<SheetData>();
        foreach (var plan in layout.Sheets)
        {
            if (!wb.TryGetWorksheet(plan.SheetName, out var ws)) continue;
            sheets.Add(ReadSheet(ws, plan, warnings));
        }

        return new ParsedWorkbook
        {
            Schema = schema,
            Config = ReadConfig(wb, schema, warnings),
            Sheets = sheets,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Отваря файла и превежда типичните повреди на разбираем език. Без това
    /// потребителят вижда суровото съобщение на библиотеката, което не помага.
    /// </summary>
    private static XLWorkbook Open(string path)
    {
        try
        {
            return new XLWorkbook(path);
        }
        catch (IOException ex)
        {
            throw new InvalidDataException(
                $"Файлът „{Path.GetFileName(path)}“ не може да бъде отворен, защото се ползва от " +
                "друга програма.\r\n\r\nЗатворете го в Excel и опитайте отново.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Файлът „{Path.GetFileName(path)}“ не може да бъде прочетен като работна книга на Excel.\r\n\r\n" +
                "Най-честата причина е изтрит или разместен ред със заглавия. В листовете с данни " +
                "ред 1 е описанието на български, ред 2 са техническите имена на полетата, а данните " +
                "започват от ред 3 — тези два реда не бива да се изтриват.\r\n\r\n" +
                "Ако файлът е бил променян силно, направете нов шаблон и прехвърлете данните в него.", ex);
        }
    }

    // ------------------------------------------------------ разпознаване

    private SchemaDefinition DetectSchema(XLWorkbook wb, IReadOnlyList<MetaRow> meta, List<string> warnings)
    {
        var key = meta.FirstOrDefault()?.Schema;
        if (key is not null)
        {
            var hit = _schemas.FirstOrDefault(s =>
                string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }

        var names = wb.Worksheets.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var best = _schemas
            .Select(s => (Schema: s, Score: CountMatches(s, names)))
            .OrderByDescending(x => x.Score)
            .First();

        if (best.Score == 0)
        {
            throw new InvalidDataException(
                Lang.T(
                    "Във файла няма нито един лист, който да отговаря на известна таблица от схемите. " +
                    "Уверете се, че сте отворили шаблон, генериран от програмата.",
                    "The file has no sheet matching any known table from the schemas. " +
                    "Make sure you opened a template generated by the program."));
        }

        warnings.Add(
            $"Липсва служебният лист „{WorkbookLayout.MetaSheet}“ — схемата е разпозната по имената " +
            $"на листовете като „{best.Schema.Title}“.");

        return best.Schema;
    }

    private static int CountMatches(SchemaDefinition schema, HashSet<string> sheetNames) =>
        schema.Reports
              .SelectMany(r => r.SelfAndDescendants())
              .Where(n => n.HasData)
              .Count(n => sheetNames.Contains(n.ElementName));

    /// <summary>Един ред от служебния лист „_МЕТА“.</summary>
    private sealed record MetaRow(string Schema, string Sheet, string Report, string Node);

    private static IReadOnlyList<MetaRow> ReadMeta(XLWorkbook wb)
    {
        if (!wb.TryGetWorksheet(WorkbookLayout.MetaSheet, out var ws)) return [];

        var rows = new List<MetaRow>();
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (var row = 2; row <= last; row++)
        {
            var schema = ws.Cell(row, 1).GetString().Trim();
            var sheet = ws.Cell(row, 2).GetString().Trim();
            var report = ws.Cell(row, 3).GetString().Trim();
            var node = ws.Cell(row, 4).GetString().Trim();

            if (schema.Length == 0 || sheet.Length == 0 ||
                report.Length == 0 || node.Length == 0) continue;

            rows.Add(new MetaRow(schema, sheet, report, node));
        }

        return rows;
    }

    /// <summary>
    /// Възстановява плана на книгата: кои отчети присъстват и кой лист на коя
    /// таблица отговаря. „_МЕТА“ е точният източник — имената на листовете сами
    /// по себе си не стигат, защото една и съща подтаблица (например „r_b_notes“)
    /// се среща в шест различни отчета.
    /// </summary>
    private static WorkbookLayout BuildLayoutFor(
        XLWorkbook wb, SchemaDefinition schema, IReadOnlyList<MetaRow> meta, List<string> warnings)
    {
        var fromMeta = BuildLayoutFromMeta(schema, meta);
        if (fromMeta is not null) return fromMeta;

        if (meta.Count > 0)
        {
            warnings.Add(
                $"Служебният лист „{WorkbookLayout.MetaSheet}“ не отговаря на схемата " +
                $"„{schema.Title}“ — отчетите са разпознати по имената на листовете.");
        }

        var names = wb.Worksheets.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Отчетът присъства само ако е налице собственият му лист. Иначе лист с
        // общо име като „r_b_notes“ би „включил“ всеки отчет, който има такава
        // подтаблица, и програмата би искала от него основен лист, какъвто няма.
        var present = schema.Reports
            .Where(r => r.HasData
                ? ContainsSheetFor(names, r, r)
                : r.SelfAndDescendants().Any(n => n.HasData && ContainsSheetFor(names, r, n)))
            .ToList();

        return WorkbookLayout.Build(schema, present);
    }

    /// <summary>
    /// Строи плана по „_МЕТА“. Връща null, ако листът е от друга схема или е
    /// повреден — тогава се минава на разпознаване по имена.
    /// </summary>
    private static WorkbookLayout? BuildLayoutFromMeta(
        SchemaDefinition schema, IReadOnlyList<MetaRow> meta)
    {
        var usable = meta
            .Where(m => string.Equals(m.Schema, schema.Key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (usable.Count == 0) return null;

        var reports = new List<ReportNode>();
        foreach (var name in usable.Select(m => m.Report).Distinct(StringComparer.Ordinal))
        {
            var report = schema.FindReport(name);
            if (report is null) return null;
            reports.Add(report);
        }

        var names = new Dictionary<(string, string), string>();
        foreach (var m in usable)
            names.TryAdd((m.Report, m.Node), m.Sheet);

        return WorkbookLayout.Build(schema, reports, names);
    }

    private static bool ContainsSheetFor(HashSet<string> names, ReportNode report, ReportNode node) =>
        names.Contains(node.ElementName) ||
        names.Contains(Trim31($"{report.ElementName}_{node.ElementName}"));

    private static string Trim31(string value) => value.Length <= 31 ? value : value[..31];

    // ------------------------------------------------------------ настройки

    private static IReadOnlyDictionary<string, CellValue> ReadConfig(
        XLWorkbook wb, SchemaDefinition schema, List<string> warnings)
    {
        var config = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);

        IXLWorksheet? ws = null;
        foreach (var name in ConfigSheetNames)
            if (wb.TryGetWorksheet(name, out ws)) break;

        if (ws is null)
        {
            warnings.Add($"Липсва лист „{WorkbookLayout.ConfigSheet}“ — общите данни на отчета не са прочетени.");
            return config;
        }

        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            var field = ws.Cell(row, 1).GetString().Trim();
            if (field.Length == 0) continue;
            config[field] = ToCellValue(ws.Cell(row, 2));
        }

        foreach (var attr in schema.RootAttributes.Where(a => a.Required))
        {
            if (!config.TryGetValue(attr.Name, out var value) || value.IsEmpty)
                warnings.Add($"В лист „{ws.Name}“ не е попълнено задължителното поле „{attr.Name}“.");
        }

        return config;
    }

    // --------------------------------------------------------------- данни

    private static SheetData ReadSheet(IXLWorksheet ws, SheetPlan plan, List<string> warnings)
    {
        var expected = plan.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var headerRow = FindHeaderRow(ws, expected.Keys);

        if (headerRow is null)
        {
            warnings.Add(
                $"В лист „{ws.Name}“ не е намерен ред със заглавия на колоните — листът е пропуснат. " +
                "Ред 2 трябва да съдържа техническите имена на полетата (stagenum, origin и т.н.), " +
                "както идват в шаблона.");
            return new SheetData { Plan = plan, Rows = [], UnknownColumns = [] };
        }

        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();

        for (var col = 1; col <= lastColumn; col++)
        {
            var name = ws.Cell(headerRow.Value, col).GetString().Trim();
            if (name.Length == 0) continue;

            if (expected.ContainsKey(name))
            {
                map.TryAdd(name, col);
            }
            else if (!name.StartsWith('_'))
            {
                unknown.Add(name);
            }
        }

        foreach (var column in plan.Columns.Where(c => c.Required && !map.ContainsKey(c.Name)))
            warnings.Add($"В лист „{ws.Name}“ липсва задължителната колона „{column.Name}“.");

        var rows = new List<RawRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow.Value;

        for (var row = headerRow.Value + 1; row <= lastRow; row++)
        {
            var cells = new Dictionary<string, CellValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, col) in map)
                cells[name] = ToCellValue(ws.Cell(row, col));

            var raw = new RawRow { ExcelRow = row, Cells = cells };
            if (raw.IsBlank) continue;

            rows.Add(raw);
        }

        return new SheetData { Plan = plan, Rows = rows, UnknownColumns = unknown };
    }

    /// <summary>
    /// Търси реда със заглавия — този, в който най-много клетки съвпадат с
    /// имената на полетата от схемата.
    /// </summary>
    private static int? FindHeaderRow(IXLWorksheet ws, IEnumerable<string> expected)
    {
        var wanted = expected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastColumn == 0) return null;

        var bestRow = 0;
        var bestScore = 0;

        for (var row = 1; row <= MaxHeaderSearchRows; row++)
        {
            var score = 0;
            for (var col = 1; col <= lastColumn; col++)
            {
                if (wanted.Contains(ws.Cell(row, col).GetString().Trim())) score++;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestRow = row;
            }
        }

        return bestScore > 0 ? bestRow : null;
    }

    private static CellValue ToCellValue(IXLCell cell)
    {
        if (cell.IsEmpty()) return CellValue.Empty;

        DateTime? date = null;
        double? number = null;

        switch (cell.DataType)
        {
            case XLDataType.DateTime when cell.TryGetValue<DateTime>(out var d):
                date = d;
                break;

            case XLDataType.Number when cell.TryGetValue<double>(out var n):
                number = n;
                // Клетка с формат за дата се чете и като дата.
                if (cell.Style.NumberFormat.Format.Contains('y', StringComparison.OrdinalIgnoreCase) &&
                    n is > 0 and < 2_958_466)
                {
                    try { date = DateTime.FromOADate(n); }
                    catch (ArgumentException) { /* остава само числото */ }
                }
                break;
        }

        return new CellValue(cell.GetFormattedString().Trim(), date, number);
    }
}
