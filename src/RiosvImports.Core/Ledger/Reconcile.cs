using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>Какво може да не е наред със склада.</summary>
public enum LedgerProblem
{
    /// <summary>Наличността пада под нула — движение, което складът не покрива.</summary>
    Negative,

    /// <summary>Третираното и образуваното за месеца не се схождат.</summary>
    Unbalanced,

    /// <summary>Движение, чиито дялове по партиди не покриват количеството.</summary>
    Unallocated,

    /// <summary>Движение преди месеца, от който се води складът.</summary>
    BeforeStart,

    /// <summary>Третирано, което още не е обявено като образуван отпадък.</summary>
    Waiting,

    /// <summary>Образувано, което сочи партида, в която нищо не чака.</summary>
    Unbacked
}

/// <summary>
/// Едно намерено несъответствие.
///
/// Носи число и дата, а не само текст: страницата подрежда по тежест и води до
/// самото движение, а тестът сверява числото, вместо да сравнява изречения.
/// </summary>
/// <remarks>
/// <paramref name="Recycled"/> е рециклираното за месеца — носи се само от
/// разликата в масата, за да я назове вярно: при площадка, която рециклира,
/// третираното се сверява с образуваното И рециклираното, и съобщение, което
/// споменава само първото, би пратило човека да търси разлика, каквато няма.
/// </remarks>
public sealed record LedgerIssue(
    LedgerProblem Kind,
    string Ewc,
    decimal Amount,
    DateOnly? On = null,
    string Detail = "",
    decimal Recycled = 0m)
{
    /// <summary>
    /// Дали пречи на затварянето на месеца.
    ///
    /// Отрицателната наличност и движението преди началото са невъзможни — те
    /// значат, че някъде липсва запис. Разминаването в масата и непълното
    /// разпределение са възможни и чести: при третиране има загуби, а човек
    /// понякога знае произхода едва после. Те се показват и се приемат
    /// нарочно, но не спират.
    /// </summary>
    public bool Blocks =>
        Kind is LedgerProblem.Negative or LedgerProblem.BeforeStart
             or LedgerProblem.Waiting or LedgerProblem.Unbacked;

    public string KindText => Kind switch
    {
        LedgerProblem.Negative => Lang.T("Недостиг", "Shortfall"),
        LedgerProblem.Unbalanced => Lang.T("Разлика в масата", "Mass difference"),
        LedgerProblem.Unallocated => Lang.T("Неразпределено", "Unallocated"),
        LedgerProblem.Waiting => Lang.T("Чака в „Третиран“", "Waiting in “Treated”"),
        LedgerProblem.Unbacked => Lang.T("Произход без основание", "Origin without ground"),
        _ => Lang.T("Преди началото", "Before the start")
    };

    public string Text => Kind switch
    {
        LedgerProblem.Negative => Lang.T(
            $"Код {Ewc}: на {Day} не достигат {SplitMath.Amount(Amount)} т.",
            $"Code {Ewc}: {SplitMath.Amount(Amount)} t short on {Day}."),

        LedgerProblem.Unbalanced => (Amount > 0, Recycled > 0) switch
        {
            (true, false) => Lang.T(
                $"Третирани са с {SplitMath.Amount(Amount)} т повече, отколкото са образувани.",
                $"{SplitMath.Amount(Amount)} t more was treated than was generated."),
            (true, true) => Lang.T(
                $"Третирани са с {SplitMath.Amount(Amount)} т повече, отколкото са образувани и рециклирани.",
                $"{SplitMath.Amount(Amount)} t more was treated than was generated and recycled."),
            (false, false) => Lang.T(
                $"Образувани са с {SplitMath.Amount(-Amount)} т повече, отколкото са третирани.",
                $"{SplitMath.Amount(-Amount)} t more was generated than was treated."),
            _ => Lang.T(
                $"Образувани и рециклирани са с {SplitMath.Amount(-Amount)} т повече, отколкото са третирани.",
                $"{SplitMath.Amount(-Amount)} t more was generated and recycled than was treated.")
        },

        LedgerProblem.Unallocated => Lang.T(
            $"Код {Ewc} на {Day}: {SplitMath.Amount(Amount)} т без посочена партида.",
            $"Code {Ewc} on {Day}: {SplitMath.Amount(Amount)} t with no batch named."),

        LedgerProblem.Waiting => Lang.T(
            $"В склад „Третиран“ чакат {SplitMath.Amount(Amount)} т към {Day}. " +
            "Обявете какво е образувано или рециклирано от тях.",
            $"{SplitMath.Amount(Amount)} t are waiting in the “Treated” store as of {Day}. " +
            "State what was generated or recycled from them."),

        // Видът стои в Detail („Образуван“ или „Рециклиран“): и двете изчерпват
        // „Третиран“ и и двете могат да посочат празна партида.
        LedgerProblem.Unbacked => Lang.T(
            $"Код {Ewc} на {Day}: {SplitMath.Amount(Amount)} т {Drawn("bg")} отпадък сочат партиди, " +
            "в които нищо не чака. Това количество не изчерпва склада и третираното зад него " +
            "може да бъде обявено втори път.",
            $"Code {Ewc} on {Day}: {SplitMath.Amount(Amount)} t of {Drawn("en")} waste point at batches " +
            "with nothing waiting. It draws nothing down, so the treatment behind it can be " +
            "declared a second time."),

        _ => Lang.T(
            $"Код {Ewc}: движение на {Day} е преди месеца, от който се води складът.",
            $"Code {Ewc}: a movement on {Day} precedes the month the store starts from.")
    };

    private string Day => On?.ToString(ValueConverter.BulgarianDateFormat) ?? "—";

    /// <summary>„образуван“ или „рециклиран“ — според движението зад бележката.</summary>
    private string Drawn(string language)
    {
        var recycled = string.Equals(Detail, Moves.Title(MoveKind.Recycled), StringComparison.Ordinal);

        return language == "bg"
            ? recycled ? "рециклиран" : "образуван"
            : recycled ? "recycled" : "generated";
    }
}

/// <summary>
/// Контролите на склада.
///
/// Затварянето на месеца пита оттук. Правилото връща намереното, а не решение —
/// кое спира и кое само се показва, казва <see cref="LedgerIssue.Blocks"/>, а
/// какво да се направи по въпроса решава човекът.
///
/// Както и другаде тук, движенията идват вече пресяти по фирма и площадка.
/// </summary>
public static class Reconcile
{
    /// <summary>
    /// Къде наличността пада под нула.
    ///
    /// Обхожда се по дни, а не се гледа само салдото накрая: месец, който
    /// започва и свършва на нула, може по средата да е бил на минус двайсет
    /// тона, и точно там е сгрешеното движение.
    ///
    /// В един и същ ден влизащото се брои преди излизащото — иначе приемане и
    /// третиране на една дата биха се разминали и денят, в който отпадъкът е
    /// дошъл, щеше да е денят, в който не може да се пипне.
    ///
    /// По един ред на код: съобщава се първото пропадане, защото всяко
    /// следващо е негово следствие.
    /// </summary>
    public static IReadOnlyList<LedgerIssue> Negative(IEnumerable<Move> moves, StoreKind store)
    {
        var issues = new List<LedgerIssue>();

        var byCode = moves
            .Where(m => m.Touches(store))
            .Select(m => (Move: m, Code: ValueConverter.NormalizeCode(m.Ewc)))
            .Where(pair => pair.Code.Length > 0)
            .GroupBy(pair => pair.Code, StringComparer.Ordinal);

        foreach (var group in byCode.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            decimal running = 0;

            foreach (var pair in group
                         .OrderBy(pair => pair.Move.Date)
                         .ThenByDescending(pair => pair.Move.Sign(store))
                         .ThenBy(pair => pair.Move.Id))
            {
                var move = pair.Move;

                running = SplitMath.Round(running + move.Signed(store));

                if (running >= 0) continue;

                issues.Add(new LedgerIssue(LedgerProblem.Negative, group.Key, -running, move.Date));
                break;
            }
        }

        return issues;
    }

    /// <summary>
    /// Разликата между третираното и образуваното за месеца.
    ///
    /// Сборно, не по кодове: третирането сменя кода — влиза кабел, излизат мед
    /// и пластмаса — така че по код двете страни не си съответстват по
    /// устройство. Сравнява се масата, защото тя е онова, което се запазва.
    ///
    /// Разликата не е грешка сама по себе си: при рязане и промиване има
    /// загуби. Но мълчаливата разлика се трупа, затова се показва.
    ///
    /// Рециклираното е излязло от третирането като образуваното и се брои
    /// заедно с него — иначе всеки рециклиран тон би стоял тук като загуба.
    /// </summary>
    public static IReadOnlyList<LedgerIssue> Unbalanced(
        IEnumerable<Move> moves, LedgerPeriod period, decimal tolerance = 0m)
    {
        var totals = StockMath.Totals(moves, period);
        var recycled = totals[MoveKind.Recycled];
        var difference = SplitMath.Round(totals[MoveKind.Treated] - totals[MoveKind.Generated] - recycled);

        return Math.Abs(difference) <= Math.Abs(tolerance)
            ? []
            : [new LedgerIssue(LedgerProblem.Unbalanced, string.Empty, difference, period.Last, Recycled: recycled)];
    }

    /// <summary>
    /// Движения, чиито дялове по партиди не покриват количеството.
    ///
    /// Без пълно покритие произходът е половин отговор: „три от петте тона са
    /// на Метал ООД, а другите два — не се знае“. Не спира месеца, защото
    /// понякога наистина не се знае, но стои на видно място.
    /// </summary>
    public static IReadOnlyList<LedgerIssue> Unallocated(
        IEnumerable<Move> moves, IEnumerable<LedgerLink> links, LedgerPeriod? period = null)
    {
        var covered = new Dictionary<long, decimal>();

        foreach (var link in links)
            covered[link.MoveId] = covered.GetValueOrDefault(link.MoveId) + link.Amount;

        var issues = new List<LedgerIssue>();

        foreach (var move in moves.OrderBy(m => m.Date).ThenBy(m => m.Id))
        {
            if (period is { } month && !month.Covers(move.Date)) continue;

            var missing = SplitMath.Round(move.Amount - covered.GetValueOrDefault(move.Id));
            if (missing <= 0) continue;

            issues.Add(new LedgerIssue(
                LedgerProblem.Unallocated,
                ValueConverter.NormalizeCode(move.Ewc),
                missing,
                move.Date,
                Moves.Title(move.Kind)));
        }

        return issues;
    }

    /// <summary>
    /// Движения преди месеца, от който се води складът.
    ///
    /// Началните количества са изключението — те съществуват точно за да стоят
    /// пред първия месец и затова не се броят.
    /// </summary>
    public static IReadOnlyList<LedgerIssue> BeforeStart(IEnumerable<Move> moves, LedgerPeriod start) =>
    [
        .. moves
            .Where(m => !m.Opening && m.Period < start)
            .OrderBy(m => m.Date)
            .ThenBy(m => m.Id)
            .Select(m => new LedgerIssue(
                LedgerProblem.BeforeStart,
                ValueConverter.NormalizeCode(m.Ewc),
                m.Amount,
                m.Date,
                Moves.Title(m.Kind)))
    ];

    /// <summary>
    /// Какво е останало да чака в склад „Третиран“ в края на месеца.
    ///
    /// Това е контролата, заради която третираното изобщо стана склад: докато
    /// не се обяви какво е образувано от него, месецът не е приключен. Третирано
    /// количество без образувано отсреща значи, че отпадъкът е някъде на
    /// площадката, а отчетът твърди, че е минал през нея.
    ///
    /// Гледа се наличността в КРАЯ на месеца, а не движението в него: третирано
    /// на 30 септември и образувано на 2 октомври е наред за октомври и
    /// неприключено за септември — точно както е било.
    /// </summary>
    public static IReadOnlyList<LedgerIssue> Waiting(
        IEnumerable<Batch> batches,
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        LedgerPeriod period,
        decimal tolerance = 0m)
    {
        var waiting = StockMath.Waiting(batches, moves, links, period.Last);

        return waiting.Total <= Math.Abs(tolerance)
            ? []
            : [new LedgerIssue(LedgerProblem.Waiting, string.Empty, waiting.Total, period.Last)];
    }

    /// <summary>
    /// Образуване, което е обявило произход без основание.
    ///
    /// Движението сочи партида — „това излезе от нейното третиране“ — а в нея
    /// нищо не чака. Такъв дял не изчерпва склада: третираното остава да чака и
    /// може да бъде обявено втори път, а масата се раздува, без нищо да е
    /// влязло. Видяно веднъж в истинска база като 15,4 тона образувани над
    /// третираното — и намерено чак след като някой седна да брои.
    ///
    /// Спира месеца. Другите бележки описват състояние, което може и да е вярно:
    /// при третиране има загуби, а произходът понякога наистина не се знае. Тази
    /// описва запис, който твърди нещо невъзможно.
    ///
    /// Собственият отпадък не се засяга. Площадка, която образува, без да
    /// третира, не сочи ничия партида — движението ѝ няма дялове и дотук не
    /// стига (<see cref="StockMath.UnbackedDraw"/>).
    /// </summary>
    public static IReadOnlyList<LedgerIssue> Unbacked(
        IEnumerable<Batch> batches,
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        LedgerPeriod period)
    {
        var all = moves as IReadOnlyList<Move> ?? [.. moves];
        var stock = StockMath.Waiting(batches, all, links, period.Last);

        if (stock.Unbacked.Count == 0) return [];

        var seen = new Dictionary<long, Move>();

        foreach (var move in all) seen[move.Id] = move;

        var issues = new List<LedgerIssue>();

        // По ДВИЖЕНИЕ, не по партида: зад едно образуване стоят десетки дяла и
        // списък от тях не казва нищо повече от сбора им, а заема целия екран.
        foreach (var group in stock.Unbacked.GroupBy(u => u.MoveId).OrderBy(g => g.Key))
        {
            if (!seen.TryGetValue(group.Key, out var move)) continue;
            if (!period.Covers(move.Date)) continue;

            issues.Add(new LedgerIssue(
                LedgerProblem.Unbacked,
                ValueConverter.NormalizeCode(move.Ewc),
                SplitMath.Round(group.Sum(u => u.Amount)),
                move.Date,
                Moves.Title(move.Kind)));
        }

        return issues;
    }

    /// <summary>
    /// Всичко наведнъж — това пита затварянето на месеца.
    ///
    /// Подредено по тежест: спиращото отгоре, за да не се чете списък от
    /// бележки, преди да се стигне до онова, което наистина пречи.
    /// </summary>
    public static IReadOnlyList<LedgerIssue> Check(
        IEnumerable<Batch> batches,
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        LedgerPeriod period,
        LedgerPeriod? start = null,
        decimal tolerance = 0m)
    {
        var all = moves as IReadOnlyList<Move> ?? [.. moves];

        var issues = new List<LedgerIssue>();

        issues.AddRange(Negative(all, StoreKind.Received));
        issues.AddRange(Negative(all, StoreKind.Formed));

        if (start is { } from) issues.AddRange(BeforeStart(all, from));

        // Преди онова, което сравнява масите: разминаването в масата е
        // СЛЕДСТВИЕ от произход без основание и без него се чете като загуба.
        issues.AddRange(Unbacked(batches, all, links, period));

        // Двете казват едно и също с различна точност: щом в склада чака
        // количество, то е по-силното твърдение и разликата в масата е
        // следствие от него. Бележка за нещо, което вече спира месеца, само
        // би удвоила списъка.
        var waiting = Waiting(batches, all, links, period, tolerance);

        if (waiting.Count > 0) issues.AddRange(waiting);
        else issues.AddRange(Unbalanced(all, period, tolerance));

        issues.AddRange(Unallocated(all, links, period));

        return [.. issues.OrderByDescending(i => i.Blocks).ThenBy(i => i.On)];
    }

    /// <summary>Дали месецът може да се затвори — тоест няма спиращо.</summary>
    public static bool CanClose(IEnumerable<LedgerIssue> issues) => !issues.Any(i => i.Blocks);
}
