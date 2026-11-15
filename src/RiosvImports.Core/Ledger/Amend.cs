using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>Едно движение, каквото ще бъде записано при поправката — още без номер.</summary>
public sealed record Planned(
    MoveKind Kind, DateOnly Date, string Ewc, decimal Amount, IReadOnlyList<Draw> Draws,
    bool Direct = false, string EwcOut = "");

/// <summary>
/// Какво поправката оставя без достатъчно — кога, колко и кое виси на него.
///
/// <paramref name="Batch"/> е празно, когато недостигът е по кода изобщо.
/// </summary>
public sealed record AmendIssue(
    StoreKind Store, Batch? Batch, string Ewc, DateOnly On, decimal Short, IReadOnlyList<Move> Takers)
{
    public string Text
    {
        get
        {
            var day = On.ToString(ValueConverter.BulgarianDateFormat);
            var amount = SplitMath.Amount(Short);
            var store = Moves.Short(Store);

            var said = Batch is { } batch
                ? Lang.T($"Партида #{batch.Code} ({batch.Ewc}) в „{store}“: на {day} не достигат {amount} т.",
                         $"Batch #{batch.Code} ({batch.Ewc}) in “{store}”: {amount} t short on {day}.")
                : Lang.T($"Код {Ewc} в „{store}“: на {day} не достигат {amount} т.",
                         $"Code {Ewc} in “{store}”: {amount} t short on {day}.");

            if (Takers.Count == 0) return said;

            var taken = string.Join("; ", Takers.Take(3).Select(Amend.Describe));

            return said + Lang.T($" Взето от: {taken}.", $" Taken by: {taken}.");
        }
    }
}

/// <summary>
/// Поправка на вече записано действие — третиране, образуване или предаване.
///
/// Поправката е „махни старото, запиши новото“, но проверена ПРЕДИ записа. Самият
/// прозорец пази новото: той смята склада към новата дата без старото движение и
/// не дава да се вземе повече, отколкото наистина е свободно
/// (<see cref="Capped"/>). Какво би се развалило след това — по-късно движение,
/// което е черпило от старото — казва <see cref="Breaks"/>, със сметката на
/// <see cref="Undo"/>: поправката и изтриването са един и същ въпрос.
/// </summary>
public static class Amend
{
    /// <summary>Складът без поправяните движения — от него прозорецът смята наличното.</summary>
    public static (IReadOnlyList<Move> Moves, IReadOnlyList<LedgerLink> Links) Without(
        IEnumerable<Move> moves, IEnumerable<LedgerLink> links, IReadOnlySet<long> gone) =>
        ([.. moves.Where(m => !gone.Contains(m.Id))], [.. links.Where(l => !gone.Contains(l.MoveId))]);

    /// <summary>Какво поправката би развалила. Празно значи, че може да се запише.</summary>
    public static IReadOnlyList<AmendIssue> Breaks(
        IReadOnlyList<Batch> batches,
        IReadOnlyList<Move> moves,
        IReadOnlyList<LedgerLink> links,
        IReadOnlySet<long> gone,
        IReadOnlyList<Planned> made)
    {
        var known = batches.ToDictionary(b => b.Id);
        var found = Undo.Shortages(moves, links, gone, made);

        // Партидата казва повече от кода: където са и двете, кодът не се повтаря.
        var named = found.Where(s => s.BatchId is not null).ToList();

        return
        [
            .. found
                .Where(s => s.BatchId is not null || !named.Any(n => n.Store == s.Store && n.Ewc == s.Ewc))
                .Select(s => new AmendIssue(
                    s.Store,
                    s.BatchId is { } id ? known.GetValueOrDefault(id) : null,
                    s.Ewc,
                    s.On,
                    s.Short,
                    s.Takers))
                .OrderBy(i => i.On)
                .ThenBy(i => i.Batch?.Id)
        ];
    }

    /// <summary>
    /// Каквото от всеки дял наистина може да се вземе на тази дата.
    ///
    /// Наличното към дата не вижда какво е станало след нея: десет тона на 5-и, от
    /// които на 15-и са взети четири, изглеждат свободни, а свободни са шест.
    /// Прозорец, който предлага десет, кани грешката и я казва чак при записа.
    /// Затова дялът се свива до най-ниското, до което купчината стига след датата.
    ///
    /// Никога под взетото от самото действие (<paramref name="own"/>), докато към
    /// новата дата до купчината е стигнало поне колкото към старата
    /// (<paramref name="since"/>): тогава обстоятелствата са същите и записаното не
    /// бива да олеква. В истинската база образуваното е с грамове над третираното
    /// заради старото закръгляне, а има и партиди, третирани два пъти — на такава
    /// датата не показва наличност изобщо. Без това всяка поправка, дори само на
    /// датата, би изпуснала взетото от тях, а точно такъв запис е най-вероятно да се
    /// поправя. Партида, приета след новата дата, е друго: тя не е изядена от чужди, а
    /// още я няма, и се предлага само стигналото.
    ///
    /// Каквото наистина не достига, казва <see cref="Breaks"/>: тази сметка само
    /// предлага, онази решава.
    /// </summary>
    /// <param name="moves">Складът БЕЗ поправяното действие.</param>
    /// <param name="links">Дяловете му, също без действието.</param>
    public static IReadOnlyList<StockShare> Capped(
        IReadOnlyList<StockShare> shares,
        IReadOnlyList<Move> moves,
        IReadOnlyList<LedgerLink> links,
        StoreKind store,
        DateOnly on,
        IReadOnlyList<StockShare>? own = null,
        DateOnly? since = null)
    {
        var known = moves.ToDictionary(m => m.Id);

        var mine = (own ?? []).ToDictionary(s => Key(store, s.Ewc, s.Batch.Id));

        var steps = links
            .Where(l => known.TryGetValue(l.MoveId, out var move) && move.Touches(store))
            .Select(l => (Move: known[l.MoveId], l.BatchId, l.Amount))
            .ToList();

        Dictionary<(string Ewc, long Batch), decimal> ArrivedBy(DateOnly day) => steps
            .Where(s => s.Move.Date <= day && s.Move.Sign(store) > 0)
            .GroupBy(s => Key(store, s.Move.Ewc, s.BatchId))
            .ToDictionary(pile => pile.Key, pile => pile.Sum(s => s.Amount));

        var arrived = ArrivedBy(on);
        var arrivedThen = ArrivedBy(since ?? on);

        decimal Kept((string, long) key)
        {
            var taken = mine.GetValueOrDefault(key)?.Amount ?? 0m;
            var there = arrived.GetValueOrDefault(key);

            return there >= arrivedThen.GetValueOrDefault(key) ? taken : Math.Min(taken, there);
        }

        var lowest = steps
            .GroupBy(s => Key(store, s.Move.Ewc, s.BatchId))
            .ToDictionary(
                pile => pile.Key,
                pile =>
                {
                    var running = pile.Where(s => s.Move.Date <= on).Sum(s => s.Move.Sign(store) * s.Amount);
                    var least = running;

                    foreach (var day in pile.Where(s => s.Move.Date > on).GroupBy(s => s.Move.Date).OrderBy(d => d.Key))
                    {
                        running += day.Sum(s => s.Move.Sign(store) * s.Amount);
                        least = Math.Min(least, running);
                    }

                    return least;
                });

        var open = shares
            .Select(share =>
            {
                var key = Key(store, share.Ewc, share.Batch.Id);

                var free = Math.Min(share.Amount, lowest.GetValueOrDefault(key, share.Amount));

                return share with { Amount = SplitMath.Round(Math.Max(free, Kept(key))) };
            })
            .Where(share => share.Amount > 0)
            .ToList();

        var seen = open.Select(s => Key(store, s.Ewc, s.Batch.Id)).ToHashSet();

        open.AddRange(mine
            .Where(pair => !seen.Contains(pair.Key) && Kept(pair.Key) > 0)
            .Select(pair => pair.Value with { Amount = SplitMath.Round(Kept(pair.Key)) }));

        return open;
    }

    /// <summary>Колко от предложеното <see cref="Capped"/> е задържало — за да се каже защо.</summary>
    public static decimal Held(IReadOnlyList<StockShare> offered, IReadOnlyList<StockShare> open)
    {
        var left = open.ToDictionary(s => (s.Ewc, s.Batch.Id), s => s.Amount);

        return SplitMath.Round(offered.Sum(s =>
            Math.Max(0m, s.Amount - left.GetValueOrDefault((s.Ewc, s.Batch.Id)))));
    }

    /// <summary>
    /// Какво е взело самото действие от склада, от който вади — като дялове, за да
    /// може прозорецът да го покаже и там, където складът вече не го показва.
    /// </summary>
    public static IReadOnlyList<StockShare> Own(
        IReadOnlyList<Batch> batches, IReadOnlyList<Move> moves, IReadOnlyList<LedgerLink> links, StoreKind store)
    {
        var known = batches.ToDictionary(b => b.Id);
        var mine = moves.ToDictionary(m => m.Id);

        return
        [
            .. links
                .Where(l => mine.ContainsKey(l.MoveId) && known.ContainsKey(l.BatchId))
                .GroupBy(l => Key(store, mine[l.MoveId].Ewc, l.BatchId))
                .Select(g => new StockShare(
                    g.Key.Ewc.Length > 0 ? g.Key.Ewc : ValueConverter.NormalizeCode(known[g.Key.Batch].Ewc),
                    known[g.Key.Batch],
                    SplitMath.Round(g.Sum(l => l.Amount))))
        ];
    }

    /// <summary>Как се чете едно движение в съобщение: „Предаден · 191202 · 10,000 т · 04.09.2026“.</summary>
    public static string Describe(Move move) =>
        $"{Moves.Title(move.Kind)} · {move.Ewc} · {SplitMath.Amount(move.Amount)} " + Lang.T("т", "t") +
        $" · {move.Date.ToString(ValueConverter.BulgarianDateFormat)}";

    /// <summary>
    /// Купчината в този склад. В „Третиран“ кодовете по устройство не си съвпадат и
    /// купчината е самата партида (виж <see cref="Undo.Shortages"/>).
    /// </summary>
    private static (string Ewc, long Batch) Key(StoreKind store, string ewc, long batch) =>
        (store == StoreKind.Treated ? string.Empty : ValueConverter.NormalizeCode(ewc), batch);
}
