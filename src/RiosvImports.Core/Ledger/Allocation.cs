using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>Един дял от разпределението: толкова от тази партида.</summary>
public sealed record Draw(long BatchId, decimal Amount);

/// <summary>
/// Готовото разпределение.
///
/// Носи и поисканото, и взетото, защото разликата между тях е самият отговор:
/// „складът не стига“ не е изключение, а обичайният случай, когато човек
/// въвежда третиране преди приемането му.
/// </summary>
public sealed record Allocated(IReadOnlyList<Draw> Draws, decimal Requested, decimal Taken)
{
    /// <summary>Колко не е покрито от наличното.</summary>
    public decimal Missing => SplitMath.Round(Requested - Taken);

    /// <summary>Дали наличното покрива поисканото.</summary>
    public bool IsComplete => Missing <= 0;

    /// <summary>Празно разпределение — нищо не е поискано или няма от какво.</summary>
    public static Allocated None => new([], 0m, 0m);
}

/// <summary>Какво не е наред с ръчно поправено разпределение.</summary>
public enum DrawProblem
{
    /// <summary>Посочена е партида, която складът не познава.</summary>
    UnknownBatch,

    /// <summary>Дял нула или отрицателен.</summary>
    NotPositive,

    /// <summary>От партидата се иска повече, отколкото има в нея.</summary>
    OverBatch,

    /// <summary>Партидата е с друг код.</summary>
    WrongCode,

    /// <summary>Сборът на дяловете не е равен на количеството.</summary>
    Mismatch
}

/// <summary>Едно намерено несъответствие в разпределението.</summary>
public sealed record DrawIssue(DrawProblem Kind, long BatchId, decimal Amount, string Batch = "")
{
    /// <summary>Как се чете на екрана.</summary>
    public string Text => Kind switch
    {
        DrawProblem.UnknownBatch =>
            Lang.T($"Непозната партида {Name}.", $"Unknown batch {Name}."),
        DrawProblem.NotPositive =>
            Lang.T($"Дялът на {Name} трябва да е положителен.",
                   $"The share of {Name} must be positive."),
        DrawProblem.OverBatch =>
            Lang.T($"В {Name} има само {SplitMath.Amount(Amount)} т.",
                   $"{Name} holds only {SplitMath.Amount(Amount)} t."),
        DrawProblem.WrongCode =>
            Lang.T($"{Name} е с друг код на отпадъка.", $"{Name} carries a different waste code."),
        _ => Lang.T($"Сборът на дяловете е {SplitMath.Amount(Amount)} т и не отговаря на количеството.",
                    $"The shares add up to {SplitMath.Amount(Amount)} t, which is not the quantity.")
    };

    private string Name => Batch.Length > 0 ? Batch : $"#{BatchId}";
}

/// <summary>
/// Разпределянето по партиди.
///
/// Тук се отговаря на въпроса, заради който изобщо има партиди: като се
/// третират осем тона по код 160216, чии са били те. Отговорът се пресмята в
/// момента на записа и се пази — по-късно, когато същият код е дошъл от петима
/// клиенти на седем дати, той вече не се извежда еднозначно.
///
/// Редът по подразбиране е FIFO: най-старата доставка първа. Това е и
/// счетоводно обичайното, и физически вярното за площадка, на която отпадъкът
/// не се пази вечно. Човек може да го поправи — тогава <see cref="Check"/>
/// казва дали поправката се държи.
/// </summary>
public static class Allocation
{
    /// <summary>
    /// Разпределя количество по наличните партиди, най-старата първа.
    ///
    /// Не спира при недостиг, а връща каквото е успяло: страницата показва
    /// колко липсва и от кога, а решението какво да прави с това е на човека.
    /// Отказ без число не му казва нищо.
    /// </summary>
    public static Allocated Fifo(IEnumerable<BatchStock> stock, decimal amount)
    {
        var wanted = SplitMath.Round(amount);
        var draws = new List<Draw>();

        if (wanted <= 0) return new Allocated(draws, wanted, 0m);

        var left = wanted;

        foreach (var item in stock.Where(s => !s.IsEmpty)
                     .OrderBy(s => s.Batch.ReceivedOn)
                     .ThenBy(s => s.Batch.Id))
        {
            if (left <= 0) break;

            var take = SplitMath.Round(Math.Min(item.Available, left));
            if (take <= 0) continue;

            draws.Add(new Draw(item.Batch.Id, take));
            left = SplitMath.Round(left - take);
        }

        return new Allocated(draws, wanted, SplitMath.Round(wanted - left));
    }

    /// <summary>
    /// Проверява ръчно поправено разпределение срещу наличното.
    ///
    /// Празен списък значи, че разпределението се държи. Правилото връща
    /// причини, не „да/не“ — питането и съобщението са работа на повикващия
    /// (правило 4).
    /// </summary>
    public static IReadOnlyList<DrawIssue> Check(
        IReadOnlyList<Draw> draws,
        IEnumerable<BatchStock> stock,
        decimal amount,
        string ewc = "")
    {
        var available = stock.ToDictionary(s => s.Batch.Id);
        var code = ValueConverter.NormalizeCode(ewc);
        var issues = new List<DrawIssue>();

        // Два дяла към една партида не са грешка, а начин на писане — събират се
        // и чак сборът им се сверява с наличното.
        var wanted = new Dictionary<long, decimal>();

        foreach (var draw in draws)
        {
            if (!available.TryGetValue(draw.BatchId, out var batch))
            {
                issues.Add(new DrawIssue(DrawProblem.UnknownBatch, draw.BatchId, 0m));
                continue;
            }

            if (draw.Amount <= 0)
            {
                issues.Add(new DrawIssue(DrawProblem.NotPositive, draw.BatchId, draw.Amount, batch.Batch.Display));
                continue;
            }

            if (code.Length > 0 &&
                !string.Equals(ValueConverter.NormalizeCode(batch.Batch.Ewc), code, StringComparison.Ordinal))
            {
                issues.Add(new DrawIssue(DrawProblem.WrongCode, draw.BatchId, draw.Amount, batch.Batch.Display));
                continue;
            }

            wanted[draw.BatchId] = wanted.GetValueOrDefault(draw.BatchId) + draw.Amount;
        }

        foreach (var (id, asked) in wanted)
        {
            var batch = available[id];
            if (SplitMath.Round(asked) > batch.Available)
                issues.Add(new DrawIssue(DrawProblem.OverBatch, id, batch.Available, batch.Batch.Display));
        }

        var total = SplitMath.Round(draws.Sum(d => d.Amount));
        if (total != SplitMath.Round(amount))
            issues.Add(new DrawIssue(DrawProblem.Mismatch, 0, total));

        return issues;
    }

    /// <summary>
    /// Пренася произхода нататък: какъв дял от произведеното е от коя партида.
    ///
    /// Това е стъпката, заради която връзката с клиента не се къса при
    /// третирането. От дванайсет тона — 8,2 от Метал ООД и 3,8 от Скрап ЕООД —
    /// излизат девет тона мед; медта носи същото съотношение, защото друго
    /// съотношение не се знае. Претенцията е точно толкова силна, колкото е:
    /// пропорционална, не измерена.
    ///
    /// Сборът на върнатите дялове е точно <paramref name="produced"/>, доколкото
    /// чакащото стига. Образувано ЦЯЛОТО чакащо връща всяка партида точно
    /// каквато е — до грам. Над чакащото произход няма (виж <see cref="Split"/>).
    /// </summary>
    public static IReadOnlyList<Draw> Inherit(IReadOnlyList<Draw> source, decimal produced) =>
        Split(source, [produced])[0];

    /// <summary>
    /// Разписва произхода на една купчина върху няколко изходни кода НАВЕДНЪЖ.
    ///
    /// Тук живееше дефектът с остатъците от по няколко грама. Всеки изходен
    /// код наследяваше произхода си поотделно, а съотношението на партидата се
    /// закръгляше до сотни от процента: 1,029 т от 39,622 т са 2,597 %, станали
    /// 2,60 %, и партидата даде 1,030 т вместо 1,029. Друга даде 0,598 вместо
    /// 0,600. Сборът на реда оставаше точен — затова нищо не се виждаше на
    /// екрана, — но по партиди единият грам оставаше да чака в „Третиран“, а
    /// другият сочеше партида, в която нищо не чака. Внесен цял месец от
    /// „Деметра“ (553 доставки, 1014 т), изтретиран и образуван наведнъж,
    /// оставяше 3,3 т, пръснати по 270 партиди — и месецът не се затваряше.
    ///
    /// Сега сметката е в грамове и в двете посоки едновременно — таблица
    /// „партида × изходен код“, на която и редовете, и колоните събират точно:
    ///
    /// • всяка партида дава точно колкото е взето от нея (сборът по реда);
    /// • всеки изходен код получава точно количеството си (сборът по колоната).
    ///
    /// Колоните се пълнят една след друга, всяка пропорционално на онова, което
    /// още е останало във всяка партида, а последната поема точния остатък.
    /// Така никой дял не излиза отрицателен и никоя партида не дава повече,
    /// отколкото има.
    ///
    /// Изходът е по-малко от входа: всяка партида дава пропорционално, до грам.
    /// Изходът е повече от входа: произход има само за колкото е чакало, а
    /// излишъкът остава без партида — собствен отпадък на площадката, както
    /// казва и записът (<c>LedgerWriter</c>).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Draw>> Split(
        IReadOnlyList<Draw> source, IReadOnlyList<decimal> parts)
    {
        var have = source.Select(d => Grams(d.Amount)).ToArray();
        var want = parts.Select(Grams).ToArray();

        var held = have.Sum();
        var made = want.Sum();

        if (held <= 0 || made <= 0) return [.. parts.Select(_ => (IReadOnlyList<Draw>)[])];

        // Сборовете по редове (партиди) и по колони (кодове) трябва да са равни,
        // иначе таблицата няма решение. Изравнява се по-малкото от двете.
        var rows = made < held ? Apportion(made, have) : have;
        var columns = made > held ? Apportion(held, want) : want;

        var left = rows.ToArray();
        var last = Array.FindLastIndex(columns, c => c > 0);
        var result = new List<IReadOnlyList<Draw>>(parts.Count);

        for (var p = 0; p < columns.Length; p++)
        {
            var take = columns[p] <= 0 ? new long[left.Length]
                : p == last ? left.ToArray()
                : Apportion(columns[p], left);

            for (var b = 0; b < left.Length; b++) left[b] -= take[b];

            result.Add(
            [
                .. source.Select((draw, b) => new Draw(draw.BatchId, take[b] * 0.001m))
                    .Where(draw => draw.Amount != 0)
            ]);
        }

        return result;
    }

    /// <summary>
    /// Разделя количество пропорционално на теглата — точно, до грам.
    ///
    /// Без проценти по пътя. Процентът със два знака е закръгляне върху
    /// закръгляне и при десетки тонове и стотици партиди грешката му излиза в
    /// килограми. Тук всеки дял е колкото му се пада, отрязан до грам, а
    /// останалите грамове отиват при дяловете с най-голяма отрязана част. Сборът
    /// е точно <paramref name="total"/>.
    ///
    /// Нулево или отрицателно тегло не получава нищо.
    /// </summary>
    public static IReadOnlyList<decimal> Prorate(decimal total, IReadOnlyList<decimal> weights) =>
    [
        .. Apportion(Grams(total), [.. weights.Select(Grams)]).Select(g => g * 0.001m)
    ];

    /// <summary>
    /// Методът на най-големия остатък, в цели грамове.
    ///
    /// Всеки дял се отрязва надолу; липсващите до сбора грамове се раздават по
    /// един на дяловете с най-голяма отрязана част, при равенство — на
    /// по-ранния. Щом сборът не надхвърля сбора на теглата, никой дял не
    /// надхвърля теглото си — на това стъпва <see cref="Split"/>.
    /// </summary>
    private static long[] Apportion(long total, IReadOnlyList<long> weights)
    {
        var shares = new long[weights.Count];

        Int128 whole = 0;
        foreach (var weight in weights) if (weight > 0) whole += weight;

        if (total <= 0 || whole == 0) return shares;

        var cut = new Int128[weights.Count];
        var given = 0L;

        for (var i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0) continue;

            var product = (Int128)total * weights[i];

            shares[i] = (long)(product / whole);
            cut[i] = product % whole;
            given += shares[i];
        }

        var left = total - given;

        foreach (var i in Enumerable.Range(0, weights.Count)
                     .Where(i => weights[i] > 0)
                     .OrderByDescending(i => cut[i])
                     .ThenBy(i => i))
        {
            if (left <= 0) break;

            shares[i]++;
            left--;
        }

        return shares;
    }

    /// <summary>Количеството в цели грамове — точността, до която брои и НИСО.</summary>
    private static long Grams(decimal amount)
    {
        var rounded = SplitMath.Round(amount);

        return rounded <= 0 ? 0 : (long)(rounded * 1000m);
    }

    /// <summary>
    /// Чий е бил този отпадък — дяловете, събрани по клиент.
    ///
    /// По партида, а не по ЕИК: два пъти един и същ клиент с различни доставки
    /// са две партиди и човекът иска да види коя. Събирането по клиент е работа
    /// на екрана, който така или иначе групира.
    /// </summary>
    public static IReadOnlyList<OriginShare> Origins(
        IEnumerable<Draw> draws, IReadOnlyDictionary<long, Batch> batches)
    {
        var shares = new Dictionary<long, decimal>();

        foreach (var draw in draws)
        {
            if (!batches.ContainsKey(draw.BatchId)) continue;

            shares[draw.BatchId] = shares.GetValueOrDefault(draw.BatchId) + draw.Amount;
        }

        return
        [
            .. shares
                .Select(pair => new OriginShare(
                    pair.Key,
                    batches[pair.Key].Eik,
                    batches[pair.Key].PartnerName,
                    SplitMath.Round(pair.Value)))
                .Where(share => share.Amount != 0)
                .OrderByDescending(share => share.Amount)
                .ThenBy(share => share.BatchId)
        ];
    }
}
