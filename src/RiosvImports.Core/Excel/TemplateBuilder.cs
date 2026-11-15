using System.Globalization;
using ClosedXML.Excel;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Excel;

/// <summary>
/// Създава работната книга-шаблон: по един лист за всяка таблица от избраните
/// отчети, с падащи менюта, формати и указания на български.
/// </summary>
public sealed class TemplateBuilder
{
    /// <summary>Ред 1 — български надпис, ред 2 — техническо име, данните започват от ред 3.</summary>
    public const int LabelRow = 1;
    public const int HeaderRow = 2;
    public const int FirstDataRow = 3;

    /// <summary>До кой ред слагаме проверка на въведеното.</summary>
    private const int ValidationLastRow = 1000;

    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x4E, 0x79);
    private static readonly XLColor RequiredFill = XLColor.FromArgb(0xFD, 0xE9, 0xE9);
    private static readonly XLColor OptionalFill = XLColor.FromArgb(0xF2, 0xF6, 0xFA);
    private static readonly XLColor LinkFill = XLColor.FromArgb(0xFF, 0xF4, 0xD6);

    private readonly WorkbookLayout _layout;
    private readonly IReadOnlyDictionary<string, string> _defaults;
    private readonly Dictionary<string, IXLRange> _listRanges = new(StringComparer.Ordinal);
    private int _listColumn = 1;

    public TemplateBuilder(WorkbookLayout layout, IReadOnlyDictionary<string, string>? defaults = null)
    {
        _layout = layout;
        _defaults = defaults ?? new Dictionary<string, string>();
    }

    public void Save(string path)
    {
        using var wb = new XLWorkbook();
        wb.Properties.Author = "RIOSV Imports";
        wb.Properties.Title = Lang.T($"Шаблон — {_layout.Schema.Title}", $"Template — {_layout.Schema.Title}");

        var lists = wb.Worksheets.Add(WorkbookLayout.ListsSheet);
        var meta = wb.Worksheets.Add(WorkbookLayout.MetaSheet);

        BuildInstructions(wb.Worksheets.Add(WorkbookLayout.InstructionsSheet));
        BuildConfig(wb.Worksheets.Add(WorkbookLayout.ConfigSheet), lists);

        foreach (var sheet in _layout.Sheets)
            BuildDataSheet(wb.Worksheets.Add(sheet.SheetName), sheet, lists);

        BuildMeta(meta);

        lists.Hide();
        meta.Hide();

        // Отваряме файла на указанията.
        wb.Worksheet(WorkbookLayout.InstructionsSheet).SetTabActive();

        wb.SaveAs(path);
    }

    // ------------------------------------------------------------ указания

    private void BuildInstructions(IXLWorksheet ws)
    {
        var lines = new List<(string Text, bool Bold, int Size)>
        {
            (Lang.T($"Шаблон за импорт в НИСО — {_layout.Schema.Title}", $"Template for import into NWIS — {_layout.Schema.Title}"), true, 16),
            (string.Empty, false, 11),
            (Lang.T("Как се работи с файла", "How to work with this file"), true, 13),
            (Lang.T("1. Попълнете лист „НАСТРОЙКИ“ — година, ЕИК и останалите общи полета.", "1. Fill in the “НАСТРОЙКИ” sheet — year, UIC and the other shared fields."), false, 11),
            (Lang.T("2. Попълнете листовете с данни. Всеки лист отговаря на една таблица от отчета.", "2. Fill in the data sheets. Each sheet matches one table of the report."), false, 11),
            (Lang.T("3. Запазете файла и го пуснете обратно в програмата, за да се генерира XML.", "3. Save the file and drop it back into the program to produce the XML."), false, 11),
            (string.Empty, false, 11),
            (Lang.T("Правила при попълване", "Rules for filling in"), true, 13),
            (Lang.T("• Ред 1 е описание на български, ред 2 е техническото име. Не ги изтривайте и не ги разменяйте.", "• Row 1 is the Bulgarian description, row 2 the technical name. Do not delete or swap them."), false, 11),
            (Lang.T("• Данните започват от ред 3. Празните редове се пропускат.", "• The data starts at row 3. Empty rows are skipped."), false, 11),
            (Lang.T("• Колоните с розов фон са задължителни, а тези с жълт фон свързват реда с отчета.", "• Pink columns are required; yellow ones link the row to the report."), false, 11),
            (Lang.T("• Числата може да се пишат със запетая или с точка — програмата приема и двете.", "• Numbers may use a comma or a full stop — the program accepts both."), false, 11),
            (Lang.T("• Датите се пишат във формат дд.мм.гггг (например 31.12.2024).", "• Dates are written as dd.mm.yyyy (for example 31.12.2024)."), false, 11),
            (Lang.T("• Кодовете по ЕКО може да се пишат с интервали („16 10 04“) или слято („161004“).", "• EWC codes may be written with spaces (“16 10 04”) or run together (“161004”)."), false, 11),
            (Lang.T("• Където има падащо меню, изберете стойност от него, вместо да пишете свободен текст.", "• Where there is a drop-down, pick a value from it instead of typing free text."), false, 11),
            (string.Empty, false, 11),
            (Lang.T("Важно", "Important"), true, 13),
            (Lang.T("• Колоната „stagenum“ е номерът на работния лист. Тя трябва да съвпада между листа на", "• The “stagenum” column is the work-sheet number. It must match between the report"), false, 11),
            (Lang.T("  отчета и листовете с подтаблици, за да се свържат редовете.", "  sheet and the sub-table sheets so the rows link up."), false, 11),
            (Lang.T("• Генерираният XML не се подписва от програмата. Електронният подпис се полага", "• The generated XML is not signed by the program. The electronic signature is applied"), false, 11),
            (Lang.T("  при качването на файла в НИСО.", "  when the file is uploaded to NWIS."), false, 11),
            (string.Empty, false, 11),
            (Lang.T("Листове в този файл", "Sheets in this file"), true, 13)
        };

        var row = 1;
        foreach (var (text, bold, size) in lines)
        {
            var cell = ws.Cell(row++, 1);
            cell.Value = text;
            cell.Style.Font.Bold = bold;
            cell.Style.Font.FontSize = size;
        }

        row++;
        ws.Cell(row, 1).Value = Lang.T("Лист", "Sheet");
        ws.Cell(row, 2).Value = Lang.T("Отчет", "Report");
        ws.Cell(row, 3).Value = Lang.T("Таблица", "Table");
        ws.Range(row, 1, row, 3).Style.Font.Bold = true;
        ws.Range(row, 1, row, 3).Style.Fill.BackgroundColor = XLColor.FromArgb(0xE7, 0xEE, 0xF6);
        row++;

        foreach (var sheet in _layout.Sheets)
        {
            ws.Cell(row, 1).Value = sheet.SheetName;
            ws.Cell(row, 2).Value = FieldLabels.Resolve(sheet.Report.ElementName, sheet.Report.Label);
            ws.Cell(row, 3).Value = ReferenceEquals(sheet.Node, sheet.Report)
                ? Lang.T("Основни данни на отчета", "The report's main data")
                : FieldLabels.Resolve(sheet.Node.ElementName, sheet.Node.Label);
            row++;
        }

        ws.Column(1).Width = 46;
        ws.Column(2).Width = 70;
        ws.Column(3).Width = 70;
        ws.Columns(2, 3).Style.Alignment.WrapText = true;
    }

    // ----------------------------------------------------------- настройки

    private void BuildConfig(IXLWorksheet ws, IXLWorksheet lists)
    {
        ws.Cell(1, 1).Value = Lang.T("Поле", "Field");
        ws.Cell(1, 2).Value = Lang.T("Стойност", "Value");
        ws.Cell(1, 3).Value = Lang.T("Описание", "Description");

        var header = ws.Range(1, 1, 1, 3);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = HeaderFill;

        var row = 2;
        foreach (var attr in _layout.Schema.RootAttributes)
        {
            ws.Cell(row, 1).Value = attr.Name;
            ws.Cell(row, 1).Style.Font.Bold = true;

            var valueCell = ws.Cell(row, 2);
            valueCell.Style.Fill.BackgroundColor = attr.Required ? RequiredFill : OptionalFill;
            ApplyFormat(valueCell.Style, attr);
            ApplyValidation(ws.Range(row, 2, row, 2), attr, lists);

            SetDefault(valueCell, attr);

            ws.Cell(row, 3).Value = Describe(attr);
            row++;
        }

        ws.Column(1).Width = 18;
        ws.Column(2).Width = 26;
        ws.Column(3).Width = 80;
        ws.Column(3).Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(1);
    }

    private void SetDefault(IXLCell cell, ColumnDef attr)
    {
        // Стойностите, въведени в програмата, имат предимство.
        if (_defaults.TryGetValue(attr.Name, out var supplied) && supplied.Length > 0)
        {
            cell.Value = supplied;
            return;
        }

        switch (attr.Name)
        {
            case "year":
                cell.Value = DateTime.Now.Year - 1;
                break;
            case "type":
                cell.Value = "REPLACE";
                break;
        }
    }

    // -------------------------------------------------------- листове данни

    private void BuildDataSheet(IXLWorksheet ws, SheetPlan sheet, IXLWorksheet lists)
    {
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            var col = sheet.Columns[i];
            var index = i + 1;

            var labelCell = ws.Cell(LabelRow, index);
            labelCell.Value = FieldLabels.Resolve(col.Name, col.Label) + (col.Required ? " *" : string.Empty);
            labelCell.Style.Font.Bold = true;
            labelCell.Style.Font.FontColor = XLColor.White;
            labelCell.Style.Fill.BackgroundColor = HeaderFill;
            labelCell.Style.Alignment.WrapText = true;
            labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var headerCell = ws.Cell(HeaderRow, index);
            headerCell.Value = col.Name;
            headerCell.Style.Font.FontSize = 9;
            headerCell.Style.Font.FontColor = XLColor.FromArgb(0x44, 0x44, 0x44);
            headerCell.Style.Fill.BackgroundColor = col.IsLink
                ? LinkFill
                : col.Required ? RequiredFill : OptionalFill;

            var dataRange = ws.Range(FirstDataRow, index, ValidationLastRow, index);
            ApplyFormat(dataRange.Style, col);
            ApplyValidation(dataRange, col, lists);

            ws.Column(index).Width = WidthFor(col);
        }

        ws.Row(LabelRow).Height = 42;
        ws.SheetView.FreezeRows(HeaderRow);
        ws.Range(HeaderRow, 1, HeaderRow, sheet.Columns.Count).SetAutoFilter();
    }

    private static double WidthFor(ColumnDef col) => col.Domain.Kind switch
    {
        ValueKind.Date => 14,
        ValueKind.Integer => 14,
        ValueKind.Decimal => 16,
        ValueKind.Enum => col.Domain.Options.Count > WorkbookLayout.CodeOnlyDropdownThreshold ? 14 : 34,
        _ => col.Domain.MaxLength is > 200 ? 46 : 26
    };

    // ------------------------------------------------------- формат/проверка

    private static void ApplyFormat(IXLStyle style, ColumnDef col)
    {
        switch (col.Domain.Kind)
        {
            case ValueKind.Date:
                style.DateFormat.Format = "dd.mm.yyyy";
                break;

            case ValueKind.Decimal:
                var digits = Math.Min(col.Domain.FractionDigits ?? 3, 5);
                style.NumberFormat.Format = "0." + new string('#', Math.Max(digits, 1));
                break;

            case ValueKind.Integer:
                style.NumberFormat.Format = "0";
                break;

            default:
                // Текст и кодове — иначе Excel изяжда водещите нули на "01".
                style.NumberFormat.Format = "@";
                break;
        }
    }

    private void ApplyValidation(IXLRange range, ColumnDef col, IXLWorksheet lists)
    {
        var domain = col.Domain;

        if (domain.IsEnum)
        {
            var listRange = EnsureList(lists, domain);
            var validation = range.CreateDataValidation();
            validation.List(listRange, true);
            Describe(validation, col, Lang.T("Изберете стойност от списъка.", "Pick a value from the list."));
            return;
        }

        switch (domain.Kind)
        {
            case ValueKind.Decimal or ValueKind.Integer:
            {
                var min = (double)(domain.MinInclusive ?? -1_000_000_000_000m);
                var max = (double)(domain.MaxInclusive ?? 1_000_000_000_000m);
                var validation = range.CreateDataValidation();
                validation.Decimal.Between(min, max);
                Describe(validation, col, DescribeNumber(domain));
                break;
            }

            case ValueKind.Date:
            {
                var validation = range.CreateDataValidation();
                validation.Date.Between(new DateTime(1990, 1, 1), new DateTime(2100, 12, 31));
                Describe(validation, col, Lang.T("Дата във формат дд.мм.гггг.", "A date in dd.mm.yyyy format."));
                break;
            }

            default:
            {
                if (domain.MaxLength is { } max)
                {
                    var validation = range.CreateDataValidation();
                    validation.TextLength.EqualOrLessThan(max);
                    Describe(validation, col, Lang.T($"Текст, най-много {max} знака.", $"Text, at most {max} characters."));
                }
                break;
            }
        }
    }

    private static void Describe(IXLDataValidation validation, ColumnDef col, string message)
    {
        validation.IgnoreBlanks = !col.Required;
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Warning;
        validation.ErrorTitle = Lang.T("Проверете стойността", "Check the value");
        validation.ErrorMessage = message;
    }

    private static string DescribeNumber(ValueDomain domain)
    {
        var parts = new List<string>();
        if (domain.MinInclusive is { } min)
            parts.Add(Lang.T($"не по-малко от {min.ToString("0.#####", CultureInfo.InvariantCulture)}",
                             $"not less than {min.ToString("0.#####", CultureInfo.InvariantCulture)}"));
        if (domain.MaxInclusive is { } max)
            parts.Add(Lang.T($"не повече от {max.ToString("0.#####", CultureInfo.InvariantCulture)}",
                             $"not more than {max.ToString("0.#####", CultureInfo.InvariantCulture)}"));
        if (domain.FractionDigits is { } fd)
            parts.Add(fd == 0 ? Lang.T("цяло число", "whole number") : Lang.T($"най-много {fd} знака след десетичната запетая", $"at most {fd} digits after the decimal separator"));

        return parts.Count == 0 ? Lang.T("Числова стойност.", "A numeric value.") : Lang.T("Числова стойност: ", "Numeric value: ") + string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// Всеки изборен тип се записва веднъж в скрития лист „_СПИСЪЦИ“ и всички
    /// колони от този тип сочат към същия диапазон.
    /// </summary>
    private IXLRange EnsureList(IXLWorksheet lists, ValueDomain domain)
    {
        if (_listRanges.TryGetValue(domain.TypeName, out var existing)) return existing;

        var column = _listColumn++;
        lists.Cell(1, column).Value = domain.TypeName;
        lists.Cell(1, column).Style.Font.Bold = true;

        var codeOnly = domain.Options.Count > WorkbookLayout.CodeOnlyDropdownThreshold;

        var row = 2;
        foreach (var option in domain.Options)
            lists.Cell(row++, column).Value = codeOnly ? option.Value : option.Display;

        var range = lists.Range(2, column, row - 1, column);
        _listRanges[domain.TypeName] = range;
        return range;
    }

    private static string Describe(ColumnDef col)
    {
        var label = FieldLabels.Resolve(col.Name, col.Label);
        var extra = col.Domain.IsEnum
            ? Lang.T(" Допустими стойности: ", " Allowed values: ") + string.Join(", ", col.Domain.Options.Take(10).Select(o => o.Value)) +
              (col.Domain.Options.Count > 10 ? ", …" : string.Empty)
            : string.Empty;

        return label + (col.Required ? Lang.T(" (задължително).", " (required).") : ".") + extra;
    }

    // ----------------------------------------------------------------- мета

    /// <summary>
    /// Скрит лист, който казва на програмата кой лист на коя таблица отговаря.
    /// Така четенето не зависи от това дали потребителят е преименувал нещо.
    /// </summary>
    private void BuildMeta(IXLWorksheet ws)
    {
        ws.Cell(1, 1).Value = "schema";
        ws.Cell(1, 2).Value = "sheet";
        ws.Cell(1, 3).Value = "report";
        ws.Cell(1, 4).Value = "node";
        ws.Cell(1, 5).Value = "header_row";

        var row = 2;
        foreach (var sheet in _layout.Sheets)
        {
            ws.Cell(row, 1).Value = _layout.Schema.Key;
            ws.Cell(row, 2).Value = sheet.SheetName;
            ws.Cell(row, 3).Value = sheet.Report.ElementName;
            ws.Cell(row, 4).Value = sheet.Node.ElementName;
            ws.Cell(row, 5).Value = HeaderRow;
            row++;
        }
    }
}
