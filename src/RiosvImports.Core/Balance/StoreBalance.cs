using RiosvImports.Core.Ledger;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Balance;

/// <summary>
/// Балансът по склада — текущото състояние, без да се чака отчет.
///
/// Балансът по отчети брои само проверените книги: той казва какво е
/// ОТЧЕТЕНО. Докато месецът тече, човек пита друго — какво стои на площадката
/// сега — а отговорът е в движенията на склада, от които отчетът после ще
/// бъде съставен.
///
/// Затова тук движенията се превръщат в същите <see cref="BalanceEntry"/>,
/// които балансът чете от отчетите, и по същите правила, по които
/// <see cref="BookProjection"/> пише книгата: всеки вид движение е своят
/// раздел, а кодът е онзи, под който редът влиза в отчета — изходният, ако е
/// посочен. Така двата баланса се сверяват ред по ред: разликата между тях е
/// точно неотчетеното.
/// </summary>
public static class StoreBalance
{
    /// <summary>Разделът на книгата, в който влиза движение от този вид.</summary>
    public static BalanceSheet Sheet(MoveKind kind) => kind switch
    {
        MoveKind.Received => BalanceSheet.Received,
        MoveKind.Treated => BalanceSheet.Treated,
        MoveKind.Generated => BalanceSheet.Generated,
        _ => BalanceSheet.Submitted
    };

    /// <summary>Кодът, под който движението влиза в отчета.</summary>
    public static string Reported(Move move) =>
        ValueConverter.NormalizeCode(move.EwcOut.Length > 0 ? move.EwcOut : move.Ewc);

    /// <summary>
    /// Движенията на годината като редове на баланса.
    ///
    /// Завареното не е движение на годината — то не е прието, нито
    /// образувано тогава, а обявено като стоящо. То влиза в началното салдо
    /// (<see cref="Openings"/>), както го пропуска и книгата.
    ///
    /// Рециклираното не влиза по същото правило, по което го пропуска и
    /// книгата (<see cref="Moves.InBook"/>): балансът по склада се сверява с
    /// баланса по отчети ред по ред, и рециклиран ред тук би бил разлика, която
    /// никой отчет няма как да затвори.
    /// </summary>
    public static IReadOnlyList<BalanceEntry> Entries(IEnumerable<Move> moves, int year) =>
    [
        .. moves
            .Where(m => !m.Opening && Moves.InBook(m.Kind) && m.Date.Year == year && m.Amount != 0)
            .Select(m => new BalanceEntry(Sheet(m.Kind), Reported(m), m.Date.Month, m.Amount))
            .Where(e => e.Ewc.Length > 0)
    ];

    /// <summary>
    /// Началното салдо на годината — по код, в кръстосания смисъл на баланса
    /// (образуван минус предаден).
    ///
    /// Всичко отпреди 1 януари се събира, а завареното — където и да е
    /// обявено. Заварено насред годината е купчина, която е стояла на
    /// площадката и преди воденето да започне: тя е част от началото, а не
    /// движение на месеца, в който е обявена.
    /// </summary>
    public static Dictionary<string, decimal> Openings(IEnumerable<Move> moves, int year)
    {
        var openings = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var move in moves)
        {
            if (!Moves.InBook(move.Kind)) continue;

            var before = move.Date.Year < year;
            var declared = move.Opening && move.Date.Year <= year;

            if (!before && !declared) continue;

            var sign = BalanceSheets.CrossSign(Sheet(move.Kind));
            if (sign == 0) continue;

            var code = Reported(move);
            if (code.Length == 0) continue;

            openings[code] = SplitMath.Round(openings.GetValueOrDefault(code) + sign * move.Amount);
        }

        foreach (var code in openings.Where(o => o.Value == 0).Select(o => o.Key).ToList())
            openings.Remove(code);

        return openings;
    }

    /// <summary>Годините, за които в склада има какво да се гледа — и текущата.</summary>
    public static IReadOnlyList<int> Years(IEnumerable<Move> moves, int today) =>
        [.. moves.Select(m => m.Date.Year).Append(today).Distinct().OrderByDescending(y => y)];
}
