using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Купчина в склада: един код и всичко, което стои под него.
///
/// Третирането се мисли по кодове, не по доставки. Човек не решава „ще третирам
/// партида 2026-0042“; той решава „ще третирам осемнайсетте тона кабел“ — а от
/// кои приемания ще излязат те, е следствие, не избор.
///
/// Затова купчината е единицата, с която говори екранът, и точно затова
/// партидите зад нея не изчезват: те остават като дялове и произходът се пази
/// нататък, без да бъде питан. Ако утре поискат произходът да се обявява,
/// числото вече е тук, вместо да се смята назад по дати.
/// </summary>
public sealed record TreatPile(string Ewc, IReadOnlyList<StockShare> Shares)
{
    /// <summary>Колко стои в купчината.</summary>
    public decimal Available => SplitMath.Round(Shares.Sum(s => s.Amount));

    /// <summary>От колко приемания е събрана.</summary>
    public int Batches => Shares.Select(s => s.Batch.Id).Distinct().Count();

    /// <summary>Откога стои най-старото в нея — въпросът на временния склад.</summary>
    public DateOnly Oldest => Shares.Count == 0 ? default : Shares.Min(s => s.Batch.ReceivedOn);

    /// <summary>Колко дни стои най-старото към дадена дата.</summary>
    public int Age(DateOnly on) => Shares.Count == 0 ? 0 : on.DayNumber - Oldest.DayNumber;
}

/// <summary>
/// Колко се третира от една купчина.
///
/// Нула значи „не тази сега“, а не грешка: човек избира двайсет кода и вади три
/// от тях, вместо да избира наново.
/// </summary>
public sealed record TreatOrder(TreatPile Pile, decimal Amount);

/// <summary>
/// Колко от един наличен дял влиза в третирането.
///
/// Не се пита — извежда се. Купчината се изчерпва от най-старото приемане
/// нататък, защото на временен склад друг ред няма смисъл: каквото е дошло
/// първо, първо трябва и да си отиде.
/// </summary>
public sealed record TreatPick(StockShare Share, decimal Amount);

/// <summary>
/// Готово движение „третиран“: код, количество и от кои партиди.
///
/// <paramref name="EwcOut"/> е кодът, под който третирането влиза в книгата,
/// когато той се различава от приетия. Складът винаги се вади по
/// <paramref name="Ewc"/> — иначе наличността не намалява там, откъдето
/// количеството наистина си отива.
/// </summary>
public sealed record TreatMove(
    string Ewc, decimal Amount, IReadOnlyList<Draw> Draws, string EwcOut = "");

/// <summary>Какво не е наред с поисканото третиране.</summary>
public enum TreatProblem
{
    /// <summary>Отрицателно количество.</summary>
    Negative,

    /// <summary>Иска се повече, отколкото стои по този код.</summary>
    OverStock,

    /// <summary>Нито един ред не носи количество.</summary>
    Nothing
}

/// <summary>Едно намерено несъответствие — текстът е за екрана, видът за кода.</summary>
public sealed record TreatIssue(TreatProblem Kind, string Ewc, decimal Amount)
{
    public string Text => Kind switch
    {
        TreatProblem.Negative =>
            Lang.T($"{Ewc} — количеството не може да е отрицателно.",
                   $"{Ewc} — the quantity cannot be negative."),
        TreatProblem.OverStock =>
            Lang.T($"{Ewc} — налични са само {SplitMath.Amount(Amount)} т.",
                   $"{Ewc} — only {SplitMath.Amount(Amount)} t are available."),
        _ => Lang.T("Няма нито един ред с количество за третиране.",
                    "No row carries a quantity to treat.")
    };
}

/// <summary>
/// Третирането, тръгнало от склада.
///
/// В „Месец“ третирането се въвежда като количество по код и чие е било се
/// познава по FIFO. От склада въпросът е същият, но питането е обърнато: човек
/// вижда купчините, казва колко от коя взема, и произходът излиза сам от
/// избраното. Затова сметката е тук, а не в прозореца: същото правило важи и за
/// един ред, и за двайсет избрани, и се проверява с тест без нито един прозорец
/// (правило 4).
///
/// Групирането по код е отделен въпрос от произхода. Обединени или не, дяловете
/// са едни и същи — <see cref="LedgerLink"/> носи произхода, а не редът. Изборът
/// е само какво ще види НИСО: един ред „третирани 15 т“ или три реда по партиди.
/// </summary>
public static class Treatment
{
    /// <summary>
    /// Свежда избраните дялове до купчини по код.
    ///
    /// Вътре в купчината редът е по дата на приемане: точно редът, по който тя
    /// ще бъде изчерпана, за да се чете списъкът както ще се похарчи.
    /// </summary>
    public static IReadOnlyList<TreatPile> Piles(IEnumerable<StockShare> shares) =>
    [
        .. shares
            .Where(s => s.Amount > 0)
            .GroupBy(s => ValueConverter.NormalizeCode(s.Ewc), StringComparer.Ordinal)
            .Where(g => g.Key.Length > 0)
            .Select(g => new TreatPile(g.Key,
            [
                .. g.OrderBy(s => s.Batch.ReceivedOn).ThenBy(s => s.Batch.Id)
            ]))
            .OrderBy(p => p.Ewc, StringComparer.Ordinal)
    ];

    /// <summary>
    /// Разпределя поисканото по партидите на купчината — най-старата първа.
    ///
    /// Не отказва при недостиг, а връща каквото е успяло: колко липсва казва
    /// <see cref="Check"/>, а какво да се прави с това решава човекът. Отказ без
    /// число не му казва нищо.
    /// </summary>
    public static IReadOnlyList<TreatPick> Spread(TreatPile pile, decimal amount)
    {
        var left = SplitMath.Round(amount);
        if (left <= 0) return [];

        var picks = new List<TreatPick>();

        foreach (var share in pile.Shares)
        {
            if (left <= 0) break;

            var take = SplitMath.Round(Math.Min(share.Amount, left));
            if (take <= 0) continue;

            picks.Add(new TreatPick(share, take));
            left = SplitMath.Round(left - take);
        }

        return picks;
    }

    /// <summary>
    /// Проверява поисканото срещу наличното.
    ///
    /// Празен списък значи, че третирането се държи. Връщат се причини, не
    /// „да/не“ — питането и съобщението са работа на повикващия.
    /// </summary>
    public static IReadOnlyList<TreatIssue> Check(IEnumerable<TreatOrder> orders)
    {
        var issues = new List<TreatIssue>();
        decimal total = 0;

        foreach (var order in orders)
        {
            var wanted = SplitMath.Round(order.Amount);

            if (wanted < 0)
            {
                issues.Add(new TreatIssue(TreatProblem.Negative, order.Pile.Ewc, wanted));
                continue;
            }

            if (wanted > order.Pile.Available)
            {
                issues.Add(new TreatIssue(TreatProblem.OverStock, order.Pile.Ewc, order.Pile.Available));
                continue;
            }

            total += wanted;
        }

        if (total <= 0) issues.Add(new TreatIssue(TreatProblem.Nothing, string.Empty, 0m));

        return issues;
    }

    /// <summary>
    /// Сглобява движенията от поисканото.
    ///
    /// <paramref name="perBatch"/> решава колко реда излизат: по подразбиране
    /// един на код — третирането е работа върху купчина, а не върху доставките
    /// поотделно. С вдигнат флаг всяка партида си получава свой ред, когато
    /// отчетът трябва да върви доставка по доставка.
    ///
    /// <paramref name="ewcOut"/> е общият изходен код. Празно значи „всеки със
    /// своя“ — обичайното. Зададен, той отива в книгата, а складът продължава да
    /// се вади по приетия код: две различни неща, които дотук се изписваха с
    /// една дума.
    ///
    /// Редовете без количество отпадат тук, а не при повикващия: „не тази
    /// купчина“ не бива да става движение от нула тона.
    /// </summary>
    public static IReadOnlyList<TreatMove> Plan(
        IEnumerable<TreatOrder> orders, bool perBatch = false, string ewcOut = "")
    {
        var spread = new List<TreatPick>();

        foreach (var order in orders) spread.AddRange(Spread(order.Pile, order.Amount));

        return From(spread, perBatch, ewcOut);
    }

    /// <summary>
    /// Сглобява движенията от ИЗРИЧНО посочени дялове.
    ///
    /// <see cref="Plan"/> стига дотук през FIFO: казано е „двайсет тона от този
    /// код“ и редът се избира сам. Но когато човек е посочил кои партиди и по
    /// колко от всяка — а на площадка с осемдесет доставки той го прави, защото
    /// третира тази и онази, не „най-старите двайсет тона“ — изборът му минава
    /// направо, без да се пресмята наново.
    ///
    /// Оттук нататък двата пътя са един: същото събиране по код, същото
    /// разделяне по партиди, същият изходен код.
    /// </summary>
    public static IReadOnlyList<TreatMove> From(
        IEnumerable<TreatPick> chosen, bool perBatch = false, string ewcOut = "")
    {
        var picks = chosen
            .Where(p => p.Amount > 0)
            .OrderBy(p => ValueConverter.NormalizeCode(p.Share.Ewc), StringComparer.Ordinal)
            .ThenBy(p => p.Share.Batch.ReceivedOn)
            .ThenBy(p => p.Share.Batch.Id)
            .ToList();

        var outgoing = ValueConverter.NormalizeCode(ewcOut);
        var moves = new List<TreatMove>();

        if (perBatch)
        {
            foreach (var pick in picks)
            {
                var code = ValueConverter.NormalizeCode(pick.Share.Ewc);

                moves.Add(new TreatMove(
                    code, pick.Amount, [new Draw(pick.Share.Batch.Id, pick.Amount)], Out(code, outgoing)));
            }

            return moves;
        }

        foreach (var group in picks
                     .GroupBy(p => ValueConverter.NormalizeCode(p.Share.Ewc), StringComparer.Ordinal))
        {
            // Два дяла към една партида се събират: ключът на връзката е
            // (движение, партида) и вторият иначе би изместил първия.
            var draws = new Dictionary<long, decimal>();

            foreach (var pick in group)
                draws[pick.Share.Batch.Id] = draws.GetValueOrDefault(pick.Share.Batch.Id) + pick.Amount;

            moves.Add(new TreatMove(
                group.Key,
                SplitMath.Round(group.Sum(p => p.Amount)),
                [.. draws.Select(pair => new Draw(pair.Key, SplitMath.Round(pair.Value)))],
                Out(group.Key, outgoing)));
        }

        return moves;
    }

    /// <summary>
    /// Изходният код се записва само когато казва нещо ново.
    ///
    /// Общ код, съвпаднал с приетия, е същият код — записан втори път, той би
    /// изглеждал като нарочно решение там, където няма такова.
    /// </summary>
    private static string Out(string ewc, string chosen) =>
        chosen.Length == 0 || string.Equals(chosen, ewc, StringComparison.Ordinal)
            ? string.Empty
            : chosen;
}
