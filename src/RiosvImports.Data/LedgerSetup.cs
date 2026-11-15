using RiosvImports.Core.Ledger;
using RiosvImports.Core.Values;

namespace RiosvImports.Data;

/// <summary>
/// Началото на воденето: от кой месец и с какво в склада.
///
/// Стои над трите хранилища, защото едно обявено начално количество е две
/// записа наведнъж — партида и движение — и половината от тях е по-лошо от
/// нищо: партида без движение не се вижда никъде, движение без партида няма
/// произход.
///
/// Заварената наличност се записва като движение с
/// <see cref="Move.Opening"/>, датирано в последния ден преди началния месец.
/// Така първият месец се смята със същата сметка като всеки следващ, вместо
/// наличността да носи „и плюс началното“ на всяко място, където се появява —
/// и вместо отчетът да трябва да помни, че януари е по-различен.
/// </summary>
public sealed class LedgerSetup(BatchStore batches, LedgerStore ledger, PeriodStore periods)
{
    private readonly BatchStore _batches = batches;
    private readonly LedgerStore _ledger = ledger;
    private readonly PeriodStore _periods = periods;

    /// <summary>От кой месец се води складът. Null значи „още не е обявено“.</summary>
    public LedgerPeriod? Start(long companyId) => _periods.Start(companyId);

    public void SetStart(long companyId, LedgerPeriod period) => _periods.SetStart(companyId, period);

    /// <summary>Денят, в който стоят заварените количества — преди първия работен ден.</summary>
    public static DateOnly Eve(LedgerPeriod start) => start.First.AddDays(-1);

    /// <summary>
    /// Кой месец да се предложи, докато началото още не е обявено: първият с
    /// движения, а без движения — текущият. Само предложение: записва се едва
    /// когато човек го потвърди.
    /// </summary>
    public LedgerPeriod Proposal(long companyId)
    {
        var first = _ledger.Read(companyId)
            .Where(m => !m.Opening)
            .Select(m => (DateOnly?)m.Date)
            .Min();

        return LedgerPeriod.Of(first ?? DateOnly.FromDateTime(DateTime.Today));
    }

    /// <summary>
    /// Най-ранното истинско движение преди този месец — то спира преместването
    /// на началото натам. Null значи, че пътят е чист.
    /// </summary>
    public DateOnly? Before(long companyId, LedgerPeriod start) =>
        _ledger.Read(companyId)
            .Where(m => !m.Opening && m.Date < start.First)
            .Select(m => (DateOnly?)m.Date)
            .Min();

    /// <summary>
    /// Мести началото на воденето и с него — завареното.
    ///
    /// Завареното стои в деня преди началния месец; сменен месец значи
    /// заварено на друга дата. Затова движенията и партидите му се пренасят
    /// заедно с настройката — иначе те биха останали в стария ден, извън
    /// воденето или насред него.
    ///
    /// Напред не се мести покрай истинско движение: приемане от 5 септември,
    /// оставено ПРЕДИ завареното от 30 септември, би черпило от склад, който
    /// тогава още не е обявен. Връща false и оставя всичко както е.
    /// </summary>
    public bool MoveStart(long companyId, LedgerPeriod to)
    {
        if (Before(companyId, to) is not null) return false;

        var eve = Eve(to);
        var moves = _ledger.Read(companyId).Where(m => m.Opening).ToList();

        foreach (var move in moves)
        {
            _ledger.Update(move.Id, new MoveDraft(
                move.Kind, eve, move.Ewc, move.Amount, move.Stagenum,
                Opening: true,
                Note: _ledger.Note(move.Id),
                Fields: _ledger.Fields(move.Id),
                Draws: _ledger.Draws(move.Id)));
        }

        foreach (var batch in _batches.List(companyId).Where(b => b.Opening))
        {
            _batches.Update(batch.Id, new BatchDraft(
                batch.Eik, batch.PartnerName, batch.Ewc, eve, batch.Stagenum, batch.Document,
                Opening: true, Fields: _batches.Fields(batch.Id)));
        }

        SetStart(companyId, to);

        return true;
    }

    // ------------------------------------------------------------- четене

    /// <summary>
    /// Обявените начални количества и колко от тях са изразходвани.
    ///
    /// Изразходваното се показва, защото то решава дали редът още може да се
    /// поправи: количество, от което вече е черпено, не е число в настройка, а
    /// начало на верига.
    /// </summary>
    public IReadOnlyList<OpeningStock> Openings(long companyId, string? stagenum = null)
    {
        var moves = _ledger.Read(companyId, stagenum);
        var links = _ledger.Links(companyId, stagenum);
        var all = _batches.List(companyId, stagenum);

        var known = all.ToDictionary(b => b.Id);
        var consumed = new Dictionary<(long, StoreKind), decimal>();

        foreach (var store in Moves.Stores)
            foreach (var item in StockMath.Batches(all, moves, links, store, DateOnly.MaxValue))
                consumed[(item.Batch.Id, store)] = item.Consumed;

        var openings = new List<OpeningStock>();

        foreach (var move in moves.Where(m => m.Opening))
        {
            var link = links.FirstOrDefault(l => l.MoveId == move.Id);
            if (link is null || !known.TryGetValue(link.BatchId, out var batch)) continue;

            // Заварените количества са приемане или образуване — и двете
            // ВЛИЗАТ някъде. Складът им е този, в който влизат.
            if (Moves.To(move.Kind) is not { } store) continue;

            openings.Add(new OpeningStock(
                batch,
                store,
                move.Amount,
                consumed.GetValueOrDefault((batch.Id, store))));
        }

        return
        [
            .. openings
                .OrderBy(o => o.Store)
                .ThenBy(o => o.Batch.Ewc, StringComparer.Ordinal)
                .ThenBy(o => o.Batch.Id)
        ];
    }

    // ------------------------------------------------------------- писане

    /// <summary>
    /// Обявява едно заварено количество и връща номера на партидата му.
    ///
    /// Партидата се създава дори когато клиентът не се знае: тя е носителят, по
    /// който произходът се проследява нататък, и без нея третирането на това
    /// количество няма от какво да черпи.
    /// </summary>
    public long AddOpening(long companyId, LedgerPeriod start, OpeningEntry entry)
    {
        var eve = Eve(start);

        var batchId = _batches.Add(companyId, new BatchDraft(
            entry.Eik,
            entry.PartnerName,
            entry.Ewc,
            eve,
            entry.Stagenum,
            entry.Document,
            Opening: true));

        _ledger.Add(companyId, new MoveDraft(
            entry.Store == StoreKind.Received ? MoveKind.Received : MoveKind.Generated,
            eve,
            entry.Ewc,
            entry.Amount,
            entry.Stagenum,
            Opening: true,
            Draws: [new Draw(batchId, entry.Amount)]));

        return batchId;
    }

    /// <summary>
    /// Маха обявено начално количество.
    ///
    /// Връща false, ако от него вече е черпено: махането щеше да отнесе
    /// произхода на истинска работа и да остави третирането да сочи към
    /// партида, каквато вече няма. Тогава редът се поправя, като се поправи
    /// движението, което го е изразходвало.
    /// </summary>
    public bool RemoveOpening(long companyId, long batchId)
    {
        var moves = _ledger.Read(companyId).ToDictionary(m => m.Id);
        var mine = _ledger.Links(companyId).Where(l => l.BatchId == batchId).ToList();

        if (mine.Any(l => moves.TryGetValue(l.MoveId, out var move) && !move.Opening)) return false;

        foreach (var link in mine) _ledger.Remove(link.MoveId);

        _batches.Remove(batchId);
        return true;
    }

    /// <summary>
    /// Отделя част от заварено количество към нов клиент.
    ///
    /// Заварените количества идват от предишни периоди и почти никога не са по
    /// партиди: „на 31 август имаше 40 тона 160216“ и толкова. Произходът им
    /// обаче се сеща по-късно — от папка, от протокол, от паметта на човека, —
    /// и тогава трябва да може да се впише, без да се пренаписва целият склад.
    ///
    /// Отделя се като ВТОРО заварено количество, а не като втори дял на
    /// същото: така „едно количество ↔ една партида“ остава вярно и списъкът
    /// показва двата реда такива, каквито са — 25 тона от Метал ООД и 15 без
    /// посочен клиент.
    ///
    /// Отделя се само от неизразходваното. Част, от която вече е черпено, има
    /// произход надолу по веригата и той не може да се смени със задна дата,
    /// без да излъже вече съставен отчет.
    ///
    /// Връща номера на новата партида, или null, ако отделянето не се държи.
    /// </summary>
    public long? SplitOpening(
        long companyId,
        long batchId,
        decimal amount,
        string eik,
        string partnerName,
        string document = "")
    {
        var wanted = SplitMath.Round(amount);
        if (wanted <= 0) return null;

        if (_batches.Find(batchId) is not { Opening: true } batch) return null;

        var moves = _ledger.Read(companyId).Where(m => m.Opening).ToDictionary(m => m.Id);

        var link = _ledger.Links(companyId)
            .FirstOrDefault(l => l.BatchId == batchId && moves.ContainsKey(l.MoveId));

        if (link is null) return null;

        var move = moves[link.MoveId];

        // Колко от заварената партида още стои — оттам се отделя.
        var stock = Openings(companyId, batch.Stagenum)
            .FirstOrDefault(o => o.Batch.Id == batchId);

        if (stock is null || wanted > stock.Available) return null;

        var rest = SplitMath.Round(move.Amount - wanted);

        var made = _batches.Add(companyId, new BatchDraft(
            eik,
            partnerName,
            batch.Ewc,
            batch.ReceivedOn,
            batch.Stagenum,
            document.Length > 0 ? document : batch.Document,
            Opening: true,
            Fields: _batches.Fields(batchId)));

        if (rest > 0)
        {
            _ledger.Update(move.Id, new MoveDraft(
                move.Kind, move.Date, move.Ewc, rest, move.Stagenum,
                Opening: true, Draws: [new Draw(batchId, rest)]));
        }
        else
        {
            // Отделено е всичко: старата партида остава без нищо зад себе си.
            // Тя може да си отиде, защото неизразходвана значи, че никой
            // надолу не сочи към нея.
            _ledger.Remove(move.Id);
            _batches.Remove(batchId);
        }

        _ledger.Add(companyId, new MoveDraft(
            move.Kind, move.Date, move.Ewc, wanted, move.Stagenum,
            Opening: true, Draws: [new Draw(made, wanted)]));

        return made;
    }

    /// <summary>
    /// Обявява целия списък наново.
    ///
    /// Заместване, а не добавяне: човек, който поправя едно число и записва,
    /// очаква да види поправеното, а не двойно количество. Заварените редове,
    /// от които вече е черпено, оцеляват — те не са настройка — и се връщат,
    /// за да се каже кои са останали.
    /// </summary>
    public IReadOnlyList<OpeningStock> ReplaceOpenings(
        long companyId, LedgerPeriod start, IReadOnlyList<OpeningEntry> entries, string? stagenum = null)
    {
        var kept = new List<OpeningStock>();

        foreach (var opening in Openings(companyId, stagenum))
            if (!RemoveOpening(companyId, opening.Batch.Id))
                kept.Add(opening);

        foreach (var entry in entries)
        {
            if (entry.Amount <= 0) continue;

            AddOpening(companyId, start, entry);
        }

        SetStart(companyId, start);

        return kept;
    }

    /// <summary>
    /// Пренася вече въведените начални количества от стария изглед „Баланс“.
    ///
    /// Тези числа са въвеждани по код и година, без клиент: балансът не е искал
    /// произход. Затова партидите излизат без ЕИК и се виждат като такива —
    /// измисленият клиент щеше да изглежда като отговор.
    ///
    /// Влизат в склад „Получен“, защото старото начално количество е било
    /// „каквото стои на площадката“, а точно то се третира нататък.
    /// </summary>
    public int Import(long companyId, LedgerPeriod start, IEnumerable<Opening> legacy)
    {
        var written = 0;

        foreach (var opening in legacy)
        {
            if (opening.Amount <= 0) continue;

            AddOpening(companyId, start, new OpeningEntry(
                StoreKind.Received,
                opening.Ewc,
                opening.Amount,
                Document: "пренесено от началните количества"));

            written++;
        }

        return written;
    }
}
