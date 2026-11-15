using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Един ред, прочетен отвън — още с чуждите думи.
///
/// Четенето не тълкува. То знае къде в чуждия файл стои какво, и нищо повече:
/// „Приети от контрагенти“ остава такова, каквото е написано, докато човек не
/// каже на кое наше движение отговаря. Иначе всяка чужда програма щеше да иска
/// свое разбиране, скрито в кода ѝ, и разминаването между тях щеше да се вижда
/// чак в баланса.
///
/// <paramref name="In"/> и <paramref name="Out"/> са двете колони с количество.
/// Чуждите дневници почти винаги имат по една за влизащото и за излизащото —
/// кое от двете важи, се решава от вида, а не от колоната.
/// </summary>
public sealed record ImportRow(
    int Line,
    string Kind,
    string Site,
    string Ewc,
    string Name = "",
    DateOnly? Date = null,
    decimal In = 0,
    decimal Out = 0,
    string Eik = "",
    string Partner = "",
    string Reason = "",
    string Note = "",
    bool Valid = true,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>Прочетеният файл: редовете и това, което не се е разчело.</summary>
public sealed record ImportFile(IReadOnlyList<ImportRow> Rows, IReadOnlyList<string> Problems)
{
    /// <summary>Видовете записи, както ги пише файлът — по тях се прави съответствието.</summary>
    public IReadOnlyList<string> Kinds =>
    [
        .. Rows.Select(r => r.Kind).Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.CurrentCulture)
    ];

    /// <summary>Площадките, както ги пише файлът — обикновено имена, не номера.</summary>
    public IReadOnlyList<string> Sites =>
    [
        .. Rows.Select(r => r.Site).Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCulture)
    ];

    /// <summary>Колко реда носи всеки вид — казва се преди внасянето.</summary>
    public IReadOnlyDictionary<string, int> Counts =>
        Rows.Where(r => r.Kind.Length > 0)
            .GroupBy(r => r.Kind, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Как чуждите думи стават наши движения.
///
/// Съответствието се прави от човека, а не се гадае по имена. Една и съща дума
/// значи различни неща в различни програми, а сбъркано съответствие се вижда
/// чак когато складът излезе на минус хиляда тона.
/// </summary>
public sealed record ImportPlan(
    IReadOnlyDictionary<string, MoveKind> Kinds,
    IReadOnlyDictionary<string, string> Sites,
    IReadOnlySet<string> Direct)
{
    public static ImportPlan Empty => new(
        new Dictionary<string, MoveKind>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Разбрано движение, готово за записване.</summary>
public sealed record ImportedMove(
    int Line,
    MoveKind Kind,
    DateOnly Date,
    string Ewc,
    decimal Amount,
    string Stagenum,
    string Eik,
    string Partner,
    string Document,
    string Note,
    bool Direct,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>Каквото е разбрано и каквото не е.</summary>
public sealed record ImportOutcome(IReadOnlyList<ImportedMove> Moves, IReadOnlyList<string> Problems)
{
    public decimal Total => Moves.Sum(m => m.Amount);

    /// <summary>Какво прави внесеното с всеки склад — показва се ПРЕДИ записа.</summary>
    public Dictionary<StoreKind, decimal> Effect()
    {
        // Пълното име, защото свойството „Moves“ засенчва класа със същото име.
        var effect = Ledger.Moves.Stores.ToDictionary(store => store, _ => 0m);

        foreach (var move in Moves)
            foreach (var store in Ledger.Moves.Stores)
                effect[store] += Ledger.Moves.Sign(move.Kind, store, move.Direct) * move.Amount;

        return effect;
    }
}

/// <summary>
/// Превръщането на прочетеното в движения.
///
/// Стои в „Ядро“ и не знае нито за Excel, нито за база: влиза прочетен файл и
/// съответствие, излизат движения и списък с това, което не е минало. Затова
/// правилата се проверяват с тест, без нито един файл.
/// </summary>
public static class LedgerImport
{
    /// <summary>
    /// Прилага съответствието.
    ///
    /// Ред, който не мине, НЕ спира внасянето — той се описва и се пропуска.
    /// Файл от чужда програма винаги носи по няколко реда, които никой не е
    /// попълнил докрай, а отказ заради тях значи да не се внесе нищо.
    /// </summary>
    public static ImportOutcome Apply(ImportFile file, ImportPlan plan)
    {
        var moves = new List<ImportedMove>();
        var problems = new List<string>(file.Problems);

        foreach (var row in file.Rows)
        {
            if (!row.Valid)
            {
                problems.Add(Say(row.Line, Lang.T("редът е отбелязан като невалиден",
                                                  "the row is marked invalid")));
                continue;
            }

            if (!plan.Kinds.TryGetValue(row.Kind, out var kind))
            {
                problems.Add(Say(row.Line, Lang.T($"видът „{row.Kind}“ няма съответствие",
                                                  $"“{row.Kind}” has no mapping")));
                continue;
            }

            if (row.Date is not { } date)
            {
                problems.Add(Say(row.Line, Lang.T("датата не се чете", "the date cannot be read")));
                continue;
            }

            var code = ValueConverter.NormalizeCode(row.Ewc);

            if (code.Length == 0)
            {
                problems.Add(Say(row.Line, Lang.T("липсва код на отпадъка", "the waste code is missing")));
                continue;
            }

            var amount = Amount(row, kind);

            if (amount <= 0)
            {
                problems.Add(Say(row.Line, Lang.T("количеството е нула или липсва",
                                                  "the quantity is zero or missing")));
                continue;
            }

            var direct = Moves.CanBeDirect(kind) && plan.Direct.Contains(row.Kind);

            moves.Add(new ImportedMove(
                row.Line,
                kind,
                date,
                code,
                amount,
                Stage(plan, row.Site),
                row.Eik.Trim(),
                row.Partner.Trim(),
                row.Reason.Trim(),
                row.Note.Trim(),
                direct,
                row.Fields ?? new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        return new ImportOutcome(moves, problems);
    }

    /// <summary>
    /// Кое от двете количества важи.
    ///
    /// Посоката решава, не колоната: влизащите видове четат влизащото,
    /// излизащите — излизащото. В истински дневник третирането стои с
    /// количество в колоната за предадено, защото и тя изважда от склада — и
    /// точно затова правилото е по знак, а не по име на колона.
    ///
    /// Ако избраната колона е празна, се пробва другата: чуждият файл невинаги
    /// е бил писан с това правило наум.
    /// </summary>
    private static decimal Amount(ImportRow row, MoveKind kind)
    {
        var incoming = kind is MoveKind.Received or MoveKind.Generated;
        var (first, second) = incoming ? (row.In, row.Out) : (row.Out, row.In);

        return SplitMath.Round(first != 0 ? first : second);
    }

    /// <summary>Площадката по съответствие; непосочената е първата.</summary>
    private static string Stage(ImportPlan plan, string site)
    {
        if (site.Length > 0 && plan.Sites.TryGetValue(site, out var stage) && stage.Trim().Length > 0)
            return stage.Trim();

        return "1";
    }

    private static string Say(int line, string text) =>
        Lang.T($"Ред {line}: {text}.", $"Row {line}: {text}.");
}
