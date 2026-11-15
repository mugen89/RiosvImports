using ClosedXML.Excel;
using RiosvImports.Core.Balance;
using RiosvImports.Core.Localization;

namespace RiosvImports.Core.Excel;

/// <summary>
/// Един изглед, готов за изнасяне: заглавие, пресметната таблица и — ако
/// оцветяването се пренася — с какво е оцветена.
/// </summary>
public sealed record BalanceExportView(
    string Title, BalanceTable Table, bool Opening, BalanceHighlight? Highlight = null);

/// <summary>
/// Какво се изнася.
///
/// Освен числата носи и обхвата: коя година, кои фирми, кои отчети са влезли
/// и има ли между тях незавършен. Изнесеният файл живее по-дълго от екрана,
/// на който е направен, и след месец никой не помни кои отметки са били
/// вдигнати. Затова обхватът пътува с числата.
/// </summary>
public sealed record BalanceExport
{
    public required int Year { get; init; }

    /// <summary>Фирмите, по които се гледа — както се изписват на екрана.</summary>
    public required IReadOnlyList<string> Companies { get; init; }

    /// <summary>Отчетите, чиито редове са влезли.</summary>
    public required IReadOnlyList<string> Reports { get; init; }

    /// <summary>Номенклатурата по код — за втората колона.</summary>
    public required IReadOnlyDictionary<string, string> Names { get; init; }

    public required IReadOnlyList<BalanceExportView> Views { get; init; }

    /// <summary>Заглавието на включената незавършена книга, ако има такава.</summary>
    public string? Draft { get; init; }

    /// <summary>Първият и последният месец от гледания период.</summary>
    public int From { get; init; } = 1;
    public int To { get; init; } = BalanceMath.Months;

    /// <summary>Кодът, до който е сведено — празно значи всички.</summary>
    public string Code { get; init; } = string.Empty;

    public bool WholeYear => From == 1 && To == BalanceMath.Months;
}

/// <summary>
/// Изнася баланса в работна книга на Excel.
///
/// По един лист за всеки изглед, а във всеки лист — и двете таблици:
/// движението по месеци и салдото в края на всеки месец. Превключвателят на
/// екрана е удобство, докато се гледа; изнесеният файл се отваря след месец
/// от друг човек и не бива да зависи от това кое копче е било натиснато.
///
/// Кодът и наименованието са в отделни колони, за да се пресява и обобщава в
/// Excel. На екрана стоят слепени, защото там колоната е една.
/// </summary>
public static class BalanceWorkbook
{
    /// <summary>Първият лист: какво точно е изнесено.</summary>
    public const string ScopeSheet = "ОБХВАТ";

    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x4E, 0x79);
    private static readonly XLColor BlockFill = XLColor.FromArgb(0xE7, 0xEE, 0xF6);
    private static readonly XLColor TotalFill = XLColor.FromArgb(0xF2, 0xF6, 0xFA);

    // Оцветяването в изнесения файл не следва темата на програмата: файлът се
    // отваря в Excel, върху бяло, и тъмните цветове от нощния набор биха били
    // нечитаеми там. Смисълът обаче е същият и е описан в листа „ОБХВАТ“.
    private static readonly XLColor DraftFill = XLColor.FromArgb(0xFF, 0xF1, 0xCC);
    private static readonly XLColor NegativeFill = XLColor.FromArgb(0xFA, 0xD4, 0xD4);
    private static readonly XLColor PeakFill = XLColor.FromArgb(0xDC, 0xE9, 0xF7);
    private static readonly XLColor IdleFill = XLColor.FromArgb(0xEE, 0xEE, 0xEE);

    /// <summary>Форматът на количествата — до грам, както ги приема схемата.</summary>
    private const string AmountFormat = "0.###";

    /// <summary>Форматът на целите количества — виж бележката при записа им.</summary>
    private const string WholeFormat = "0";

    public static void Save(string path, BalanceExport export)
    {
        using var wb = new XLWorkbook();
        wb.Properties.Author = "RIOSV Imports";
        wb.Properties.Title = Lang.T($"Баланс {export.Year}", $"Balance {export.Year}");

        BuildScope(wb.Worksheets.Add(ScopeSheet), export);

        var used = new List<string> { ScopeSheet };

        foreach (var view in export.Views)
            BuildView(wb.Worksheets.Add(SheetName(view.Title, used)), view, export);

        wb.Worksheet(ScopeSheet).SetTabActive();
        wb.SaveAs(path);
    }

    /// <summary>
    /// Име на лист, което Excel приема: до 31 знака, без забранените знаци и
    /// без повторение. Имената на разделите минават цели, но правилото стои,
    /// защото те идват отвън.
    /// </summary>
    private static string SheetName(string title, List<string> used)
    {
        var clean = new string([.. title.Where(c => !"[]:*?/\\".Contains(c))]).Trim();
        if (clean.Length == 0) clean = "Лист";
        if (clean.Length > 31) clean = clean[..31];

        var name = clean;
        var next = 2;

        while (used.Contains(name, StringComparer.OrdinalIgnoreCase))
            name = clean[..Math.Min(clean.Length, 28)] + " " + next++;

        used.Add(name);

        return name;
    }

    // --------------------------------------------------------------- обхват

    private static void BuildScope(IXLWorksheet ws, BalanceExport export)
    {
        var row = 1;

        void Line(string text, bool bold = false, int size = 11)
        {
            var cell = ws.Cell(row++, 1);
            cell.Value = text;
            cell.Style.Font.Bold = bold;
            cell.Style.Font.FontSize = size;
        }

        Line(Lang.T($"Баланс за {export.Year} г.", $"Balance for {export.Year}"), true, 16);
        Line(string.Empty);

        Line(Lang.T("Как се чете", "How to read it"), true, 13);
        Line(Lang.T("• Всеки лист е един раздел от отчетната книга; последният е балансът.",
                    "• Each sheet is one section of the book; the last one is the balance."));
        Line(Lang.T("• В листа стоят две таблици: движение по месеци и салдо в края на всеки месец.",
                    "• Each sheet holds two tables: movement per month and the running balance at each month end."));
        Line(Lang.T("• В отделните раздели количествата са такива, каквито са записани.",
                    "• In the individual sections the quantities are exactly as recorded."));
        Line(Lang.T("• В „Баланс“ образуваният отпадък се събира, а предаденият се вади.",
                    "• In “Balance” the generated waste is added and the submitted waste is subtracted."));
        Line(string.Empty);

        Line(Lang.T("Обхват", "Scope"), true, 13);
        Line(Lang.T($"Година: {export.Year}", $"Year: {export.Year}"));

        if (!export.WholeYear)
        {
            Line(Lang.T(
                $"Период: {BalanceSheets.Month(export.From)} – {BalanceSheets.Month(export.To)}. " +
                "Колоната „Начално“ е салдото ПРЕДИ първия месец от периода.",
                $"Period: {BalanceSheets.Month(export.From)} – {BalanceSheets.Month(export.To)}. " +
                "The “Opening” column is the balance BEFORE the first month of the period."));
        }

        if (export.Code.Length > 0)
            Line(Lang.T($"Само код: {export.Code}", $"Only code: {export.Code}"));

        Line(export.Companies.Count == 0
            ? Lang.T("Фирми: всички", "Companies: all")
            : Lang.T($"Фирми ({export.Companies.Count}): ", $"Companies ({export.Companies.Count}): ") +
              string.Join("; ", export.Companies));

        Line(Lang.T($"Отчети: {export.Reports.Count}", $"Reports: {export.Reports.Count}"));

        foreach (var report in export.Reports) Line("    " + report);

        if (export.Draft is not null)
        {
            Line(string.Empty);
            Line(Lang.T("Внимание", "Careful"), true, 13);
            Line(Lang.T($"Включена е незавършена книга: {export.Draft}",
                        $"An unfinished book is included: {export.Draft}"));
            Line(Lang.T("Числата ще се променят, докато тя не бъде проверена.",
                        "These numbers will change until it passes the check."));
        }

        if (export.Views.Any(v => v.Highlight is { } h && !h.IsEmpty))
        {
            Line(string.Empty);
            Line(Lang.T("Оцветяване", "Colouring"), true, 13);

            var marks = export.Views.Select(v => v.Highlight!.Marks).Aggregate((a, b) => a | b);

            void Legend(BalanceMark mark, XLColor fill, string bg, string en)
            {
                if ((marks & mark) == 0) return;

                ws.Cell(row, 1).Style.Fill.BackgroundColor = fill;
                Line("        " + Lang.T(bg, en));
            }

            Legend(BalanceMark.Negative, NegativeFill,
                "отрицателна стойност — предадено е повече от наличното",
                "a negative value — more submitted than is on hand");

            Legend(BalanceMark.Draft, DraftFill,
                "клетка от незавършената книга — числото още ще се мени",
                "a cell from the unfinished book — the number will still change");

            Legend(BalanceMark.Peak, PeakFill,
                "най-голямото движение по този код",
                "the largest movement for that code");

            Legend(BalanceMark.Idle, IdleFill,
                "ред без нито едно движение през годината",
                "a row with no movement at all during the year");

            if ((marks & BalanceMark.Heat) != 0)
                Line("        " + Lang.T("по-плътният фон значи по-голямо количество",
                                         "a denser background means a larger quantity"));
        }

        Line(string.Empty);
        Line(Lang.T($"Изнесено на {DateTime.Now:dd.MM.yyyy} в {DateTime.Now:HH:mm} ч.",
                    $"Exported on {DateTime.Now:dd.MM.yyyy} at {DateTime.Now:HH:mm}"));

        ws.Column(1).Width = 110;
    }

    // --------------------------------------------------------------- изглед

    private static void BuildView(IXLWorksheet ws, BalanceExportView view, BalanceExport export)
    {
        var title = ws.Cell(1, 1);
        title.Value = view.Title;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = Lang.T($"{export.Year} г.", $"{export.Year}");

        var row = Block(ws, 4, view, export,
            Lang.T("Движение по месеци", "Movement per month"),
            Lang.T("Общо", "Total"),
            running: false);

        Block(ws, row + 2, view, export,
            Lang.T("Салдо в края на месеца", "Balance at month end"),
            Lang.T("Край", "Closing"),
            running: true);

        var columns = Width(view, export);

        ws.Column(1).Width = 14;
        ws.Column(2).Width = 46;

        for (var i = 3; i <= columns; i++) ws.Column(i).Width = 11;

        // Кодът и името остават видими, докато се превърта надясно: без тях
        // дванайсетте месеца са просто числа.
        ws.SheetView.FreezeColumns(2);
    }

    /// <summary>Колко колони има таблицата: код, име, [начално], месеците от периода, сбор.</summary>
    private static int Width(BalanceExportView view, BalanceExport export) =>
        3 + (view.Opening ? 1 : 0) + (export.To - export.From);

    /// <summary>
    /// Една таблица. Връща номера на последния ѝ ред, за да може следващата да
    /// започне под нея.
    /// </summary>
    private static int Block(
        IXLWorksheet ws,
        int row,
        BalanceExportView view,
        BalanceExport export,
        string heading,
        string last,
        bool running)
    {
        var columns = Width(view, export);

        var caption = ws.Cell(row, 1);
        caption.Value = heading;
        caption.Style.Font.Bold = true;
        ws.Range(row, 1, row, columns).Style.Fill.BackgroundColor = BlockFill;

        row++;

        var header = row;
        var column = 1;

        void Head(string text)
        {
            var cell = ws.Cell(header, column++);
            cell.Value = text;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Alignment.Horizontal = column > 3
                ? XLAlignmentHorizontalValues.Right
                : XLAlignmentHorizontalValues.Left;
        }

        Head(Lang.T("Код", "Code"));
        Head(Lang.T("Наименование", "Description"));

        if (view.Opening) Head(Lang.T("Начално", "Opening"));

        for (var month = export.From; month <= export.To; month++) Head(BalanceSheets.Month(month));

        Head(last);

        row++;

        var marking = view.Highlight is null
            ? null
            : new BalanceMarking(view.Table, view.Highlight, running);

        foreach (var line in view.Table.Lines)
        {
            ws.Cell(row, 1).SetValue(line.Ewc);
            ws.Cell(row, 1).Style.NumberFormat.Format = "@";
            ws.Cell(row, 2).SetValue(export.Names.GetValueOrDefault(line.Ewc, string.Empty));

            // Залежалият отпадък е свойство на целия ред, не на клетка: цялата
            // година е един и същ отговор.
            if (marking?.Idle(line) == true)
            {
                var idle = ws.Range(row, 1, row, columns);

                idle.Style.Fill.BackgroundColor = IdleFill;
                idle.Style.Font.FontColor = XLColor.FromArgb(0x77, 0x77, 0x77);
            }

            Numbers(ws, row, line, view, export, running, marking);
            row++;
        }

        if (view.Table.Lines.Count > 0)
        {
            ws.Cell(row, 1).SetValue(Lang.T("Общо", "Total"));

            Numbers(ws, row, view.Table.Totals, view, export, running, marking: null);

            var totals = ws.Range(row, 1, row, columns);
            totals.Style.Font.Bold = true;
            totals.Style.Fill.BackgroundColor = TotalFill;

            row++;
        }

        return row - 1;
    }

    private static void Numbers(
        IXLWorksheet ws, int row, BalanceLine line, BalanceExportView view, BalanceExport export,
        bool running, BalanceMarking? marking)
    {
        var column = 3;

        if (view.Opening) Amount(ws, row, column++, line.Opening);

        // Нулата се изписва празна — както на екрана. Числата в двете таблици
        // се сверяват едно към едно с показаното и разлика в дреболия като
        // тази кара човек да търси къде е сгрешил.
        for (var month = export.From; month <= export.To; month++)
        {
            var value = running ? line.Running[month - 1] : line.Movement[month - 1];
            var cell = Amount(ws, row, column++, value);

            if (marking is not null) Paint(cell, marking.Tint(line, month, out var heat), heat);
        }

        var last = Amount(ws, row, column,
            running ? line.Running[export.To - 1] : line.Total);

        if (marking is not null) Paint(last, marking.Total(line, out var end), end);
    }

    /// <summary>
    /// Слага фона на клетката. Топлината се смесва с бялото на листа, защото
    /// Excel няма прозрачност, а плътният цвят изяжда числото под себе си.
    /// </summary>
    private static void Paint(IXLCell cell, BalanceTint tint, double heat)
    {
        cell.Style.Fill.BackgroundColor = tint switch
        {
            BalanceTint.Negative => NegativeFill,
            BalanceTint.Draft => DraftFill,
            BalanceTint.Peak => PeakFill,
            BalanceTint.Heat => Warm(heat),
            _ => XLColor.NoColor
        };

        if (tint == BalanceTint.Negative) cell.Style.Font.FontColor = XLColor.FromArgb(0xA8, 0x1C, 0x1C);

        // Върху наситения син фон черното изчезва — числото минава на бяло.
        if (tint == BalanceTint.Heat && heat > 0.55) cell.Style.Font.FontColor = XLColor.White;
    }

    private static XLColor Warm(double heat)
    {
        var strength = Math.Clamp(heat, 0, 1) * 0.72;

        int Mix(int channel) => (int)Math.Round(255 - (255 - channel) * strength);

        return XLColor.FromArgb(Mix(0x1F), Mix(0x4E), Mix(0x79));
    }

    private static IXLCell Amount(IXLWorksheet ws, int row, int column, decimal value)
    {
        var cell = ws.Cell(row, column);

        // Точката във формата е буквална: „0.###“ изписва цялото число с увиснал
        // разделител накрая („10,“). Затова целите числа получават свой формат —
        // различни формати в една колона не пречат нито на окото, нито на сбора.
        cell.Style.NumberFormat.Format = value == Math.Truncate(value) ? WholeFormat : AmountFormat;

        if (value != 0) cell.Value = value;

        return cell;
    }
}
