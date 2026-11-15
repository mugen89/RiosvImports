using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Колко се предава от една купчина.
///
/// Нула значи „не тази сега“, а не грешка — същото, което значи и при
/// третирането: човек избира двайсет кода и вади три от тях, вместо да избира
/// наново.
/// </summary>
public sealed record HandOrder(TreatPile Pile, decimal Amount);

/// <summary>
/// Готово движение „предаден“: код, количество и от кои партиди.
///
/// <paramref name="EwcOut"/> е кодът, с който предаването влиза в книгата,
/// когато той се различава от кода, под който отпадъкът стои. Рядко, но се
/// случва: отпадък, приет и заведен под един код, се предава на оползотворител,
/// който го приема под друг, и в книгата трябва да стои неговият.
///
/// Складът винаги се вади по <paramref name="Ewc"/> — иначе наличността не
/// намалява там, откъдето количеството наистина си отива. Същото разделяне като
/// при третирането и със същите две думи, за да не се учи втори път.
/// </summary>
public sealed record HandMove(
    string Ewc, decimal Amount, IReadOnlyList<Draw> Draws, string EwcOut = "");

/// <summary>
/// Кантарът срещу избраното.
///
/// Тук се сблъскват двете числа, с които работи площадката. <b>Заявеното</b> е
/// измереното: камионът е напълнен, качен е на кантар и е върнал трийсет тона.
/// <b>Избраното</b> е сглобеното назад — редовете от склад „Образуван“, за
/// които човек твърди, че са влезли в този камион.
///
/// Двете се разминават почти винаги и точно това разминаване е работата:
/// програмата съществува, за да се развърже сметката „това от кога е и чие е
/// било“, правена досега на ръка. Затова разликата е самостоятелно понятие със
/// знак, а не „грешка“: положителна значи, че до кантара не достига,
/// отрицателна — че избраното го надхвърля.
///
/// Заявено нула значи „не е казано“. Тогава нищо не се сверява — полето е по
/// избор, защото не всяко предаване минава през кантар.
/// </summary>
public sealed record Weighing(decimal Picked, decimal Target)
{
    /// <summary>Колко не достига до заявеното. Отрицателно значи „в повече“.</summary>
    public decimal Gap => SplitMath.Round(Target - Picked);

    /// <summary>Дали изобщо е заявено общо количество.</summary>
    public bool Stated => Target > 0;

    /// <summary>Дали избраното се покрива със заявеното — или такова няма.</summary>
    public bool Matches => !Stated || Gap == 0;

    /// <summary>Избраното надхвърля заявеното.</summary>
    public bool Over => Stated && Gap < 0;

    /// <summary>До заявеното не достига.</summary>
    public bool Short => Stated && Gap > 0;
}

/// <summary>Какво не е наред с поисканото предаване.</summary>
public enum HandProblem
{
    /// <summary>Отрицателно количество.</summary>
    Negative,

    /// <summary>Иска се повече, отколкото стои по този код.</summary>
    OverStock,

    /// <summary>Нито един ред не носи количество.</summary>
    Nothing,

    /// <summary>До заявеното общо количество не достига.</summary>
    Short,

    /// <summary>Избраното надхвърля заявеното общо количество.</summary>
    Over
}

/// <summary>
/// Едно намерено несъответствие — текстът е за екрана, видът за кода.
///
/// При трите складови несъответствия <see cref="Ewc"/> е кодът на реда, а
/// <see cref="Amount"/> — наличното по него. При двете кантарни ред няма: те са
/// за целия камион. Тогава <see cref="Amount"/> носи заявеното, а
/// <see cref="Ewc"/> — размера на разликата, вече изписан. Един запис за пет
/// вида е за предпочитане пред пети вид с шесто поле.
///
/// <see cref="Store"/> е складът, от който се вади — „Образуван“ обикновено,
/// „Получен“ при пряко предаване. Носи се, защото съобщението за недостиг
/// трябва да го НАЗОВЕ: „стоят само 8 т“ без да се каже къде, праща човека да
/// търси разликата в другия склад.
/// </summary>
public sealed record HandIssue(
    HandProblem Kind, string Ewc, decimal Amount, StoreKind Store = StoreKind.Formed)
{
    public string Text => Kind switch
    {
        HandProblem.Negative =>
            Lang.T($"{Ewc} — количеството не може да е отрицателно.",
                   $"{Ewc} — the quantity cannot be negative."),
        HandProblem.OverStock =>
            Lang.T($"{Ewc} — в склад „{Moves.Short(Store)}“ стоят само {SplitMath.Amount(Amount)} т.",
                   $"{Ewc} — only {SplitMath.Amount(Amount)} t stand in the " +
                   $"“{Moves.Short(Store)}” store."),
        HandProblem.Short =>
            Lang.T($"До заявените {SplitMath.Amount(Amount)} т не достигат {Ewc} т. " +
                   "Изравнете или изберете още.",
                   $"{Ewc} t are missing from the stated {SplitMath.Amount(Amount)} t. " +
                   "Balance it out or pick more."),
        HandProblem.Over =>
            Lang.T($"Избраното надхвърля заявените {SplitMath.Amount(Amount)} т с {Ewc} т. " +
                   "Изравнете или махнете редове.",
                   $"What is picked exceeds the stated {SplitMath.Amount(Amount)} t " +
                   $"by {Ewc} t. Balance it out or drop rows."),
        _ => Lang.T("Няма нито един ред с количество за предаване.",
                    "No row carries a quantity to hand over.")
    };
}

/// <summary>
/// Предаването, тръгнало от склада.
///
/// Същата работа като третирането и нарочно същата: избира се какво излиза от
/// един склад, разписва се по партидите му и от това стават движения. Затова
/// купчината е същата <see cref="TreatPile"/>, дялът е същият
/// <see cref="TreatPick"/>, а сглобяването минава през
/// <see cref="Treatment.From"/> — една сметка, а не две, които се разминават на
/// първата поправка.
///
/// Едно нещо е само тук и то е причината предаването да има свой файл:
/// <b>кантарът</b>. Третирането тръгва от склада и свършва в склада — никой не
/// го мери отвън. Предаването тръгва от кантара: камионът е напълнен, теглен е
/// и е върнал число, което е по-вярно от всяка сметка назад. Оттам нататък
/// работата е обратна на обичайната — не „колко взимам“, а „кои редове правят
/// точно толкова“ — и разликата между двете се изравнява тук
/// (<see cref="Onto"/>, <see cref="Scale"/>).
/// </summary>
public static class Handover
{
    /// <summary>
    /// Купчините, от които се предава.
    ///
    /// Същото свеждане като при третирането — и нарочно същото: човек, който е
    /// видял едните, не бива да заварва другите подредени иначе.
    /// </summary>
    public static IReadOnlyList<TreatPile> Piles(IEnumerable<StockShare> shares) =>
        Treatment.Piles(shares);

    /// <summary>Разписва поисканото по партидите на купчината — най-старата първа.</summary>
    public static IReadOnlyList<TreatPick> Spread(TreatPile pile, decimal amount) =>
        Treatment.Spread(pile, amount);

    /// <summary>
    /// Взима отгоре надолу, докато се събере заявеното.
    ///
    /// Редът е този, който човек вижда: той подрежда по дата, пресява
    /// нежеланото и казва „двеста тона“. При партиди от по десет-сто килограма
    /// това са стотици редове — да се маркират на ръка е точно работата, която
    /// програмата трябва да спести.
    ///
    /// Всеки ред се взима цял, докато сборът не стигне или прескочи заявеното;
    /// тогава последният се взима само колкото липсва — изравняването сяда там,
    /// където е прескочено. Не стигнат ли редовете, взима се всичко и сборът
    /// остава под заявеното: повече от стоялото не се предава.
    /// </summary>
    public static IReadOnlyList<TreatPick> FromTop(IEnumerable<StockShare> ordered, decimal target)
    {
        var left = SplitMath.Round(target);
        var picks = new List<TreatPick>();

        foreach (var share in ordered)
        {
            if (left <= 0) break;

            var take = SplitMath.Round(Math.Min(share.Amount, left));
            if (take <= 0) continue;

            picks.Add(new TreatPick(share, take));
            left = SplitMath.Round(left - take);
        }

        return picks;
    }

    /// <summary>Избраното срещу заявеното — двете числа, които трябва да съвпаднат.</summary>
    public static Weighing Weigh(IEnumerable<TreatPick> picks, decimal target) =>
        new(SplitMath.Round(picks.Sum(p => p.Amount)), SplitMath.Round(Math.Max(0m, target)));

    /// <summary>
    /// Разликата отива в ЕДИН посочен дял.
    ///
    /// Това е обичайният отговор на кантара и той не е сметка, а знание: човекът
    /// знае кой ред е насипният — този, от който е догребано, докато камионът се
    /// напълни — и иска разликата да седне точно там, а не размазана по всички.
    ///
    /// <paramref name="room"/> е таванът на този дял: наличното по него. Оттам
    /// идва и второто върнато число — колко от разликата НЕ се е побрало. То не
    /// се премълчава: „изравних“, което е изравнило наполовина, е по-лошо от
    /// „остават още 0,400 т“.
    /// </summary>
    public static (decimal Amount, decimal Left) Onto(decimal take, decimal room, decimal gap)
    {
        var wanted = SplitMath.Round(take + gap);
        var amount = Math.Clamp(wanted, 0m, SplitMath.Round(Math.Max(0m, room)));

        return (amount, SplitMath.Round(wanted - amount));
    }

    /// <summary>
    /// Разликата се разнася по всички дялове, в тяхното съотношение.
    ///
    /// Другият отговор на кантара, за случая, в който няма един виновен ред:
    /// камионът е товарен от седем купчини и разликата е от мерене, а не от
    /// пропуснат ред. Тогава единственото честно разпределение е същото
    /// съотношение — друго никой не е измервал (същото правило, по което
    /// образуваното наследява произхода си).
    ///
    /// Сборът излиза точно заявеното, доколкото складът стига: дял, който не
    /// може да порасне, отстъпва мястото си на онези, които могат.
    /// </summary>
    public static IReadOnlyList<TreatPick> Scale(IReadOnlyList<TreatPick> picks, decimal target)
    {
        var wanted = SplitMath.Round(target);
        var current = SplitMath.Round(picks.Sum(p => p.Amount));

        if (wanted <= 0 || current <= 0) return picks;

        // Точното съотношение, до грам — не през процент със два знака, който
        // при стотици партиди местеше килограми от един ред в друг.
        var shares = Allocation.Prorate(wanted, [.. picks.Select(p => p.Amount)]);

        var scaled = picks
            .Select((p, i) => new TreatPick(p.Share, Math.Min(shares[i], p.Share.Amount)))
            .ToList();

        // Каквото не се е побрало в един дял, отива при тези, които имат място.
        // Без тази обиколка вдигането до кантара спира на първата пълна партида
        // и сборът остава под заявеното, без да си личи защо.
        var left = SplitMath.Round(wanted - scaled.Sum(p => p.Amount));

        for (var i = 0; i < scaled.Count && left > 0; i++)
        {
            var room = SplitMath.Round(scaled[i].Share.Amount - scaled[i].Amount);
            var add = Math.Min(room, left);

            if (add <= 0) continue;

            scaled[i] = new TreatPick(scaled[i].Share, SplitMath.Round(scaled[i].Amount + add));
            left = SplitMath.Round(left - add);
        }

        return [.. scaled.Where(p => p.Amount > 0)];
    }

    /// <summary>
    /// Проверява поисканото срещу наличното и срещу кантара.
    ///
    /// Празен списък значи, че предаването се държи. Връщат се причини, не
    /// „да/не“ — питането и съобщението са работа на повикващия (правило 4).
    ///
    /// <paramref name="target"/> нула значи „не е заявено общо количество“:
    /// тогава се проверява само складът, както при третирането.
    ///
    /// <paramref name="store"/> влиза само в текста на съобщението — сметката е
    /// същата от кой и да е склад. Казва се обаче, защото при пряко предаване
    /// недостигът е в „Получен“, а не в „Образуван“.
    /// </summary>
    public static IReadOnlyList<HandIssue> Check(
        IEnumerable<HandOrder> orders, decimal target = 0m, StoreKind store = StoreKind.Formed)
    {
        var issues = new List<HandIssue>();
        decimal total = 0;

        foreach (var order in orders)
        {
            var wanted = SplitMath.Round(order.Amount);

            if (wanted < 0)
            {
                issues.Add(new HandIssue(HandProblem.Negative, order.Pile.Ewc, wanted, store));
                continue;
            }

            if (wanted > order.Pile.Available)
            {
                issues.Add(new HandIssue(
                    HandProblem.OverStock, order.Pile.Ewc, order.Pile.Available, store));

                continue;
            }

            total += wanted;
        }

        if (total <= 0)
        {
            issues.Add(new HandIssue(HandProblem.Nothing, string.Empty, 0m, store));
            return issues;
        }

        var weighing = new Weighing(SplitMath.Round(total), SplitMath.Round(Math.Max(0m, target)));

        if (weighing.Matches) return issues;

        issues.Add(new HandIssue(
            weighing.Short ? HandProblem.Short : HandProblem.Over,
            SplitMath.Amount(Math.Abs(weighing.Gap)),
            weighing.Target,
            store));

        return issues;
    }

    /// <summary>
    /// Сглобява движенията от поисканото по кодове.
    ///
    /// Покритието се избира само, по FIFO — най-старото приемане първо. Ползва
    /// се, когато човек е казал само „толкова от този код“.
    /// </summary>
    public static IReadOnlyList<HandMove> Plan(
        IEnumerable<HandOrder> orders, bool perBatch = false, string ewcOut = "")
    {
        var spread = new List<TreatPick>();

        foreach (var order in orders) spread.AddRange(Spread(order.Pile, order.Amount));

        return From(spread, perBatch, ewcOut);
    }

    /// <summary>
    /// Сглобява движенията от ИЗРИЧНО посочени дялове.
    ///
    /// Минава през <see cref="Treatment.From"/>, защото събирането по код и
    /// разделянето по партиди са една и съща работа за двете движения. Второ
    /// копие би се разминало с първото на първата поправка — а разминаването
    /// тук значи предадено количество, което сочи към чужда партида.
    ///
    /// <b>Оттук идва произходът на предаденото.</b> Без посочени дялове записът
    /// търси партида по кода на движението, а партидата помни кода, с който
    /// отпадъкът е ДОШЪЛ; при предаване на образуван отпадък двата не съвпадат
    /// никога и веригата се късаше на последната стъпка.
    /// </summary>
    public static IReadOnlyList<HandMove> From(
        IEnumerable<TreatPick> chosen, bool perBatch = false, string ewcOut = "") =>
    [
        .. Treatment.From(chosen, perBatch, ewcOut)
            .Select(m => new HandMove(m.Ewc, m.Amount, m.Draws, m.EwcOut))
    ];
}
