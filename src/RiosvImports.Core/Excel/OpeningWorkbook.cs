using System.Globalization;
using ClosedXML.Excel;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Excel;

/// <summary>Начално количество, както се чете от файла — още без фирма от базата.</summary>
public sealed record OpeningRow(int ExcelRow, string Eik, string Ewc, int Year, decimal Amount);

/// <summary>Прочетеният файл: какво е разбрано и от какво програмата се е отказала.</summary>
public sealed record OpeningFile(IReadOnlyList<OpeningRow> Rows, IReadOnlyList<string> Problems);

/// <summary>Фирма, както се предлага в падащото меню на шаблона.</summary>
public sealed record OpeningCompany(string Eik, string Name);

/// <summary>
/// Шаблонът за начални количества и четенето му.
///
/// Балансът тръгва от нула само първата година. После в началото на всяка
/// година на площадката вече има отпадък и без него салдото е вярно число за
/// грешен въпрос. Тези количества не идват от отчет — те са състояние отпреди
/// програмата — затова се внасят.
///
/// Файлът е нарочно на един лист с четири колони. Наредбата не го изисква и
/// НИСО не го иска; той съществува само за да не се въвеждат на ръка сто
/// кода. Затова е скучен колкото може: ЕИК, код, година, количество.
///
/// Устройството на листа е същото като на големите шаблони — ред 1 е надписът
/// на български, ред 2 е техническото име, данните почват от ред 3. Един
/// човек, който вече е попълвал шаблон за отчет, не учи нищо ново.
/// </summary>
public static class OpeningWorkbook
{
    public const string DataSheet = "НАЧАЛНИ";

    public const int LabelRow = 1;
    public const int HeaderRow = 2;
    public const int FirstDataRow = 3;

    /// <summary>Докъде се слага проверка на въведеното и падащо меню.</summary>
    private const int LastRow = 1000;

    /// <summary>Техническите имена на колоните — по тях се чете файлът.</summary>
    public const string EikColumn = "eik";
    public const string EwcColumn = "ewc";
    public const string YearColumn = "year";
    public const string AmountColumn = "amount_ton";

    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x4E, 0x79);
    private static readonly XLColor RequiredFill = XLColor.FromArgb(0xFD, 0xE9, 0xE9);

    // --------------------------------------------------------------- запис

    /// <summary>
    /// Записва шаблона. Вече въведените количества влизат в него — така файлът
    /// е и шаблон, и извадка: човек го тегли, поправя две числа и го връща,
    /// вместо да преписва всичко наново.
    /// </summary>
    public static void Save(
        string path,
        int year,
        IReadOnlyList<OpeningCompany> companies,
        IReadOnlyList<string> codes,
        IEnumerable<OpeningRow>? existing = null)
    {
        using var wb = new XLWorkbook();
        wb.Properties.Author = "RIOSV Imports";
        wb.Properties.Title = Lang.T("Начални количества", "Opening quantities");

        var instructions = wb.Worksheets.Add(WorkbookLayout.InstructionsSheet);
        var data = wb.Worksheets.Add(DataSheet);
        var lists = wb.Worksheets.Add(WorkbookLayout.ListsSheet);

        BuildInstructions(instructions, year);
        BuildLists(lists, companies, codes);
        BuildData(data, lists, year, companies, codes, existing);

        lists.Hide();
        wb.Worksheet(WorkbookLayout.InstructionsSheet).SetTabActive();

        wb.SaveAs(path);
    }

    private static void BuildInstructions(IXLWorksheet ws, int year)
    {
        (string Text, bool Bold, int Size)[] lines =
        [
            (Lang.T("Начални количества за баланса", "Opening quantities for the balance"), true, 16),
            (string.Empty, false, 11),
            (Lang.T($"Колко тона от всеки код има на площадката към 1 януари {year} г.",
                    $"How many tonnes of each code are on site as of 1 January {year}."), false, 11),
            (Lang.T("Програмата ги слага пред януари и оттам нататък смята сама.",
                    "The program places them before January and works the rest out itself."), false, 11),
            (string.Empty, false, 11),
            (Lang.T("Как се попълва", "How to fill it in"), true, 13),
            (Lang.T($"1. Лист „{DataSheet}“ — по един ред за всеки код.",
                    $"1. Sheet “{DataSheet}” — one row per code."), false, 11),
            (Lang.T("2. Ред 1 е надписът, ред 2 е техническото име. Не ги изтривайте и не ги разменяйте.",
                    "2. Row 1 is the label, row 2 the technical name. Do not delete or swap them."), false, 11),
            (Lang.T("3. Данните започват от ред 3. Празните редове се пропускат.",
                    "3. The data starts at row 3. Empty rows are skipped."), false, 11),
            (Lang.T("4. Запазете файла и го внесете обратно от бутона „Начални количества“.",
                    "4. Save the file and import it back from the “Opening quantities” button."), false, 11),
            (string.Empty, false, 11),
            (Lang.T("Правила", "Rules"), true, 13),
            (Lang.T("• ЕИК посочва на коя фирма е количеството. Фирмата трябва да е заведена в програмата.",
                    "• The UIC says which company the quantity belongs to. The company must exist in the program."), false, 11),
            (Lang.T("• Кодовете по ЕКО се пишат с интервали („16 10 04“) или слято („161004“) — все едно.",
                    "• EWC codes may be written with spaces (“16 10 04”) or run together (“161004”) — either works."), false, 11),
            (Lang.T("• Числата може да са със запетая или с точка.",
                    "• Numbers may use a comma or a full stop."), false, 11),
            (Lang.T("• Два реда с една и съща фирма, код и година се събират.",
                    "• Two rows with the same company, code and year are added together."), false, 11),
            (Lang.T("• Внасянето ЗАМЕСТВА въведеното досега за годините във файла.",
                    "• Importing REPLACES what has been entered so far for the years in the file."), false, 11)
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

    private static void BuildLists(
        IXLWorksheet ws, IReadOnlyList<OpeningCompany> companies, IReadOnlyList<string> codes)
    {
        ws.Cell(1, 1).Value = EikColumn;
        ws.Cell(1, 2).Value = EwcColumn;
        ws.Range(1, 1, 1, 2).Style.Font.Bold = true;

        for (var i = 0; i < companies.Count; i++)
            ws.Cell(i + 2, 1).SetValue(companies[i].Eik);

        for (var i = 0; i < codes.Count; i++)
            ws.Cell(i + 2, 2).SetValue(codes[i]);

        ws.Columns(1, 2).Style.NumberFormat.Format = "@";
    }

    private static void BuildData(
        IXLWorksheet ws,
        IXLWorksheet lists,
        int year,
        IReadOnlyList<OpeningCompany> companies,
        IReadOnlyList<string> codes,
        IEnumerable<OpeningRow>? existing)
    {
        (string Name, string Label, double Width)[] columns =
        [
            (EikColumn, Lang.T("ЕИК на фирмата", "Company UIC"), 22),
            (EwcColumn, Lang.T("Код на отпадъка", "Waste code"), 18),
            (YearColumn, Lang.T("Година", "Year"), 12),
            (AmountColumn, Lang.T("Начално количество, тон", "Opening quantity, tonnes"), 24)
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

            var range = ws.Range(FirstDataRow, index, LastRow, index);

            // Кодът и ЕИК са текст: иначе Excel изяжда водещата нула на „01 01 01“.
            range.Style.NumberFormat.Format = name switch
            {
                YearColumn => "0",
                AmountColumn => "0.#####",
                _ => "@"
            };

            ws.Column(index).Width = width;
        }

        Validate(ws.Range(FirstDataRow, 1, LastRow, 1),
            lists.Range(2, 1, Math.Max(2, companies.Count + 1), 1),
            Lang.T("Изберете ЕИК на заведена фирма.", "Pick the UIC of a company entered in the program."));

        Validate(ws.Range(FirstDataRow, 2, LastRow, 2),
            lists.Range(2, 2, Math.Max(2, codes.Count + 1), 2),
            Lang.T("Изберете код по ЕКО от списъка.", "Pick an EWC code from the list."));

        var yearCheck = ws.Range(FirstDataRow, 3, LastRow, 3).CreateDataValidation();
        yearCheck.IgnoreBlanks = true;
        yearCheck.WholeNumber.Between(1990, 2100);
        Explain(yearCheck, Lang.T("Година между 1990 и 2100.", "A year between 1990 and 2100."));

        var amountCheck = ws.Range(FirstDataRow, 4, LastRow, 4).CreateDataValidation();
        amountCheck.IgnoreBlanks = true;
        amountCheck.Decimal.Between(0, 1_000_000_000);
        Explain(amountCheck, Lang.T("Количество в тонове, не по-малко от нула.",
                                    "A quantity in tonnes, not less than zero."));

        var row = FirstDataRow;

        foreach (var entry in existing ?? [])
        {
            ws.Cell(row, 1).SetValue(entry.Eik);
            ws.Cell(row, 2).SetValue(entry.Ewc);
            ws.Cell(row, 3).SetValue(entry.Year);
            ws.Cell(row, 4).SetValue(entry.Amount);
            row++;
        }

        // Празният файл не е празен съвсем: годината и единствената фирма се
        // подразбират, а човек, който вижда попълнен ред, разбира устройството,
        // без да чете указанията.
        if (row == FirstDataRow)
        {
            ws.Cell(row, 1).SetValue(companies.Count == 1 ? companies[0].Eik : string.Empty);
            ws.Cell(row, 3).SetValue(year);
        }

        ws.Row(LabelRow).Height = 34;
        ws.SheetView.FreezeRows(HeaderRow);
    }

    private static void Validate(IXLRange range, IXLRange source, string message)
    {
        var validation = range.CreateDataValidation();
        validation.List(source, true);
        Explain(validation, message);
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
    /// Чете попълнения файл.
    ///
    /// Нечитаемият ред не спира останалите: при сто реда един сбъркан не бива
    /// да отмени деветдесет и деветте верни. Затова се връщат и приетите, и
    /// причините за отказаните, а какво да прави с тях решава повикващият.
    /// </summary>
    public static OpeningFile Read(string path)
    {
        var rows = new List<OpeningRow>();
        var problems = new List<string>();

        using var wb = Open(path);

        if (Sheet(wb) is not { } ws)
        {
            problems.Add(Lang.T($"Във файла няма лист „{DataSheet}“.",
                                $"The file has no “{DataSheet}” sheet."));

            return new OpeningFile(rows, problems);
        }

        if (!Header(ws, out var header, out var headerRow))
        {
            problems.Add(Lang.T(
                $"В листа „{ws.Name}“ не се намира редът с имената на колоните ({EikColumn}, {EwcColumn}, {YearColumn}, {AmountColumn}).",
                $"The row with the column names ({EikColumn}, {EwcColumn}, {YearColumn}, {AmountColumn}) is not found in sheet “{ws.Name}”."));

            return new OpeningFile(rows, problems);
        }

        var last = ws.LastRowUsed()?.RowNumber() ?? headerRow;

        for (var number = headerRow + 1; number <= last; number++)
        {
            var eik = Text(ws, number, header, EikColumn);
            var ewc = ValueConverter.NormalizeCode(Text(ws, number, header, EwcColumn));
            var yearText = Text(ws, number, header, YearColumn);
            var amountText = Text(ws, number, header, AmountColumn);

            if (eik.Length == 0 && ewc.Length == 0 && yearText.Length == 0 && amountText.Length == 0) continue;

            // Празният шаблон идва с подсказан ред: фирмата и годината са
            // попълнени, останалото — не. Той не е сгрешен ред, а недокоснат,
            // и няма за какво да се оплаква.
            if (ewc.Length == 0 && amountText.Length == 0) continue;

            if (eik.Length == 0)
            {
                problems.Add(Where(number) + Lang.T("липсва ЕИК.", "the UIC is missing."));
                continue;
            }

            if (ewc.Length == 0)
            {
                problems.Add(Where(number) + Lang.T("липсва код на отпадъка.", "the waste code is missing."));
                continue;
            }

            if (ValueConverter.TryAmount(yearText) is not { } yearValue ||
                yearValue != Math.Truncate(yearValue) || yearValue is < 1990 or > 2100)
            {
                problems.Add(Where(number) + Lang.T($"„{yearText}“ не е година.", $"“{yearText}” is not a year."));
                continue;
            }

            if (ValueConverter.TryAmount(amountText) is not { } amount)
            {
                problems.Add(Where(number) + Lang.T($"„{amountText}“ не е число.", $"“{amountText}” is not a number."));
                continue;
            }

            if (amount < 0)
            {
                problems.Add(Where(number) + Lang.T("количеството е отрицателно.", "the quantity is negative."));
                continue;
            }

            rows.Add(new OpeningRow(number, eik, ewc, (int)yearValue, amount));
        }

        return new OpeningFile(rows, problems);
    }

    private static string Where(int row) => Lang.T($"ред {row}: ", $"row {row}: ");

    private static IXLWorksheet? Sheet(XLWorkbook wb)
    {
        if (wb.TryGetWorksheet(DataSheet, out var named)) return named;

        // Файл, правен на ръка, рядко има точно това име на листа. Щом
        // заглавията са на място, името няма значение.
        return wb.Worksheets.FirstOrDefault(w => Header(w, out _, out _));
    }

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

            if (!found.ContainsKey(EikColumn) || !found.ContainsKey(EwcColumn) ||
                !found.ContainsKey(YearColumn) || !found.ContainsKey(AmountColumn)) continue;

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
                : number.ToString("0.#####", CultureInfo.InvariantCulture);
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
