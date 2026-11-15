using System.Globalization;
using ClosedXML.Excel;
using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Excel;

/// <summary>
/// Едно заварено количество, както се чете от файла: склад, код и колко.
///
/// Без клиент и документ: завареното е купчина, чиято история не се знае.
/// Измислен произход е по-лош от липсващия — той изглежда като отговор.
/// </summary>
public sealed record StockOpeningRow(int ExcelRow, StoreKind Store, string Ewc, decimal Amount);

/// <summary>Прочетеният файл: какво е разбрано и от какво програмата се е отказала.</summary>
public sealed record StockOpeningFile(IReadOnlyList<StockOpeningRow> Rows, IReadOnlyList<string> Problems);

/// <summary>
/// Шаблонът за заварените количества в склада и четенето му.
///
/// Два листа, защото купчините са две — „Получен“ и „Образуван“ — и в
/// прозореца те стоят в два раздела. Един лист с колона „склад“ би бил още
/// едно поле за сбъркване на всеки ред.
///
/// Устройството е същото като при другите шаблони: ред 1 е надписът на
/// български, ред 2 е техническото име, данните почват от ред 3. Човек, който
/// вече е попълвал шаблон, не учи нищо ново.
/// </summary>
public static class StockOpeningWorkbook
{
    public const string ReceivedSheet = "ПОЛУЧЕН";
    public const string FormedSheet = "ОБРАЗУВАН";

    public const int LabelRow = 1;
    public const int HeaderRow = 2;
    public const int FirstDataRow = 3;

    private const int LastRow = 2000;

    public const string EwcColumn = "ewc";
    public const string AmountColumn = "amount_ton";

    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x4E, 0x79);
    private static readonly XLColor RequiredFill = XLColor.FromArgb(0xFD, 0xE9, 0xE9);

    /// <summary>Листът на склада — по него се пише и се чете.</summary>
    public static string Sheet(StoreKind store) => store == StoreKind.Formed ? FormedSheet : ReceivedSheet;

    // --------------------------------------------------------------- запис

    /// <summary>
    /// Записва шаблона. Вече обявеното влиза в него — файлът е и шаблон, и
    /// извадка: тегли се, поправят се две числа и се връща.
    /// </summary>
    public static void Save(
        string path,
        DateOnly eve,
        IReadOnlyList<string> codes,
        IEnumerable<StockOpeningRow>? existing = null)
    {
        using var wb = new XLWorkbook();
        wb.Properties.Author = "RIOSV Imports";
        wb.Properties.Title = Lang.T("Начални количества в склада", "Opening stock");

        var instructions = wb.Worksheets.Add(WorkbookLayout.InstructionsSheet);
        var received = wb.Worksheets.Add(ReceivedSheet);
        var formed = wb.Worksheets.Add(FormedSheet);
        var lists = wb.Worksheets.Add(WorkbookLayout.ListsSheet);

        BuildInstructions(instructions, eve);
        BuildLists(lists, codes);

        var rows = existing?.ToList() ?? [];

        BuildData(received, lists, codes.Count, rows.Where(r => r.Store == StoreKind.Received));
        BuildData(formed, lists, codes.Count, rows.Where(r => r.Store == StoreKind.Formed));

        lists.Hide();
        instructions.SetTabActive();

        wb.SaveAs(path);
    }

    private static void BuildInstructions(IXLWorksheet ws, DateOnly eve)
    {
        var day = eve.ToString(ValueConverter.BulgarianDateFormat, CultureInfo.InvariantCulture);

        (string Text, bool Bold, int Size)[] lines =
        [
            (Lang.T("Начални количества в склада", "Opening stock"), true, 16),
            (string.Empty, false, 11),
            (Lang.T($"Какво е стояло на площадката към {day} г. — денят преди първия воден месец.",
                    $"What stood on the site as of {day} — the day before the first tracked month."), false, 11),
            (Lang.T("Месецът се избира в прозореца „Начални количества“; внесеното застава в деня преди него.",
                    "The month is chosen in the “Opening quantities” window; the imported goes on the day before it."), false, 11),
            (string.Empty, false, 11),
            (Lang.T("Как се попълва", "How to fill it in"), true, 13),
            (Lang.T($"1. Лист „{ReceivedSheet}“ — суровината, както е дошла.",
                    $"1. Sheet “{ReceivedSheet}” — the raw material as it came in."), false, 11),
            (Lang.T($"2. Лист „{FormedSheet}“ — каквото е излязло от третирането и чака да бъде предадено.",
                    $"2. Sheet “{FormedSheet}” — what came out of treatment and waits to be handed over."), false, 11),
            (Lang.T("3. Ред 1 е надписът, ред 2 е техническото име. Не ги изтривайте и не ги разменяйте.",
                    "3. Row 1 is the label, row 2 the technical name. Do not delete or swap them."), false, 11),
            (Lang.T("4. Данните започват от ред 3. Празните редове се пропускат.",
                    "4. The data starts at row 3. Empty rows are skipped."), false, 11),
            (Lang.T("5. Запазете файла и го внесете от „Внеси от Excel“ в същия прозорец.",
                    "5. Save the file and import it with “Import from Excel” in the same window."), false, 11),
            (string.Empty, false, 11),
            (Lang.T("Правила", "Rules"), true, 13),
            (Lang.T("• По един ред за код: завареното е купчина, без клиент и без история.",
                    "• One row per code: the opening stock is a pile, with no client and no history."), false, 11),
            (Lang.T("• Два реда с един и същ код в един лист се събират.",
                    "• Two rows with the same code on one sheet are added together."), false, 11),
            (Lang.T("• От кода се четат само цифрите: „16 10 04“, „16.10.04“ и „161004“ са едно и също. Трябват точно 6.",
                    "• Only the digits of the code are read: “16 10 04”, “16.10.04” and “161004” are the same. Exactly 6 are needed."), false, 11),
            (Lang.T("• Числата може да са със запетая или с точка.",
                    "• Numbers may use a comma or a full stop."), false, 11),
            (Lang.T("• Внасянето ЗАМЕСТВА обявеното досега. Количество, от което вече е черпено, остава както е.",
                    "• Importing REPLACES what was declared so far. A quantity already drawn from stays as it is."), false, 11)
        ];

        var row = 1;

        foreach (var (text, bold, size) in lines)
        {
            var cell = ws.Cell(row++, 1);
            cell.Value = text;
            cell.Style.Font.Bold = bold;
            cell.Style.Font.FontSize = size;
        }

        ws.Column(1).Width = 110;
    }

    private static void BuildLists(IXLWorksheet ws, IReadOnlyList<string> codes)
    {
        ws.Cell(1, 1).Value = EwcColumn;
        ws.Cell(1, 1).Style.Font.Bold = true;

        for (var i = 0; i < codes.Count; i++)
            ws.Cell(i + 2, 1).SetValue(codes[i]);

        ws.Column(1).Style.NumberFormat.Format = "@";
    }

    private static void BuildData(
        IXLWorksheet ws, IXLWorksheet lists, int codeCount, IEnumerable<StockOpeningRow> existing)
    {
        (string Name, string Label, double Width)[] columns =
        [
            (EwcColumn, Lang.T("Код на отпадъка", "Waste code"), 18),
            (AmountColumn, Lang.T("Количество, тон", "Quantity, tonnes"), 18)
        ];

        for (var i = 0; i < columns.Length; i++)
        {
            var (name, label, width) = columns[i];
            var index = i + 1;

            var labelCell = ws.Cell(LabelRow, index);
            labelCell.Value = label;
            labelCell.Style.Font.Bold = true;
            labelCell.Style.Font.FontColor = XLColor.White;
            labelCell.Style.Fill.BackgroundColor = HeaderFill;
            labelCell.Style.Alignment.WrapText = true;
            labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            var headerCell = ws.Cell(HeaderRow, index);
            headerCell.Value = name;
            headerCell.Style.Font.FontSize = 9;
            headerCell.Style.Font.FontColor = XLColor.FromArgb(0x44, 0x44, 0x44);
            headerCell.Style.Fill.BackgroundColor = RequiredFill;

            // Кодът е текст: иначе Excel изяжда водещата нула на „01 01 01“.
            ws.Range(FirstDataRow, index, LastRow, index).Style.NumberFormat.Format =
                name == AmountColumn ? "0.000" : "@";

            ws.Column(index).Width = width;
        }

        var codes = ws.Range(FirstDataRow, 1, LastRow, 1).CreateDataValidation();
        codes.List(lists.Range(2, 1, Math.Max(2, codeCount + 1), 1), true);
        Explain(codes, Lang.T("Изберете код по ЕКО от списъка.", "Pick an EWC code from the list."));

        var amounts = ws.Range(FirstDataRow, 2, LastRow, 2).CreateDataValidation();
        amounts.Decimal.Between(0, 1_000_000_000);
        Explain(amounts, Lang.T("Количество в тонове, по-голямо от нула.",
                                "A quantity in tonnes, greater than zero."));

        var row = FirstDataRow;

        foreach (var entry in existing)
        {
            ws.Cell(row, 1).SetValue(entry.Ewc);
            ws.Cell(row, 2).SetValue(entry.Amount);
            row++;
        }

        ws.Row(LabelRow).Height = 34;
        ws.SheetView.FreezeRows(HeaderRow);
    }

    private static void Explain(IXLDataValidation validation, string message)
    {
        validation.IgnoreBlanks = true;
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Warning;
        validation.ErrorTitle = Lang.T("Проверете стойността", "Check the value");
        validation.ErrorMessage = message;
    }

    // -------------------------------------------------------------- четене

    /// <summary>
    /// Чете попълнения файл — двата листа.
    ///
    /// Нечитаемият ред не спира останалите: при сто реда един сбъркан не бива
    /// да отмени деветдесет и деветте верни. Липсващ лист е празен склад, не
    /// грешка — но липсват ли и двата, файлът не е този шаблон.
    /// </summary>
    public static StockOpeningFile Read(string path, IReadOnlySet<string>? known = null)
    {
        var rows = new List<StockOpeningRow>();
        var problems = new List<string>();

        using var wb = Open(path);

        var found = 0;

        foreach (var store in new[] { StoreKind.Received, StoreKind.Formed })
        {
            if (!wb.TryGetWorksheet(Sheet(store), out var ws)) continue;

            found++;

            if (!Header(ws, out var header, out var headerRow))
            {
                problems.Add(Lang.T(
                    $"В листа „{ws.Name}“ не се намира редът с имената на колоните ({EwcColumn}, {AmountColumn}).",
                    $"The row with the column names ({EwcColumn}, {AmountColumn}) is not found in sheet “{ws.Name}”."));

                continue;
            }

            var last = ws.LastRowUsed()?.RowNumber() ?? headerRow;

            for (var number = headerRow + 1; number <= last; number++)
            {
                var raw = Text(ws, number, header, EwcColumn);
                var amountText = Text(ws, number, header, AmountColumn);

                if (raw.Length == 0 && amountText.Length == 0) continue;

                var where = Lang.T($"„{ws.Name}“, ред {number}: ", $"“{ws.Name}”, row {number}: ");

                if (raw.Length == 0)
                {
                    problems.Add(where + Lang.T("липсва код на отпадъка.", "the waste code is missing."));
                    continue;
                }

                var ewc = Code(raw, Numeric(ws, number, header, EwcColumn));

                if (ewc.Length != CodeLength)
                {
                    problems.Add(where + Lang.T(
                        $"„{raw}“ не е код по ЕКО — трябват точно {CodeLength} цифри.",
                        $"“{raw}” is not an EWC code — exactly {CodeLength} digits are needed."));
                    continue;
                }

                if (known is not null && !known.Contains(ewc))
                {
                    problems.Add(where + Lang.T(
                        $"код {ewc} го няма в списъка на кодовете по ЕКО.",
                        $"code {ewc} is not in the EWC code list."));
                    continue;
                }

                if (ValueConverter.TryAmount(amountText) is not { } amount)
                {
                    problems.Add(where + (amountText.Length == 0
                        ? Lang.T("липсва количество.", "the quantity is missing.")
                        : Lang.T($"„{amountText}“ не е число.", $"“{amountText}” is not a number.")));
                    continue;
                }

                if (amount <= 0)
                {
                    problems.Add(where + Lang.T("количеството трябва да е по-голямо от нула.",
                                                "the quantity must be greater than zero."));
                    continue;
                }

                rows.Add(new StockOpeningRow(number, store, ewc, SplitMath.Round(amount)));
            }
        }

        if (found == 0)
            problems.Add(Lang.T($"Във файла няма нито лист „{ReceivedSheet}“, нито „{FormedSheet}“.",
                                $"The file has neither a “{ReceivedSheet}” nor a “{FormedSheet}” sheet."));

        return new StockOpeningFile(rows, problems);
    }

    /// <summary>Кодът по ЕКО е шест цифри — нито повече, нито по-малко.</summary>
    public const int CodeLength = 6;

    /// <summary>
    /// Прочита кода само по цифрите му.
    ///
    /// Кодът се пише как ли не: „16 02 16“, „16.02.16“, „160216 “, с неразделящ
    /// интервал от копиране, с тире. Всичко, което не е цифра, се маха — така
    /// внасянето не зависи от това как е бил написан.
    ///
    /// Буквите слагат край: „160216 — Опаковки от 2 вида“ е кодът с името му,
    /// а цифрите в името не са част от кода.
    ///
    /// Число в клетката губи водещите си нули — Excel пази 010101 като 10101.
    /// Затова числото се допълва с нули отпред до шест цифри.
    /// </summary>
    public static string Code(string text, bool numeric = false)
    {
        var digits = new System.Text.StringBuilder(CodeLength);

        foreach (var c in text)
        {
            if (char.IsLetter(c)) break;
            if (c is >= '0' and <= '9') digits.Append(c);
        }

        var code = digits.ToString();

        return numeric && code.Length is > 0 and < CodeLength ? code.PadLeft(CodeLength, '0') : code;
    }

    /// <summary>Дали клетката е число — тогава водещите нули на кода са изгубени.</summary>
    private static bool Numeric(IXLWorksheet ws, int row, IReadOnlyDictionary<string, int> header, string column) =>
        header.TryGetValue(column, out var index) &&
        ws.Cell(row, index) is { DataType: XLDataType.Number } cell && cell.Value.IsNumber;

    /// <summary>Намира реда със заглавията и кой номер колона на какво отговаря.</summary>
    private static bool Header(IXLWorksheet ws, out Dictionary<string, int> header, out int row)
    {
        header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        row = 0;

        for (var candidate = 1; candidate <= 8; candidate++)
        {
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var column = 1; column <= 12; column++)
            {
                var text = ws.Cell(candidate, column).GetString().Trim();
                if (text.Length > 0 && !found.ContainsKey(text)) found[text] = column;
            }

            if (!found.ContainsKey(EwcColumn) || !found.ContainsKey(AmountColumn)) continue;

            header = found;
            row = candidate;

            return true;
        }

        return false;
    }

    private static string Text(IXLWorksheet ws, int row, IReadOnlyDictionary<string, int> header, string column)
    {
        if (!header.TryGetValue(column, out var index)) return string.Empty;

        var cell = ws.Cell(row, index);

        // Excel връща числата с плаващата си точка; текстът на клетката пази
        // каквото се вижда, а водещите нули на кода са част от него.
        if (cell.DataType == XLDataType.Number && cell.Value.IsNumber)
        {
            var number = cell.Value.GetNumber();

            return number == Math.Floor(number) && Math.Abs(number) < 1e15
                ? ((long)number).ToString(CultureInfo.InvariantCulture)
                : number.ToString("0.######", CultureInfo.InvariantCulture);
        }

        return cell.GetString().Trim();
    }

    private static XLWorkbook Open(string path)
    {
        try
        {
            return new XLWorkbook(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                Lang.T($"Файлът „{Path.GetFileName(path)}“ не се отваря: {ex.Message}",
                       $"The file “{Path.GetFileName(path)}” cannot be opened: {ex.Message}"), ex);
        }
    }
}
