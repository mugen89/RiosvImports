using RiosvImports.Core.Localization;

namespace RiosvImports.Core.Balance;

/// <summary>
/// Четирите раздела на отчетната книга за оползотворяване.
///
/// Изброяване, а не низ с името на възела: балансът пита за знак и за
/// подредба, а низът не отговаря нито на едното, нито на другото, без някой
/// да го сравни на ръка на пет места.
/// </summary>
public enum BalanceSheet
{
    /// <summary>Получен отпадък.</summary>
    Received,

    /// <summary>Обработен отпадък.</summary>
    Treated,

    /// <summary>Образуван отпадък.</summary>
    Generated,

    /// <summary>Предаден отпадък.</summary>
    Submitted
}

/// <summary>
/// Какво знае балансът за разделите: как се казват в схемата и с какъв знак
/// влизат, когато четирите се кръстосат в едно.
/// </summary>
public static class BalanceSheets
{
    /// <summary>Отчетът, за който изобщо има баланс.</summary>
    public const string Report = "r_b_recovery_dispose";

    /// <summary>Разделите в реда, в който отпадъкът минава през площадката.</summary>
    public static readonly IReadOnlyList<BalanceSheet> All =
    [
        BalanceSheet.Received, BalanceSheet.Treated, BalanceSheet.Generated, BalanceSheet.Submitted
    ];

    /// <summary>Името на възела в схемата.</summary>
    public static string Node(BalanceSheet sheet) => sheet switch
    {
        BalanceSheet.Received => "r_b_rd_received",
        BalanceSheet.Treated => "r_b_rd_treated",
        BalanceSheet.Generated => "r_b_rd_generated",
        _ => "r_b_rd_submitted"
    };

    /// <summary>Кой раздел е това — null, ако възелът не участва в баланса.</summary>
    public static BalanceSheet? Of(string node) =>
        All.Select(s => (BalanceSheet?)s).FirstOrDefault(s => Node(s!.Value) == node);

    /// <summary>
    /// Знакът, с който разделът влиза в кръстосания изглед.
    ///
    /// Образуваният отпадък се трупа на площадката, предаденият си отива —
    /// това са двете движения, от които излиза наличното. Полученият и
    /// третираният описват едно и също количество на две стъпки от пътя му и
    /// в сбора биха се самоизбродили; те се гледат поотделно, в собствения си
    /// изглед, където числата са такива, каквито са записани.
    /// </summary>
    public static int CrossSign(BalanceSheet sheet) => sheet switch
    {
        BalanceSheet.Generated => 1,
        BalanceSheet.Submitted => -1,
        _ => 0
    };

    /// <summary>Дали разделът изобщо участва в кръстосването.</summary>
    public static bool Crosses(BalanceSheet sheet) => CrossSign(sheet) != 0;

    /// <summary>
    /// Съкратеното име на месеца — заглавие на колона и на екрана, и в
    /// изнесения файл.
    ///
    /// На едно място, защото двете таблици трябва да се четат като една и
    /// съща: човек сверява изнесеното с показаното ред по ред.
    /// </summary>
    public static string Month(int month) => month switch
    {
        1 => Lang.T("яну", "Jan"),
        2 => Lang.T("фев", "Feb"),
        3 => Lang.T("мар", "Mar"),
        4 => Lang.T("апр", "Apr"),
        5 => Lang.T("май", "May"),
        6 => Lang.T("юни", "Jun"),
        7 => Lang.T("юли", "Jul"),
        8 => Lang.T("авг", "Aug"),
        9 => Lang.T("сеп", "Sep"),
        10 => Lang.T("окт", "Oct"),
        11 => Lang.T("ное", "Nov"),
        _ => Lang.T("дек", "Dec")
    };

    /// <summary>
    /// Как се казва разделът на екрана.
    ///
    /// Метод, а не готов списък: статично поле се пълни веднъж, с езика, който
    /// е бил в сила тогава, и после не се мени при смяна на езика.
    /// </summary>
    public static string Title(BalanceSheet sheet) => sheet switch
    {
        BalanceSheet.Received => Lang.T("Получен", "Received"),
        BalanceSheet.Treated => Lang.T("Третиран", "Treated"),
        BalanceSheet.Generated => Lang.T("Образуван", "Generated"),
        _ => Lang.T("Предаден", "Submitted")
    };
}

/// <summary>
/// Едно движение: толкова тона по този код, в този раздел, в този месец.
///
/// Датата вече е сведена до месец, а количеството — до число. Оттук нататък
/// балансът не знае нищо за отчети, за редове и за формати на дати.
/// </summary>
public sealed record BalanceEntry(BalanceSheet Sheet, string Ewc, int Month, decimal Amount);

/// <summary>
/// Един ред от таблицата: код, начално количество и дванайсетте месеца в двата
/// им вида.
///
/// И двата се смятат наведнъж, защото са едно и също число, гледано два пъти:
/// движението е какво се е случило през месеца, салдото — какво е останало в
/// края му. Превключвателят на екрана само избира кое да покаже.
/// </summary>
public sealed record BalanceLine(
    string Ewc,
    decimal Opening,
    IReadOnlyList<decimal> Movement,
    IReadOnlyList<decimal> Running)
{
    /// <summary>Сборът на движенията за годината.</summary>
    public decimal Total => Movement.Sum();

    /// <summary>Каквото остава в края на декември.</summary>
    public decimal Closing => Running[^1];

    /// <summary>Ред без нито едно движение и без начално количество.</summary>
    public bool IsEmpty => Opening == 0 && Movement.All(m => m == 0);
}

/// <summary>Готовата таблица: редовете по код и сборният ред под тях.</summary>
public sealed record BalanceTable(IReadOnlyList<BalanceLine> Lines, BalanceLine Totals)
{
    public bool IsEmpty => Lines.Count == 0;
}
