using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;
using RiosvImports.Data;

namespace RiosvImports.App.Shell;

/// <summary>
/// Записано действие, отворено наново в прозореца, от който е дошло.
///
/// Носи старите движения и дяловете им: от тях прозорецът нарежда отново
/// същото — дата, полета, партиди, количества, изходен код, рецепта — и от тях
/// знае кои движения да „върне“ в склада, докато човек поправя. Иначе
/// собственото количество на действието би стояло като вече взето и същото
/// третиране не би могло да се премести дори с един ден.
///
/// <paramref name="Scope"/>, <paramref name="Target"/> и <paramref name="Split"/> са
/// онова, което движенията не помнят: какво е предлагал прозорецът, какво е
/// показал кантарът и дали е имало отделен ред за всяка партида. Без тях поправката
/// отваря записаното, а не обстоятелствата: грешно взета партида не може да се
/// смени с вярната, а редовете в отчета тихо се събират в един.
///
/// <paramref name="Takers"/> са движенията, които вече висят на действието, а
/// <paramref name="Held"/> — колко не се предлага, защото е взето след тази дата. И
/// двете се казват още при отваряне, а не чак при записа.
///
/// <paramref name="Own"/> е взетото от самото действие: то остава предложено, каквото
/// и да показва складът (виж <see cref="Amend.Capped"/>).
/// </summary>
internal sealed record AmendSeed(
    IReadOnlyList<ActionEntry> Entries,
    IReadOnlyList<Move> Moves,
    IReadOnlyList<LedgerLink> Links,
    IReadOnlyDictionary<string, string> Fields,
    string Note,
    string Contract = "",
    IReadOnlySet<(string Ewc, long Batch)>? Scope = null,
    decimal Target = 0m,
    bool Split = false,
    IReadOnlyList<Move>? Takers = null,
    decimal Held = 0m,
    IReadOnlyList<StockShare>? Own = null)
{
    /// <summary>Движенията, които поправката заменя — прозорецът смята склада без тях.</summary>
    public IReadOnlySet<long> Gone => Moves.Select(m => m.Id).ToHashSet();

    public MoveKind Kind => Moves[0].Kind;

    /// <summary>Датата на действието — от нея тръгва поправката.</summary>
    public DateOnly Date => Moves[0].Date;

    public string EwcOut => Moves[0].EwcOut;

    public bool Direct => Moves[0].Direct;

    /// <summary>От кой склад вади действието — там се мери и свободното.</summary>
    public StoreKind Store => RiosvImports.Core.Ledger.Moves.From(Kind, Direct) ?? StoreKind.Formed;

    /// <summary>Белегът на действието — поправеното си остава същото действие.</summary>
    public string Action => Entries[0].Action;

    /// <summary>Кога е въведено — поправката не го мести в дневника.</summary>
    public DateTime Since => Entries[0].CreatedUtc;

    /// <summary>
    /// Какво е било, преди да се пипне. Поправяно два пъти, то си остава
    /// първоначалното: другото само се чете в съседния ред на дневника.
    /// </summary>
    public string Was
    {
        get
        {
            if (Entries.Select(e => e.Was).FirstOrDefault(w => w.Length > 0) is { } first) return first;

            var amount = SplitMath.Amount(Moves.Sum(m => m.Amount));
            var day = Date.ToString(ValueConverter.BulgarianDateFormat);

            return Moves.Count == 1
                ? Lang.T($"{amount} т на {day}", $"{amount} t on {day}")
                : Lang.T($"{amount} т на {day}, {Moves.Count} реда", $"{amount} t on {day}, {Moves.Count} rows");
        }
    }

    /// <summary>Какво се поправя — първото изречение на лентата.</summary>
    private string Doing
    {
        get
        {
            var day = Date.ToString(ValueConverter.BulgarianDateFormat);

            return Kind switch
            {
                MoveKind.Treated => Lang.T($"Поправя се третиране от {day}.", $"Correcting the treatment of {day}."),
                MoveKind.Generated => Lang.T($"Поправя се образуване от {day}.", $"Correcting the generation of {day}."),
                MoveKind.Recycled => Lang.T($"Поправя се рециклиране от {day}.", $"Correcting the recycling of {day}."),
                _ => Lang.T($"Поправя се предаване от {day}.", $"Correcting the hand-over of {day}.")
            };
        }
    }

    /// <summary>
    /// Какво трябва да се знае, преди да се пипне нещо: какво се поправя, какво виси на
    /// него и защо наличното е такова.
    /// </summary>
    /// <param name="held">Колко не се предлага на избраната дата.</param>
    public string Notice(decimal held)
    {
        var said = new List<string>
        {
            Doing,
            Lang.T("Количеството му е върнато в склада, докато се поправя; записът заменя старото.",
                   "Its quantity is back in stock while it is corrected; saving replaces the old one.")
        };

        if (Takers is { Count: > 0 } takers)
        {
            said.Add(Lang.T(
                $"От това действие вече е взето: {Amend.Describe(takers[0])}" +
                (takers.Count > 1 ? $" и още {takers.Count - 1}" : string.Empty) +
                ". Намаление или по-късна дата може да не минат.",
                $"This action has already been drawn on: {Amend.Describe(takers[0])}" +
                (takers.Count > 1 ? $" and {takers.Count - 1} more" : string.Empty) +
                ". A reduction or a later date may not go through."));
        }

        if (held > 0)
        {
            said.Add(Lang.T(
                $"Наличното е показано без {SplitMath.Amount(held)} т, взети след тази дата.",
                $"The stock is shown without {SplitMath.Amount(held)} t taken after this date."));
        }

        return string.Join(" ", said);
    }

    /// <summary>Какво наистина е свободно на тази дата (<see cref="Amend.Capped"/>).</summary>
    public IReadOnlyList<StockShare> Capped(
        IReadOnlyList<StockShare> shares, IReadOnlyList<Move> moves, IReadOnlyList<LedgerLink> links, DateOnly on) =>
        Amend.Capped(shares, moves, links, Store, on, Own, Date);

    /// <summary>
    /// Взетото от партидите на една купчина — по кода на движението.
    ///
    /// При третиране и предаване движението носи кода на купчината, от която
    /// е взето; така един и същ номер партида под два кода не се смесва.
    /// </summary>
    public Dictionary<long, decimal> Taken(TreatPile pile)
    {
        var mine = Moves
            .Where(m => string.Equals(ValueConverter.NormalizeCode(m.Ewc), pile.Ewc, StringComparison.Ordinal))
            .Select(m => m.Id)
            .ToHashSet();

        return Sum(Links.Where(l => mine.Contains(l.MoveId)), pile);
    }

    /// <summary>
    /// Взетото от партидите на една купчина — по самите партиди.
    ///
    /// При образуване движението носи кода, в който Е ИЗЛЯЗЛО, а не този, от
    /// който е взето; купчината се познава по партидите ѝ.
    /// </summary>
    public Dictionary<long, decimal> Drawn(TreatPile pile) => Sum(Links, pile);

    /// <summary>В какво е излязла една купчина при образуването — по изходен код.</summary>
    public List<GenPart> Recipe(TreatPile pile)
    {
        var batches = pile.Shares.Select(s => s.Batch.Id).ToHashSet();
        var codes = Moves.ToDictionary(m => m.Id, m => ValueConverter.NormalizeCode(m.Ewc));

        return
        [
            .. Links
                .Where(l => batches.Contains(l.BatchId) && codes.ContainsKey(l.MoveId))
                .GroupBy(l => codes[l.MoveId])
                .Select(g => new GenPart(g.Key, SplitMath.Round(g.Sum(l => l.Amount))))
                .Where(p => p.Amount > 0)
        ];
    }

    private static Dictionary<long, decimal> Sum(IEnumerable<LedgerLink> links, TreatPile pile)
    {
        var batches = pile.Shares.Select(s => s.Batch.Id).ToHashSet();

        return links
            .Where(l => batches.Contains(l.BatchId))
            .GroupBy(l => l.BatchId)
            .ToDictionary(g => g.Key, g => SplitMath.Round(g.Sum(l => l.Amount)));
    }
}
