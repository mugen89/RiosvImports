using System.Xml.Linq;
using RiosvImports.Core.Excel;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;
using RiosvImports.Core.Xml;

namespace RiosvImports.Core.Import;

/// <summary>
/// Превръща прочетената работна книга в XML за НИСО: проверява всеки ред,
/// свързва подтаблиците с отчетите по stagenum и накрая проверява резултата
/// срещу самите XSD файлове.
/// </summary>
public sealed class ImportProcessor
{
    private readonly XsdValidator _validator;

    public ImportProcessor(XsdValidator validator) => _validator = validator;

    public ImportResult Process(ParsedWorkbook workbook)
    {
        var schema = workbook.Schema;
        XNamespace ns = schema.TargetNamespace;

        var general = new List<Issue>();
        foreach (var warning in workbook.Warnings)
            general.Add(new Issue(IssueSeverity.Warning, string.Empty, null, warning));

        var root = new XElement(ns + "reports");
        WriteRootAttributes(root, schema, workbook, general);

        var sheetsByNode = workbook.Sheets.ToDictionary(s => s.Plan.Node, s => s);
        var outcomes = new List<ReportOutcome>();

        // Редът на отчетите следва реда в схемата — xs:sequence го изисква.
        foreach (var report in schema.Reports)
        {
            if (!report.SelfAndDescendants().Any(sheetsByNode.ContainsKey)) continue;

            var outcome = new ReportOutcome
            {
                Report = report,
                Title = ImportResult.TitleFor(report)
            };

            ProcessReport(report, sheetsByNode, outcome, root, ns);
            outcomes.Add(outcome);
        }

        foreach (var sheet in workbook.Sheets.Where(s => s.UnknownColumns.Count > 0))
        {
            general.Add(new Issue(IssueSeverity.Warning, sheet.Plan.SheetName, null,
                Lang.T("непознати колони, които се пренебрегват: ", "unknown columns, which are ignored: ") + string.Join(", ", sheet.UnknownColumns))
            { SheetTitle = ReportTitles.Table(sheet.Plan.Node) });
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        var hasContent = root.Elements().Any();
        if (!hasContent)
        {
            general.Add(new Issue(IssueSeverity.Error, string.Empty, null,
                Lang.T("Няма нито един валиден ред — XML файл не беше генериран.", "Not a single valid row — no XML file was produced.")));
        }

        var schemaErrors = hasContent
            ? _validator.Validate(document)
            : (IReadOnlyList<string>)[];

        return new ImportResult
        {
            Schema = schema,
            Reports = outcomes,
            GeneralIssues = general,
            SchemaErrors = schemaErrors,
            Xml = hasContent ? document : null
        };
    }

    // ----------------------------------------------------------- заглавие

    private static void WriteRootAttributes(
        XElement root, SchemaDefinition schema, ParsedWorkbook workbook, List<Issue> general)
    {
        foreach (var attr in schema.RootAttributes)
        {
            var cell = workbook.Config.TryGetValue(attr.Name, out var value) ? value : CellValue.Empty;
            var result = ValueConverter.Convert(cell, attr);

            if (!result.Ok)
            {
                general.Add(new Issue(IssueSeverity.Error, WorkbookLayout.ConfigSheet, null, result.Error!));
                continue;
            }

            if (result.Value is not null)
                root.SetAttributeValue(attr.Name, result.Value);
        }
    }

    // -------------------------------------------------------------- отчети

    private static void ProcessReport(
        ReportNode report,
        IReadOnlyDictionary<ReportNode, SheetData> sheets,
        ReportOutcome outcome,
        XElement root,
        XNamespace ns)
    {
        var instances = new Dictionary<string, XElement>(StringComparer.Ordinal);

        if (!sheets.TryGetValue(report, out var sheet))
        {
            outcome.Issues.Add(new Issue(IssueSeverity.Error, report.ElementName, null,
                Lang.T(
                    $"липсва таблицата с основните данни на „{ReportTitles.Table(report)}“ — " +
                    "подтаблиците не могат да бъдат свързани",
                    $"the table with the main data of “{ReportTitles.Table(report)}” is missing — " +
                    "the sub-tables cannot be linked"))
            { SheetTitle = ReportTitles.Table(report) });
            return;
        }

        foreach (var row in sheet.Rows)
        {
            if (!ConvertRow(row, sheet, outcome, out var values))
            {
                outcome.InvalidRows++;
                continue;
            }

            var key = KeyOf(sheet.Plan.LinkColumns, values, report, out var stagenum);
            key = stagenum;

            if (key is null)
            {
                outcome.Issues.Add(new Issue(IssueSeverity.Error, sheet.Plan.SheetName, row.ExcelRow,
                    Lang.T(
                        $"не е попълнено „{Named(sheet, "stagenum")}“ — " +
                        "редът не може да се свърже с подтаблиците",
                        $"“{Named(sheet, "stagenum")}” is not filled in — " +
                        "the row cannot be linked to the sub-tables"))
                { SheetTitle = ReportTitles.Table(sheet.Plan.Node) });
                outcome.InvalidRows++;
                continue;
            }

            if (instances.ContainsKey(key))
            {
                outcome.Issues.Add(new Issue(IssueSeverity.Error, sheet.Plan.SheetName, row.ExcelRow,
                    Lang.T(
                        $"„{Named(sheet, "stagenum")}“ {key} се повтаря — " +
                        "всеки работен лист трябва да е на един ред",
                        $"“{Named(sheet, "stagenum")}” {key} is repeated — " +
                        "each work sheet must sit on one row"))
                { SheetTitle = ReportTitles.Table(sheet.Plan.Node) });
                outcome.InvalidRows++;
                continue;
            }

            var element = new XElement(ns + report.ElementName);
            WriteAttributes(element, report.Attributes, values);
            root.Add(element);

            instances[key] = element;
            outcome.Instances++;
            outcome.ValidRows++;
        }

        foreach (var child in report.Children)
            ProcessChild(child, sheets, outcome, instances, ns);
    }

    private static void ProcessChild(
        ReportNode node,
        IReadOnlyDictionary<ReportNode, SheetData> sheets,
        ReportOutcome outcome,
        IReadOnlyDictionary<string, XElement> parents,
        XNamespace ns)
    {
        if (!sheets.TryGetValue(node, out var sheet))
        {
            // Таблицата не е част от този файл — тя е незадължителна в схемата.
            return;
        }

        var ownInstances = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var wrappers = new Dictionary<XElement, XElement>();

        foreach (var row in sheet.Rows)
        {
            if (!ConvertRow(row, sheet, outcome, out var values))
            {
                outcome.InvalidRows++;
                continue;
            }

            var key = KeyOf(sheet.Plan.LinkColumns, values, node, out _);
            if (key is null || !parents.TryGetValue(key, out var parent))
            {
                outcome.Issues.Add(new Issue(IssueSeverity.Error, sheet.Plan.SheetName, row.ExcelRow,
                    Lang.T(
                        $"няма ред с {DescribeKey(sheet.Plan.LinkColumns, values)} — " +
                        "проверете дали той съществува в основната таблица на отчета",
                        $"there is no row with {DescribeKey(sheet.Plan.LinkColumns, values)} — " +
                        "check whether it exists in the report's main table"))
                { SheetTitle = ReportTitles.Table(sheet.Plan.Node) });
                outcome.InvalidRows++;
                continue;
            }

            XElement target;
            if (node.ItemWrapper)
            {
                if (!wrappers.TryGetValue(parent, out var wrapper))
                {
                    wrapper = new XElement(ns + node.ElementName);
                    parent.Add(wrapper);
                    wrappers[parent] = wrapper;
                }

                target = new XElement(ns + "item");
                wrapper.Add(target);
            }
            else
            {
                target = new XElement(ns + node.ElementName);
                parent.Add(target);
            }

            WriteAttributes(target, node.Attributes, values);
            outcome.ValidRows++;

            if (sheet.Plan.IdentityColumn is { } identity &&
                values.TryGetValue(identity.Name, out var identityValue) &&
                identityValue is not null)
            {
                ownInstances[key + "|" + identityValue] = target;
            }
        }

        foreach (var child in node.Children)
            ProcessChild(child, sheets, outcome, ownInstances, ns);
    }

    // -------------------------------------------------------------- редове

    private static bool ConvertRow(
        RawRow row, SheetData sheet, ReportOutcome outcome,
        out Dictionary<string, string?> values)
    {
        values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var ok = true;

        foreach (var column in sheet.Plan.Columns)
        {
            var result = ValueConverter.Convert(row.Get(column.Name), column);

            if (!result.Ok)
            {
                outcome.Issues.Add(new Issue(
                    IssueSeverity.Error, sheet.Plan.SheetName, row.ExcelRow, result.Error!)
                { SheetTitle = ReportTitles.Table(sheet.Plan.Node) });
                ok = false;
                continue;
            }

            values[column.Name] = result.Value;
        }

        return ok;
    }

    private static void WriteAttributes(
        XElement element, IReadOnlyList<ColumnDef> attributes, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var attr in attributes)
        {
            if (values.TryGetValue(attr.Name, out var value) && value is not null)
                element.SetAttributeValue(attr.Name, value);
        }
    }

    /// <summary>Съставя ключа, по който редът се свързва с родителския запис.</summary>
    private static string? KeyOf(
        IReadOnlyList<ColumnDef> linkColumns,
        IReadOnlyDictionary<string, string?> values,
        ReportNode node,
        out string? stagenum)
    {
        stagenum = values.GetValueOrDefault(WorkbookLayout.StagenumLink);

        if (linkColumns.Count == 0)
            return stagenum;

        var parts = new List<string>(linkColumns.Count);
        foreach (var column in linkColumns)
        {
            var value = values.GetValueOrDefault(column.Name);
            if (value is null) return null;
            parts.Add(value);
        }

        return string.Join('|', parts);
    }

    private static string DescribeKey(
        IReadOnlyList<ColumnDef> linkColumns, IReadOnlyDictionary<string, string?> values)
    {
        if (linkColumns.Count == 0) return Lang.T("посочения номер на работен лист", "the given work-sheet number");

        return string.Join(", ", linkColumns.Select(c =>
            $"„{ReportTitles.ColumnShort(c)}“ = „{values.GetValueOrDefault(c.Name) ?? string.Empty}“"));
    }

    /// <summary>
    /// Надписът на колона от този лист, както го вижда потребителят. Ако
    /// схемата не я познава, остава името ѝ — по-добре суровото име, отколкото
    /// празно място.
    /// </summary>
    private static string Named(SheetData sheet, string columnName) =>
        sheet.Plan.Columns.FirstOrDefault(c => c.Name == columnName) is { } column
            ? ReportTitles.ColumnShort(column)
            : FieldLabels.Resolve(columnName, null);
}
