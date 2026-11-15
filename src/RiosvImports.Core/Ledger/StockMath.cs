using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Наличността: колко има, къде и към кога.
///
/// Нищо тук не знае за прозорци, за база и за Excel — влизат движения, излизат
/// числа. Затова „третираното се вади от полученото“ се проверява с тест, а не
/// с оглед на екрана (правило 12).
///
/// <b>Движенията идват вече пресяти по фирма и площадка.</b> Наличността е по
/// (фирма, площадка, код) — две площадки на едно дружество нямат общ склад — но
/// пресяването е работа на четенето, не на сметката. Тук се събира каквото е
/// подадено; ако е подадено смесено, ще излезе смесено.
///
/// Началните количества не са отделно понятие: те са движения с
/// <see cref="Move.Opening"/>, вписани преди първия работен ден. Така първият
/// месец се смята със същата сметка като всеки следващ, вместо наличността да
/// има „и плюс началното“ на всяко място, където се появява.
/// </summary>
public static class StockMath
{
    /// <summary>
    /// Наличността на един код в един склад в края на дадения ден.
    ///
    /// „В края“, а не „към“: движение от същия ден се брои. Иначе приемане и
    /// третиране в един ден биха се разминали и денят, в който отпадъкът е
    /// дошъл, щеше да е денят, в който не може да се третира.
    /// </summary>
    public static decimal Balance(IEnumerable<Move> moves, StoreKind store, string ewc, DateOnly on)
    {
        var code = ValueConverter.NormalizeCode(ewc);
        decimal total = 0;

        foreach (var move in moves)
        {
            if (!move.Touches(store)) continue;
            if (move.Date > on) continue;
            if (!Same(move.Ewc, code)) continue;

            total += move.Signed(store);
        }

        return SplitMath.Round(total);
    }

    /// <summary>Наличността по кодове в един склад в края на дадения ден.</summary>
    public static Dictionary<string, decimal> Stock(
        IEnumerable<Move> moves, StoreKind store, DateOnly on)
    {
        var stock = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var move in moves)
        {
            if (!move.Touches(store)) continue;
            if (move.Date > on) continue;

            var code = ValueConverter.NormalizeCode(move.Ewc);
            if (code.Length == 0) continue;

            stock[code] = stock.GetValueOrDefault(code) + move.Signed(store);
        }

        return Trim(stock);
    }

    /// <summary>Каквото е стояло в склада, преди месецът да започне.</summary>
    public static Dictionary<string, decimal> Opening(
        IEnumerable<Move> moves, StoreKind store, LedgerPeriod period) =>
        Stock(moves, store, period.First.AddDays(-1));

    /// <summary>Каквото остава в склада, след като месецът свърши.</summary>
    public static Dictionary<string, decimal> Closing(
        IEnumerable<Move> moves, StoreKind store, LedgerPeriod period) =>
        Stock(moves, store, period.Last);

    /// <summary>
    /// Какво се е случило през месеца — влязлото минус излязлото, по кодове.
    ///
    /// Не разликата между двете салда: тя дава същото число, но по обиколен
    /// път и се разминава при код, който през месеца е влязъл и излязъл целият.
    /// </summary>
    public static Dictionary<string, decimal> Movement(
        IEnumerable<Move> moves, StoreKind store, LedgerPeriod period)
    {
        var movement = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var move in moves)
        {
            if (!move.Touches(store)) continue;
            if (!period.Covers(move.Date)) continue;

            var code = ValueConverter.NormalizeCode(move.Ewc);
            if (code.Length == 0) continue;

            movement[code] = movement.GetValueOrDefault(code) + move.Signed(store);
        }

        return Trim(movement);
    }

    /// <summary>
    /// Сборът по вид движение за един месец — четирите числа над таблицата.
    ///
    /// Количествата са такива, каквито са записани: положителни. Знакът е
    /// въпрос на склада, а тук се брои движение.
    /// </summary>
    public static Dictionary<MoveKind, decimal> Totals(IEnumerable<Move> moves, LedgerPeriod period)
    {
        // Всички видове, с рециклирането: то изчерпва „Третиран“ и сбор без
        // него не би казал къде е отишло третираното.
        var totals = Moves.Every.ToDictionary(kind => kind, _ => 0m);

        foreach (var move in moves)
        {
            if (!period.Covers(move.Date)) continue;

            totals[move.Kind] += move.Amount;
        }

        foreach (var kind in Moves.Every) totals[kind] = SplitMath.Round(totals[kind]);

        return totals;
    }

    /// <summary>
    /// Наличното по партиди в един склад към края на дадения ден.
    ///
    /// Връзките (<see cref="LedgerLink"/>) са единственият източник: движение
    /// може да пипне няколко партиди наведнъж — третиране, което черпи от три
    /// доставки — и тогава едно поле „партида“ на самото движение би било
    /// половин истина.
    ///
    /// Подредбата е по дата на приемане, после по номер: точно редът, в който
    /// разпределянето ще ги изчерпи, за да се чете списъкът както ще се
    /// похарчи.
    /// </summary>
    public static IReadOnlyList<BatchStock> Batches(
        IEnumerable<Batch> batches,
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        StoreKind store,
        DateOnly on)
    {
        var known = batches.ToDictionary(b => b.Id);
        var seen = moves.ToDictionary(m => m.Id);

        var incoming = new Dictionary<long, decimal>();
        var outgoing = new Dictionary<long, decimal>();

        foreach (var link in links)
        {
            if (!seen.TryGetValue(link.MoveId, out var move)) continue;
            if (move.Date > on) continue;
            if (!known.ContainsKey(link.BatchId)) continue;

            var sign = move.Sign(store);
            if (sign == 0) continue;

            var side = sign > 0 ? incoming : outgoing;
            side[link.BatchId] = side.GetValueOrDefault(link.BatchId) + link.Amount;
        }

        var stock = new List<BatchStock>();

        foreach (var (id, batch) in known)
        {
            var received = SplitMath.Round(incoming.GetValueOrDefault(id));
            var consumed = SplitMath.Round(outgoing.GetValueOrDefault(id));

            // Партида, която този склад изобщо не е виждал, не е негов ред.
            if (received == 0 && consumed == 0) continue;

            stock.Add(new BatchStock(batch, received, consumed));
        }

        return
        [
            .. stock
                .OrderBy(s => s.Batch.ReceivedOn)
                .ThenBy(s => s.Batch.Id)
        ];
    }

    /// <summary>
    /// Наличността, разбита едновременно по код и по партида.
    ///
    /// <b>Не важи за склад „Третиран“.</b> Там влизащото и излизащото носят
    /// различни кодове по устройство — влиза кабел, излиза мед — и разликата по
    /// код не значи нищо. За него има <see cref="Waiting"/>.
    ///
    /// Това е изгледът, заради който партидите изобщо съществуват: не „има 7
    /// тона мед“, а „има 7 тона мед, от които 4,8 са дошли от Метал ООД“.
    ///
    /// Кодът се взема от ДВИЖЕНИЕТО, а не от партидата, и разликата е
    /// съществена. В склад „Получен“ двата съвпадат. В склад „Образуван“ не
    /// съвпадат никога: партидата помни кода, с който отпадъкът е дошъл, а
    /// движението — кода, с който е излязъл след третирането. Ако тук се
    /// вземеше кодът на партидата, образуваната мед щеше да се води кабел.
    /// </summary>
    public static IReadOnlyList<StockShare> Shares(
        IEnumerable<Batch> batches,
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        StoreKind store,
        DateOnly on,
        DateOnly? from = null)
    {
        var known = batches.ToDictionary(b => b.Id);
        var seen = moves.ToDictionary(m => m.Id);
        var totals = new Dictionary<(string Ewc, long Batch), decimal>();

        foreach (var link in links)
        {
            if (!seen.TryGetValue(link.MoveId, out var move)) continue;
            if (move.Date > on) continue;
            if (from is { } start && move.Date < start) continue;
            if (!known.ContainsKey(link.BatchId)) continue;

            var sign = move.Sign(store);
            if (sign == 0) continue;

            var code = ValueConverter.NormalizeCode(move.Ewc);
            if (code.Length == 0) continue;

            var key = (code, link.BatchId);
            totals[key] = totals.GetValueOrDefault(key) + sign * link.Amount;
        }

        return
        [
            .. totals
                .Select(pair => new StockShare(
                    pair.Key.Ewc, known[pair.Key.Batch], SplitMath.Round(pair.Value)))
                .Where(share => share.Amount != 0)
                .OrderBy(share => share.Ewc, StringComparer.Ordinal)
                .ThenBy(share => share.Batch.ReceivedOn)
                .ThenBy(share => share.Batch.Id)
        ];
    }

    /// <summary>
    /// Същото като <see cref="Shares"/>, но с получателя на всеки ред.
    ///
    /// За трите склада с наличност е буквално <see cref="Shares"/>, обвит: там
    /// нищо не е излязло от площадката и получател няма.
    ///
    /// За „Предаден“ е друго и точно заради него функцията съществува: един и
    /// същ код от една и съща партида може да е излязъл към трима различни
    /// оползотворители. <see cref="Shares"/> ги събира в едно число — вярно за
    /// въпроса „колко е излязло“ и безполезно за „на кого“. Тук ключът е
    /// (код, партида, ПОЛУЧАТЕЛ) и трите реда остават три.
    ///
    /// Получателите се подават готови: те идват от колоните на схемата, които
    /// не участват в нито една сметка и затова не пътуват в <see cref="Move"/>.
    ///
    /// <b>В летописа един ред е ЕДНО предаване.</b> В един месец към един и същ
    /// оползотворител тръгват три курса; събрани в един ред, те стават число, а
    /// въпросът „кога и по колко“ остава без отговор — а точно него задава и
    /// проверяващият, и самият получател. Затова ключът носи и движението, и
    /// редът носи датата си.
    ///
    /// Сборовете не се менят от това: три реда по четири тона събират същото
    /// като един ред от дванайсет.
    /// </summary>
    /// <remarks>
    /// <paramref name="from"/> отрязва отдолу и мени СМИСЪЛА на числото: без
    /// него редът е наличност („какво стои към 30.09“), с него — движение
    /// („какво се случи през септември“). Второто може да е и отрицателно и
    /// точно затова се показва под друго заглавие.
    /// </remarks>
    public static IReadOnlyList<StockLine> Lines(
        IEnumerable<Batch> batches,
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        StoreKind store,
        DateOnly on,
        IReadOnlyDictionary<long, HandParty>? parties = null,
        DateOnly? from = null)
    {
        // „Рециклиран“ е летопис като „Предаден“ и се чете по същия начин: един
        // ред е ЕДНО рециклиране, с датата си. Получател там няма, но редът по
        // движение е нужен по друга причина — сгрешеното рециклиране се връща
        // ред по ред, а сбор от три рециклирания не може да се върне наполовина.
        var perMove = store == StoreKind.Recycled || (store == StoreKind.Submitted && parties is not null);

        if (!perMove)
            return [.. Shares(batches, moves, links, store, on, from).Select(StockLine.Of)];

        parties ??= new Dictionary<long, HandParty>();

        var known = batches.ToDictionary(b => b.Id);
        var seen = moves.ToDictionary(m => m.Id);

        var totals = new Dictionary<(string Ewc, long Batch, string Party, long Move), decimal>();
        var when = new Dictionary<long, DateOnly>();

        foreach (var link in links)
        {
            if (!seen.TryGetValue(link.MoveId, out var move)) continue;
            if (move.Date > on) continue;
            if (from is { } start && move.Date < start) continue;
            if (!known.ContainsKey(link.BatchId)) continue;

            var sign = move.Sign(store);
            if (sign == 0) continue;

            var code = ValueConverter.NormalizeCode(move.Ewc);
            if (code.Length == 0) continue;

            var party = parties.GetValueOrDefault(link.MoveId, HandParty.None);

            when[link.MoveId] = move.Date;

            var key = (code, link.BatchId, party.Key, link.MoveId);
            totals[key] = totals.GetValueOrDefault(key) + sign * link.Amount;
        }

        return
        [
            .. totals
                .Select(pair => new StockLine(
                    new StockShare(pair.Key.Ewc, known[pair.Key.Batch], SplitMath.Round(pair.Value)),

                    // Страната е на самото движение: един получател с два
                    // договора дава редове с двата договора, а не с последния
                    // прочетен.
                    parties.GetValueOrDefault(pair.Key.Move, HandParty.None),
                    when.GetValueOrDefault(pair.Key.Move),
                    pair.Key.Move))
                .Where(line => line.Amount != 0)
                .OrderBy(line => line.Ewc, StringComparer.Ordinal)
                .ThenBy(line => line.Moved)
                .ThenBy(line => line.Batch.ReceivedOn)
                .ThenBy(line => line.Batch.Id)
        ];
    }

    /// <summary>
    /// Каквото е влязло в склада, без да е вързано за партида — по движение.
    ///
    /// <see cref="Lines"/> тръгва от дяловете и движение без дял просто не
    /// стига до него; <see cref="Loose"/> казва колко е то, но като едно число.
    /// Изгледът „Поотделно“ обещава ВСЯКО движение, затова остатъкът излиза тук
    /// със свой ред, на мястото на партида — <see cref="Batch.None"/>.
    ///
    /// Покритото от дяловете не се повтаря: движение, вързано наполовина, дава
    /// тук само другата си половина.
    /// </summary>
    public static IReadOnlyList<StockLine> Unlinked(
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        StoreKind store,
        DateOnly on,
        IReadOnlyDictionary<long, HandParty>? parties = null,
        DateOnly? from = null)
    {
        var covered = new Dictionary<long, decimal>();

        foreach (var link in links)
            covered[link.MoveId] = covered.GetValueOrDefault(link.MoveId) + link.Amount;

        var lines = new List<StockLine>();

        foreach (var move in moves)
        {
            if (move.Sign(store) <= 0) continue;
            if (move.Date > on) continue;
            if (from is { } start && move.Date < start) continue;

            var left = SplitMath.Round(move.Amount - covered.GetValueOrDefault(move.Id));
            if (left <= 0) continue;

            var code = ValueConverter.NormalizeCode(move.Ewc);

            lines.Add(new StockLine(
                new StockShare(code, Batch.None(code, move.Date), left),
                parties?.GetValueOrDefault(move.Id, HandParty.None) ?? HandParty.None,
                move.Date,
                move.Id));
        }

        return lines;
    }

    /// <summary>
    /// Целият път на една партида: всяко движение, което я е пипнало.
    ///
    /// Чете се от същите връзки, от които се смята и наличността — затова
    /// „какво стана с тези дванайсет тона“ и „колко още стоят“ не могат да си
    /// противоречат. Подредено по дата, после по номер на движението: редът, в
    /// който се е случило.
    /// </summary>
    public static Trail Trace(
        Batch batch, IEnumerable<Move> moves, IEnumerable<LedgerLink> links)
    {
        var seen = moves.ToDictionary(m => m.Id);
        var steps = new List<TrailStep>();

        foreach (var link in links)
        {
            if (link.BatchId != batch.Id) continue;
            if (!seen.TryGetValue(link.MoveId, out var move)) continue;

            steps.Add(new TrailStep(move, SplitMath.Round(link.Amount)));
        }

        return new Trail(batch, [.. steps.OrderBy(s => s.Move.Date).ThenBy(s => s.Move.Id)]);
    }

    /// <summary>
    /// Какво стои в склад „Третиран“.
    ///
    /// Три отговора наведнъж, защото са три въпроса към едно и също нещо:
    /// <see cref="Shares"/> го показва на екрана, <see cref="Draws"/> е пулът, от
    /// който черпи образуваното, а <see cref="Total"/> е числото, което решава
    /// дали месецът може да се отчете.
    ///
    /// <see cref="Loose"/> е третирано без посочена партида. То не се вижда по
    /// партиди, но стои в склада като всичко друго — иначе месец с
    /// неразпределено третиране би изглеждал приключен.
    /// </summary>
    public sealed record TreatedStock(
        IReadOnlyList<StockShare> Shares,
        IReadOnlyList<Draw> Draws,
        decimal Loose,
        IReadOnlyList<UnbackedDraw> Unbacked)
    {
        /// <summary>Колко общо чака да бъде обявено като образувано.</summary>
        public decimal Total => SplitMath.Round(Shares.Sum(s => s.Amount) + Loose);

        /// <summary>Празен склад — всичко третирано си е намерило образуваното.</summary>
        public bool IsEmpty => Total <= 0;

        /// <summary>Колко образувано е обявило произход, който го няма.</summary>
        public decimal Groundless => SplitMath.Round(Unbacked.Sum(u => u.Amount));

        public static TreatedStock None => new([], [], 0m, []);
    }

    /// <summary>
    /// Дял на образуване, който не е намерил какво да изчерпи.
    ///
    /// Движението сочи партида — тоест твърди „това излезе от нейното третиране“
    /// — а в нея нищо не чака. Такъв дял не намалява склада: третираното остава
    /// да чака и може да бъде обявено втори път. Оттам идва двойното броене,
    /// което се вижда чак в месечната справка като „образувано над третирано“.
    ///
    /// <b>Това не е същото като собствен отпадък.</b> Площадка, която образува,
    /// без да третира, не сочи ничия партида — движението ѝ няма дялове и тук
    /// изобщо не стига. Тук се хваща само обявен произход без основание.
    /// </summary>
    public sealed record UnbackedDraw(long MoveId, long BatchId, decimal Amount);

    /// <summary>
    /// Третираното, което още не е станало образуван отпадък.
    ///
    /// Тук не важи сметката на другите два склада. В тях влизащото и излизащото
    /// носят един и същ код; в този — по устройство различни: влиза кабел
    /// 160216, излизат мед 191202 и пластмаса 191204. Затова наличността му се
    /// смята по ПАРТИДИ, а кодът се пази за показване.
    ///
    /// Вътре в партидата третиранията се изчерпват в реда, в който са станали —
    /// най-старото първо. Образувано над наличното НЕ вкарва склада на минус:
    /// то е собствен отпадък на площадката, а не следствие от чуждото. Точно
    /// заради това правило площадка, която само образува и нищо не третира,
    /// няма какво да чака и месецът ѝ се отчита.
    ///
    /// Рециклираното изчерпва по същия начин като образуваното: и то е излязло
    /// от третирането. Разликата е само по-нататък — то не влиза в отчета.
    /// </summary>
    public static TreatedStock Waiting(
        IEnumerable<Batch> batches,
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        DateOnly on)
    {
        var known = batches.ToDictionary(b => b.Id);

        var seen = moves
            .Where(m => m.Date <= on && m.Kind is MoveKind.Treated or MoveKind.Generated or MoveKind.Recycled)
            .ToDictionary(m => m.Id);

        var steps = new Dictionary<long, List<TrailStep>>();
        var covered = new Dictionary<long, decimal>();
        var unbacked = new List<UnbackedDraw>();

        foreach (var link in links)
        {
            if (!seen.TryGetValue(link.MoveId, out var move)) continue;

            covered[link.MoveId] = covered.GetValueOrDefault(link.MoveId) + link.Amount;

            if (!known.ContainsKey(link.BatchId)) continue;

            if (!steps.TryGetValue(link.BatchId, out var list)) steps[link.BatchId] = list = [];

            list.Add(new TrailStep(move, link.Amount));
        }

        var shares = new List<StockShare>();

        foreach (var (batchId, list) in steps)
        {
            var lots = new List<(string Ewc, decimal Left)>();

            // В рамките на деня влизащото върви първо, както в Reconcile: денят е
            // най-дребната мярка на склада и редът на въвеждане в него не значи
            // нищо. По номер образуване от същия ден, записано преди поправено
            // третиране, не намираше лот — и третираното оставаше в склада, вече
            // образувано.
            foreach (var step in list
                         .OrderBy(x => x.Move.Date)
                         .ThenByDescending(x => x.Move.Sign(StoreKind.Treated))
                         .ThenBy(x => x.Move.Id))
            {
                if (step.Move.Kind == MoveKind.Treated)
                {
                    lots.Add((ValueConverter.NormalizeCode(step.Move.Ewc), step.Amount));
                    continue;
                }

                var left = step.Amount;

                for (var i = 0; i < lots.Count && left > 0; i++)
                {
                    var take = SplitMath.Round(Math.Min(lots[i].Left, left));
                    if (take <= 0) continue;

                    lots[i] = (lots[i].Ewc, SplitMath.Round(lots[i].Left - take));
                    left = SplitMath.Round(left - take);
                }

                // Каквото не е намерило лот, не се губи мълчаливо.
                //
                // Дотук остатъкът просто отпадаше и складът оставаше непокътнат
                // — образуването минаваше ПРЕЗ него, без да го изчерпи, а
                // третираното можеше да се обяви втори път. Разликата излизаше
                // чак в месечната справка, като „образувано над третирано“, и
                // оттам нататък не се виждаше откъде е дошла.
                if (SplitMath.Round(left) > 0)
                    unbacked.Add(new UnbackedDraw(step.Move.Id, batchId, SplitMath.Round(left)));
            }

            foreach (var group in lots.Where(l => l.Left > 0)
                         .GroupBy(l => l.Ewc, StringComparer.Ordinal))
                shares.Add(new StockShare(
                    group.Key, known[batchId], SplitMath.Round(group.Sum(l => l.Left))));
        }

        // Третирано без партида: не се вижда по партиди, но чака като всичко
        // друго. Образувано без партида не изчерпва нищо — то не сочи откъде е.
        var loose = 0m;

        foreach (var move in seen.Values.Where(m => m.Kind == MoveKind.Treated))
            loose += Math.Max(0m, SplitMath.Round(move.Amount - covered.GetValueOrDefault(move.Id)));

        var draws = shares
            .GroupBy(s => s.Batch.Id)
            .Select(g => new Draw(g.Key, SplitMath.Round(g.Sum(s => s.Amount))))
            .ToList();

        return new TreatedStock(
        [
            .. shares
                .OrderBy(s => s.Ewc, StringComparer.Ordinal)
                .ThenBy(s => s.Batch.ReceivedOn)
                .ThenBy(s => s.Batch.Id)
        ], draws, SplitMath.Round(loose),
        [.. unbacked.OrderBy(u => u.MoveId).ThenBy(u => u.BatchId)]);
    }

    /// <summary>
    /// Колко от склада не е вързано за нито една партида.
    ///
    /// Разликата между това, което движенията казват, и това, което дяловете
    /// покриват. Не е грешка, а състояние: образувано над чакащото няма от
    /// какво да наследи произход, а предадено от склад „Образуван“ не намира
    /// партида, защото партидата помни кода, с който отпадъкът е ДОШЪЛ, а
    /// движението носи кода, с който е излязъл.
    ///
    /// Показва се отделно и точно затова: изглед, който брои само вързаното,
    /// би обявил за по-малко излязло, отколкото наистина е излязло.
    /// </summary>
    public static decimal Loose(
        IEnumerable<Move> moves,
        IEnumerable<LedgerLink> links,
        StoreKind store,
        DateOnly on)
    {
        var seen = new Dictionary<long, int>();
        decimal total = 0;

        foreach (var move in moves)
        {
            if (move.Date > on) continue;

            var sign = move.Sign(store);
            if (sign == 0) continue;

            seen[move.Id] = sign;
            total += sign * move.Amount;
        }

        decimal linked = 0;

        foreach (var link in links)
            if (seen.TryGetValue(link.MoveId, out var sign))
                linked += sign * link.Amount;

        return SplitMath.Round(Math.Max(0, total - linked));
    }

    /// <summary>Само партидите, от които още има какво да се вземе.</summary>
    public static IReadOnlyList<BatchStock> Available(
        IEnumerable<BatchStock> stock, string ewc = "")
    {
        var code = ValueConverter.NormalizeCode(ewc);

        return
        [
            .. stock.Where(s => !s.IsEmpty && (code.Length == 0 || Same(s.Batch.Ewc, code)))
        ];
    }

    /// <summary>
    /// Месеците, в които изобщо има движение — редът на изгледа „Месец“.
    ///
    /// Подредени назад: човек отваря програмата заради текущия месец, не заради
    /// първия.
    /// </summary>
    public static IReadOnlyList<LedgerPeriod> Periods(IEnumerable<Move> moves) =>
    [
        .. moves.Where(m => !m.Opening)
            .Select(m => m.Period)
            .Distinct()
            .OrderByDescending(p => p)
    ];

    /// <summary>Кодовете, които складът познава — за менюта и за пресяване.</summary>
    public static IReadOnlyList<string> Codes(IEnumerable<Move> moves) =>
    [
        .. moves.Select(m => ValueConverter.NormalizeCode(m.Ewc))
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
    ];

    /// <summary>Кодовете се сравняват сведени: „16 10 04“ и „161004“ са едно.</summary>
    private static bool Same(string left, string right) =>
        string.Equals(ValueConverter.NormalizeCode(left), right, StringComparison.Ordinal);

    /// <summary>
    /// Маха нулите и закръгля.
    ///
    /// Код, който през периода е влязъл и излязъл целият, не е наличност — той
    /// е ред от нули, който кара човек да търси разлика, каквато няма.
    /// </summary>
    private static Dictionary<string, decimal> Trim(Dictionary<string, decimal> values)
    {
        var trimmed = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var (code, amount) in values)
        {
            var rounded = SplitMath.Round(amount);
            if (rounded != 0) trimmed[code] = rounded;
        }

        return trimmed;
    }
}
