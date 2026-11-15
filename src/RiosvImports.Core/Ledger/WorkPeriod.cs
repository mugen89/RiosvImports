using System.Globalization;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Работният период: между кои дати се пише СЕГА.
///
/// Не е същото като затворен месец и нарочно не е. Затвореният месец е
/// <b>заключалка</b>: месецът е отчетен, подаден е и не се пипа от никого.
/// Работният период е <b>самоконтрол</b>: човекът сам казва „тази седмица
/// въвеждам септември“ и програмата го държи за думата — но той може да я
/// оттегли с една отметка, защото правилото е негово, а не на закона.
///
/// Заради какво съществува: грешките се случват в първите дни на новия месец,
/// когато старият още се дописва. Датата в прозорчето стои на днешната, човек
/// въвежда вчерашното количество и то отива в новия месец. Сгрешената дата
/// изглежда точно като дата — това е единственото поле, което не се вижда, че е
/// грешно — и излиза чак когато отчетът не излезе.
///
/// Периодът е ДВЕ ДАТИ, а не месец, защото хората работят на различни ритми:
/// един въвежда веднъж месечно, друг — всеки петък за изминалата седмица. Месец
/// е просто най-честият период и има свое копче, не свой вид.
/// </summary>
public sealed record WorkPeriod(DateOnly From, DateOnly To, bool On = true)
{
    /// <summary>Никакво ограничение — така се държи програмата, докато не я вържат.</summary>
    public static WorkPeriod Off => new(default, default, false);

    /// <summary>Цял месец — най-честият период.</summary>
    public static WorkPeriod Of(LedgerPeriod period) => new(period.First, period.Last);

    /// <summary>Седмицата, в която пада денят — от понеделник до неделя.</summary>
    public static WorkPeriod Week(DateOnly day)
    {
        var back = ((int)day.DayOfWeek + 6) % 7;
        var monday = day.AddDays(-back);

        return new WorkPeriod(monday, monday.AddDays(6));
    }

    /// <summary>Дали периодът изобщо се държи — обърнати дати не вързват нищо.</summary>
    public bool Valid => !On || From <= To;

    /// <summary>Дали датата пада вътре. Изключен период приема всичко.</summary>
    public bool Covers(DateOnly date) => !On || !Valid || (date >= From && date <= To);

    /// <summary>Колко дни хваща.</summary>
    public int Days => On && Valid ? To.DayNumber - From.DayNumber + 1 : 0;

    /// <summary>Дали покрива точно един цял месец — тогава се изписва с името му.</summary>
    public bool IsMonth =>
        On && Valid &&
        From == LedgerPeriod.Of(From).First &&
        To == LedgerPeriod.Of(From).Last;

    /// <summary>
    /// Как се чете на екрана.
    ///
    /// Цял месец се изписва с ИМЕТО си („септември 2026“), защото точно месецът
    /// се бърка и „01.09.2026 – 30.09.2026“ не се чете като септември достатъчно
    /// бързо, за да се хване с крайчеца на окото.
    /// </summary>
    public string Title
    {
        get
        {
            if (!On) return Lang.T("без ограничение", "no limit");
            if (!Valid) return Lang.T("сгрешен период", "an invalid period");

            if (IsMonth) return LedgerPeriod.Of(From).Title();

            var from = From.ToString(ValueConverter.BulgarianDateFormat);
            var to = To.ToString(ValueConverter.BulgarianDateFormat);

            return From == To ? from : $"{from} – {to}";
        }
    }

    /// <summary>
    /// Периодът с границите си — за местата, където човек СВЕРЯВА дата.
    ///
    /// „септември 2026“ казва кой е периодът, но не и докъде стига. Човек с
    /// дата 01.10 вижда името на месеца и пак не знае с един ден ли е сбъркал,
    /// или с цял месец — а точно това решава дали да поправи датата, или да
    /// вдигне отметката. Затова до името вървят и двете граници.
    ///
    /// Период, който не е цял месец, вече се изписва с датите си и се връща
    /// какъвто е. В настройките се ползва <see cref="Title"/>: там периодът се
    /// ИЗБИРА и границите му стоят в самите полета за дати.
    /// </summary>
    public string Bounds =>
        !On || !Valid || !IsMonth
            ? Title
            : $"{Title} ({From.ToString(ValueConverter.BulgarianDateFormat)} – " +
              $"{To.ToString(ValueConverter.BulgarianDateFormat)})";

    /// <summary>Какво да се каже, когато датата е извън периода.</summary>
    public string Miss(DateOnly date) => Lang.T(
        $"{date.ToString(ValueConverter.BulgarianDateFormat)} е извън работния период: " +
        $"{Bounds}. Вдигнете отметката, ако е нарочно.",
        $"{date.ToString(ValueConverter.BulgarianDateFormat)} falls outside the working period: " +
        $"{Bounds}. Tick the box if you mean it.");

    /// <summary>
    /// Записва се като „2026-09-01|2026-09-30|1“.
    ///
    /// Текст в настройките, а не колони по базата: това е предпочитание на
    /// човека, не данни за склада, и нова фирма не бива да иска стъпка по
    /// базата (същото решение като при началния месец).
    /// </summary>
    public string Text => On && Valid ? $"{Iso(From)}|{Iso(To)}|1" : string.Empty;

    /// <summary>Разчита записаното. Всичко неразбрано значи „без ограничение“.</summary>
    public static WorkPeriod Read(string? text)
    {
        var parts = text?.Split('|');
        if (parts is not { Length: 3 } || parts[2] != "1") return Off;

        if (!DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var from)) return Off;

        if (!DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var to)) return Off;

        var period = new WorkPeriod(from, to);

        return period.Valid ? period : Off;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
