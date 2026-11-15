using System.Reflection;
using RiosvImports.App.Shell;
using RiosvImports.Core;
using RiosvImports.Core.Ledger;
using RiosvImports.Data;

/// <summary>
/// Една фирма с празен склад — за проверките на поправката.
///
/// Записва и поправя по пътя на програмата: през прозореца, през истинското копче и
/// през същата замяна, през която минава StockPage.AmendEntry. Иначе проверката
/// би доказала, че работи нещо, което никой не вика.
/// </summary>
internal sealed class AmendWorld : IDisposable
{
    public static readonly DateOnly End = new(2026, 9, 30);

    private static readonly Dictionary<string, string> MadeFields = new(StringComparer.Ordinal);

    private readonly ImportService _service;

    public AmendWorld(ImportService service, string folder)
    {
        _service = service;

        DbPath = Path.Combine(folder, $"amend_{Guid.NewGuid():N}.db");
        Db = Workspace.Open(DbPath);
        Co = Db.Companies.Add("203344556", "Метал ООД");
        Page = new StockPage(service, Db);
        Page.UseDate(End);
    }

    public string DbPath { get; }

    public Workspace Db { get; }

    public long Co { get; }

    public StockPage Page { get; }

    public static DateOnly Day(int day) => new(2026, 9, day);

    public void Dispose()
    {
        Page.Dispose();
        Db.Dispose();
    }

    // ------------------------------------------------------------- складът

    public IReadOnlyList<Move> Moves => Db.Ledger.Read(Co, "1");

    public IReadOnlyList<LedgerLink> Links => Db.Ledger.Links(Co, "1");

    public IReadOnlyList<StockShare> Stock(StoreKind store, DateOnly? on = null) =>
        StockMath.Shares(Db.Batches.List(Co, "1"), Moves, Links, store, on ?? End);

    public StockMath.TreatedStock Waiting() => StockMath.Waiting(Db.Batches.List(Co, "1"), Moves, Links, End);

    public decimal Left(StoreKind store, long batch) =>
        Stock(store).Where(s => s.Batch.Id == batch).Sum(s => s.Amount);

    /// <summary>Какво точно има в базата — движенията с номерата им, дневникът, прозорците.</summary>
    public string Print()
    {
        var links = Links;

        string Row(Move m)
        {
            var draws = string.Join(",", links.Where(l => l.MoveId == m.Id).OrderBy(l => l.BatchId)
                .Select(l => $"{l.BatchId}:{l.Amount:0.######}"));

            var fields = string.Join(",", Db.Ledger.Fields(m.Id).OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"{f.Key}={f.Value}"));

            return $"{m.Id}|{m.Kind}|{m.Date:yyyy-MM-dd}|{m.Ewc}|{m.Amount:0.######}|{m.Direct}|{m.EwcOut}|" +
                   $"{Db.Ledger.Note(m.Id)}|{Db.Ledger.Contract(m.Id)}|{draws}|{fields}";
        }

        var diary = Db.Actions.List(Co, "1");

        return string.Join("\n",
            Moves.OrderBy(m => m.Id).Select(Row)
                .Concat(diary.Select(e => $"{e.Id}|{e.MoveId}|{e.Action}|{e.Trashed}|{e.CreatedUtc:O}|{e.Was}"))
                .Concat(diary.Select(e => e.Action).Distinct().Select(a => Db.Actions.Window(a)?.Write() ?? "-")));
    }

    // ------------------------------------------------------------- запис

    public long Receive(int day, decimal tons, string ewc = "160216") =>
        Db.Ledger.Draws(Db.Writer.Write(Co, new MoveRequest(
            MoveKind.Received, Day(day), ewc, tons, Eik: "204455667", Partner: "Скрап ЕООД")))[0].BatchId;

    /// <summary>Третиране през прозореца, както човек го прави.</summary>
    public void Treat(DateOnly on, Action<TreatDialog> setup, IReadOnlyList<StockShare>? shares = null)
    {
        using var dialog = new TreatDialog(_service, Db, Co, "1", shares ?? Stock(StoreKind.Received), on);

        dialog.UseOperation(0);
        setup(dialog);

        dialog.Write = (moves, date, fields, note) => Page.Apply(moves, date, fields, note, dialog.Window);
        dialog.TreatAndContinue();
    }

    /// <summary>Предаване през прозореца, както човек го прави.</summary>
    public void Hand(
        DateOnly on, Action<SubmitDialog> setup, IReadOnlyList<StockShare>? shares = null, bool direct = false)
    {
        var store = direct ? StoreKind.Received : StoreKind.Formed;

        using var dialog = new SubmitDialog(_service, Db, Co, "1", shares ?? Stock(store), on, direct: direct);

        dialog.UseField("type_receiver_code", "01");
        dialog.UseField("name_receiver", "Претопи ООД");
        dialog.UseField("pin_receiver", "205566778");
        dialog.UseOperation(0);
        dialog.UseField("is_for_reuse", "0");

        setup(dialog);

        dialog.Write = (moves, date, fields, note) =>
            Page.ApplySubmitted(moves, date, fields, note, direct, dialog.Contract, dialog.Window);

        dialog.HandAndContinue();
    }

    /// <summary>Образува чакащото в един код, с прозорец за обхвата. Без количество — цялото.</summary>
    public void Generate(
        DateOnly on, string code = "191202", bool recycled = false, bool perPile = false, decimal? take = null)
    {
        var shares = Waiting().Shares;
        var orders = Generation.Piles(shares)
            .Select(pile =>
            {
                var amount = take ?? pile.Shares.Sum(s => s.Amount);

                return new GenOrder(
                    pile,
                    [.. pile.Shares.Select(s => new TreatPick(s, Math.Min(s.Amount, amount)))],
                    [new GenPart(code, amount)]);
            })
            .ToList();

        var window = new ActionWindow([.. shares.Select(s => new WindowShare(s.Ewc, s.Batch.Id))], Split: perPile);

        var plan = Generation.Plan(orders, perPile);

        if (recycled) Page.ApplyRecycled(plan, on, MadeFields, "", window);
        else Page.ApplyGenerated(plan, on, MadeFields, "", window);
    }

    // ------------------------------------------------------------- поправка

    /// <summary>Редът от дневника за този вид — най-новият, ако не е казано друго.</summary>
    public ActionEntry Entry(MoveKind kind, int index = 0) =>
        Db.Actions.List(Co, "1").Where(e => e.Kind == kind).ElementAt(index);

    /// <summary>Ред отпреди дневникът да пази каквото и да е: само движението.</summary>
    public ActionEntry Bare(Move move) =>
        new(0, Co, "1", move.Kind, move.Id, move.Date, move.Period, move.Ewc, move.Amount, "", DateTime.UtcNow, true);

    public (TreatDialog Fix, AmendSeed Seed) FixTreat(ActionEntry entry)
    {
        var seed = Open(entry, out var shares);
        var fix = new TreatDialog(_service, Db, Co, "1", shares, seed.Date, seed);

        fix.Guard = (_, made) => Page.Check(seed, made);

        return (fix, seed);
    }

    public (GenerateDialog Fix, AmendSeed Seed) FixMade(ActionEntry entry)
    {
        var seed = Open(entry, out var shares);
        var fix = new GenerateDialog(_service, Db, Co, "1", shares, seed.Date, seed, seed.Kind);

        fix.Guard = (_, made) => Page.Check(seed, made);

        return (fix, seed);
    }

    public (SubmitDialog Fix, AmendSeed Seed) FixHand(ActionEntry entry)
    {
        var seed = Open(entry, out var shares);
        var fix = new SubmitDialog(_service, Db, Co, "1", shares, seed.Date, seed.Direct, seed);

        fix.Guard = (_, made) => Page.Check(seed, made);

        return (fix, seed);
    }

    public int Save(TreatDialog fix, AmendSeed seed)
    {
        Press(fix);

        return Page.Replace(seed, () => Page.Apply(fix.Result, fix.Date, fix.Fields, fix.Note, fix.Window));
    }

    public int Save(GenerateDialog fix, AmendSeed seed)
    {
        Press(fix);

        return Page.Replace(seed, () => seed.Kind == MoveKind.Recycled
            ? Page.ApplyRecycled(fix.Result, fix.Date, fix.Fields, fix.Note, fix.Window)
            : Page.ApplyGenerated(fix.Result, fix.Date, fix.Fields, fix.Note, fix.Window));
    }

    public int Save(SubmitDialog fix, AmendSeed seed)
    {
        Press(fix);

        return Page.Replace(seed, () => Page.ApplySubmitted(
            fix.Result, fix.Date, fix.Fields, fix.Note, seed.Direct, fix.Contract, fix.Window));
    }

    private AmendSeed Open(ActionEntry entry, out IReadOnlyList<StockShare> shares) =>
        Page.Prepare(entry, out shares, out var why) ?? throw new InvalidOperationException(why);

    /// <summary>
    /// Истинското копче: оттам идват Result и Fields, преди да започне замяната.
    /// Прозорецът не е показан, затова и питането за променено количество мълчи.
    /// </summary>
    private static void Press(Form dialog) =>
        dialog.GetType().GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
}
