namespace RiosvImports.Core.Balance;

/// <summary>Един код с дела си — за подредбите и за кръговата диаграма.</summary>
public sealed record BalanceShare(string Ewc, string Name, decimal Amount, double Share);

/// <summary>
/// Числата зад таблото.
///
/// Таблицата отговаря на „колко по кой код и в кой месец“. Таблото отговаря на
/// други въпроси — „накъде вървим“, „кое тежи най-много“, „какво стои
/// непокътнато“ — и те се смятат веднъж тук, вместо всяка плочка да рови сама
/// из същите редове.
/// </summary>
public sealed record BalanceStats
{
    public required int Year { get; init; }

    /// <summary>Колко отчета са влезли.</summary>
    public required int Reports { get; init; }

    /// <summary>Колко различни кода има в баланса.</summary>
    public required int Codes { get; init; }

    public required decimal Opening { get; init; }
    public required decimal Closing { get; init; }

    /// <summary>Сборовете по раздели за годината — такива, каквито са записани.</summary>
    public required IReadOnlyDictionary<BalanceSheet, decimal> Sheets { get; init; }

    /// <summary>Салдото на цялата площадка в края на всеки месец.</summary>
    public required IReadOnlyList<decimal> Running { get; init; }

    /// <summary>Образуваното по месеци — вдига салдото.</summary>
    public required IReadOnlyList<decimal> Generated { get; init; }

    /// <summary>Предаденото по месеци — сваля го.</summary>
    public required IReadOnlyList<decimal> Submitted { get; init; }

    /// <summary>Кодовете по салдо в края, най-големият пръв.</summary>
    public required IReadOnlyList<BalanceShare> ByClosing { get; init; }

    /// <summary>Кодовете по образувано количество за годината.</summary>
    public required IReadOnlyList<BalanceShare> ByGenerated { get; init; }

    /// <summary>Кодовете с отрицателно салдо — предадено повече от наличното.</summary>
    public required IReadOnlyList<BalanceShare> Negative { get; init; }

    /// <summary>Кодовете с начално количество и нито едно движение през годината.</summary>
    public required IReadOnlyList<BalanceShare> Idle { get; init; }

    /// <summary>Най-натовареният месец по движение — 0, ако няма нито едно.</summary>
    public required int BusiestMonth { get; init; }

    /// <summary>Има ли изобщо какво да се показва.</summary>
    public bool IsEmpty => Codes == 0 && Reports == 0;

    // ------------------------------------------------------------- сглобяване

    /// <summary>
    /// Сглобява числата от вече пресметнатите таблици.
    ///
    /// Приема ги наготово, вместо да ги смята сама: екранът и без това ги е
    /// направил за таблицата, а второ смятане от същите редове е втори повод
    /// двете места да се разминат.
    /// </summary>
    public static BalanceStats Build(
        int year,
        int reports,
        BalanceTable cross,
        IReadOnlyDictionary<BalanceSheet, BalanceTable> sheets,
        IReadOnlyDictionary<string, string> names,
        int top = 8)
    {
        var generated = Months(sheets, BalanceSheet.Generated);
        var submitted = Months(sheets, BalanceSheet.Submitted);

        var byClosing = Shares(cross.Lines, names, line => line.Closing);
        var byGenerated = Shares(Lines(sheets, BalanceSheet.Generated), names, line => line.Total);

        var busiest = 0;
        var most = 0m;

        for (var month = 1; month <= BalanceMath.Months; month++)
        {
            var moved = generated[month - 1] + Math.Abs(submitted[month - 1]);
            if (moved <= most) continue;

            most = moved;
            busiest = month;
        }

        return new BalanceStats
        {
            Year = year,
            Reports = reports,
            Codes = cross.Lines.Count,

            Opening = cross.Totals.Opening,
            Closing = cross.Totals.Closing,

            Sheets = BalanceSheets.All.ToDictionary(
                sheet => sheet,
                sheet => Lines(sheets, sheet).Sum(line => line.Total)),

            Running = cross.Totals.Running,
            Generated = generated,
            Submitted = submitted,

            ByClosing = [.. byClosing.Where(s => s.Amount != 0).Take(top)],
            ByGenerated = [.. byGenerated.Where(s => s.Amount != 0).Take(top)],

            Negative = [.. byClosing.Where(s => s.Amount < 0)],

            Idle =
            [
                .. cross.Lines
                    .Where(line => line.Opening != 0 && line.Movement.All(m => m == 0))
                    .Select(line => new BalanceShare(line.Ewc, Name(names, line.Ewc), line.Opening, 0))
                    .OrderByDescending(s => s.Amount)
            ],

            BusiestMonth = busiest
        };
    }

    private static IReadOnlyList<BalanceLine> Lines(
        IReadOnlyDictionary<BalanceSheet, BalanceTable> sheets, BalanceSheet sheet) =>
        sheets.TryGetValue(sheet, out var table) ? table.Lines : [];

    private static IReadOnlyList<decimal> Months(
        IReadOnlyDictionary<BalanceSheet, BalanceTable> sheets, BalanceSheet sheet) =>
        sheets.TryGetValue(sheet, out var table) ? table.Totals.Movement : new decimal[BalanceMath.Months];

    /// <summary>
    /// Кодовете, подредени по големина, с дела на всеки.
    ///
    /// Делът се мери спрямо сбора на положителните: отрицателното салдо е
    /// грешка в отчета, а не дял от цялото, и включено в знаменателя прави
    /// всички останали дялове неверни.
    /// </summary>
    private static List<BalanceShare> Shares(
        IReadOnlyList<BalanceLine> lines,
        IReadOnlyDictionary<string, string> names,
        Func<BalanceLine, decimal> amount)
    {
        var total = lines.Select(amount).Where(a => a > 0).Sum();

        return
        [
            .. lines
                .Select(line => new BalanceShare(
                    line.Ewc,
                    Name(names, line.Ewc),
                    amount(line),
                    total == 0 ? 0 : (double)(amount(line) / total)))
                .OrderByDescending(share => share.Amount)
        ];
    }

    private static string Name(IReadOnlyDictionary<string, string> names, string ewc) =>
        names.TryGetValue(ewc, out var name) && name.Length > 0 ? name : ewc;
}
