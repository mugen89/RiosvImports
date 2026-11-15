using RiosvImports.Core.Ledger;
using RiosvImports.Core.Values;

namespace RiosvImports.Data;

/// <summary>Едно движение, както го иска записът — с клиента, но без дяловете.</summary>
public sealed record MoveRequest(
    MoveKind Kind,
    DateOnly Date,
    string Ewc,
    decimal Amount,
    string Stagenum = "1",
    string Eik = "",
    string Partner = "",
    string Document = "",
    string Note = "",
    bool Direct = false,
    IReadOnlyDictionary<string, string>? Fields = null,
    long? MoveId = null,
    IReadOnlyList<Draw>? Draws = null,
    string EwcOut = "",
    string Contract = "");

/// <summary>
/// Записването на движение с всичко, което върви с него.
///
/// Едно движение почти никога не е един запис: приемането ражда партида,
/// третирането изчерпва партиди, образуваното наследява дяловете им. Тези
/// правила се пишат ВЕДНЪЖ тук, защото движения се въвеждат от три места — по
/// едно в прозорче, наведнъж в таблица и наведнъж от чужд файл — и три техни
/// копия щяха да се разминат на първата поправка.
///
/// Разпределението се смята, когато не е подадено. Подаденото не се пипа: то
/// идва от човек, който го е погледнал и е решил друго, а FIFO е предположение,
/// не правило.
/// </summary>
public sealed class LedgerWriter(BatchStore batches, LedgerStore ledger, PartnerStore? partners = null)
{
    private readonly BatchStore _batches = batches;
    private readonly LedgerStore _ledger = ledger;

    /// <summary>
    /// Списъкът на доставчиците — научава се тук, при всяко записано приемане.
    ///
    /// Тук, а не в прозорците: приемания се записват от три места — прозорче,
    /// таблица и внесен дневник — и всички минават оттук. Научено на едно
    /// място, списъкът не пропуска нито едно от тях.
    /// </summary>
    private readonly PartnerStore? _partners = partners;

    /// <summary>Полетата от схемата, в които стоят името и ЕИК-ът на доставчика.</summary>
    public const string NameField = "name_originator";
    public const string EikField = "pin_originator";

    /// <summary>
    /// Допълва доставчика на приемането от списъка — преди да бъде записан.
    ///
    /// Липсващ ЕИК при познато име (или обратното) се попълва, а написаното
    /// само с други главни букви се изравнява с познатото. Пише се и в
    /// партидата, и в полетата, които отиват в книгата: двете трябва да казват
    /// едно и също. Различно написаното не се пипа (<see cref="PartnerStore.Complete"/>).
    /// </summary>
    private MoveRequest Complete(long companyId, MoveRequest request)
    {
        if (_partners is null) return request;

        var name = request.Partner.Length > 0
            ? request.Partner
            : request.Fields?.GetValueOrDefault(NameField) ?? string.Empty;

        var eik = request.Eik.Length > 0
            ? request.Eik
            : request.Fields?.GetValueOrDefault(EikField) ?? string.Empty;

        var (who, pin) = _partners.Complete(companyId, name, eik, PartnerRole.Originator);

        if (who == request.Partner && pin == request.Eik) return request;

        var fields = new Dictionary<string, string>(
            request.Fields ?? new Dictionary<string, string>(), StringComparer.Ordinal);

        if (who.Length > 0) fields[NameField] = who;
        if (pin.Length > 0) fields[EikField] = pin;

        return request with { Partner = who, Eik = pin, Fields = fields };
    }

    /// <summary>
    /// Изравнява договора със списъка и научава новия — преди да бъде записан.
    ///
    /// Тук, а не в прозорците, по същата причина като доставчиците: предаване
    /// се записва от прозореца, от таблицата на месеца и от поправка, и всички
    /// минават оттук. Написаното с други главни букви или с излишни интервали
    /// става такова, каквото е в списъка: „договор 12“ и „Договор  12“ са един
    /// договор и по него трябва да се пресява като по един.
    ///
    /// Научава се само стойност, НОВА за движението. Таблицата на месеца
    /// пренаписва всичките си редове при всеки запис; иначе договор, махнат от
    /// „Настройки“, защото е изтекъл, щеше да се връща с първото записване на
    /// стар месец.
    /// </summary>
    private MoveRequest Contracted(long companyId, MoveRequest request)
    {
        // Кой вид изобщо носи договор, решава самият запис — там са и другите
        // такива правила (LedgerStore.Bind). Тук се пита само дали има какво да
        // се научи: чужд вид не бива да пълни списъка.
        if (!Moves.HasContract(request.Kind) || _partners is null) return request;

        var typed = PartnerStore.Tidy(request.Contract);

        if (typed.Length == 0) return request with { Contract = string.Empty };

        var (same, _) = _partners.Complete(companyId, typed, string.Empty, PartnerRole.Contract);

        var was = request.MoveId is { } id ? _ledger.Contract(id) : string.Empty;

        if (!string.Equals(was, same, StringComparison.Ordinal))
            _partners.Remember(companyId, same, string.Empty, PartnerRole.Contract);

        return request with { Contract = same };
    }

    /// <summary>
    /// Записва движението и връща номера му.
    ///
    /// Подаден <see cref="MoveRequest.MoveId"/> значи поправка на вече записано:
    /// то се пренаписва на място, а при приемане и партидата му се обновява,
    /// вместо да се заведе втора. Иначе всяка поправка на печатна грешка щеше да
    /// ражда нова партида.
    /// </summary>
    public long Write(long companyId, MoveRequest request)
    {
        if (request.Kind == MoveKind.Received) request = Complete(companyId, request);

        request = Contracted(companyId, request);

        var draws = request.Draws is { Count: > 0 }
            ? request.Draws
            : Allocate(companyId, request);

        // Приемането е самата партида: тя се завежда преди движението и то сочи
        // изцяло към нея.
        if (request.Kind == MoveKind.Received)
        {
            draws = [new Draw(Batch(companyId, request), request.Amount)];

            _partners?.Remember(companyId, request.Partner, request.Eik, PartnerRole.Originator);
        }

        var draft = new MoveDraft(
            request.Kind,
            request.Date,
            request.Ewc,
            request.Amount,
            request.Stagenum,
            Opening: false,
            Direct: request.Direct,
            Note: request.Note,
            Fields: request.Fields,
            Draws: draws,
            EwcOut: request.EwcOut,
            Contract: request.Contract);

        if (request.MoveId is { } id)
        {
            _ledger.Update(id, draft);
            return id;
        }

        return _ledger.Add(companyId, draft);
    }

    /// <summary>
    /// Маха движение и партидата му, ако към нея вече нищо не сочи.
    ///
    /// Партида, от която е черпено, остава: тя носи произхода на истинска
    /// работа. Тогава махнатото приемане си личи като недостиг — по-добре
    /// видим недостиг, отколкото тихо изтрита следа.
    /// </summary>
    public void Remove(long companyId, long moveId) => Remove(companyId, [moveId]);

    /// <summary>
    /// Същото за няколко движения наведнъж — цяло действие, всичко или нищо.
    ///
    /// Дали нещо след тях виси на тях, се пита ПРЕДИ това
    /// (<see cref="Undo.Takers"/>): тук само се маха.
    /// </summary>
    public void Remove(long companyId, IReadOnlyCollection<long> moveIds)
    {
        var mine = moveIds
            .SelectMany(id => _ledger.Draws(id))
            .Select(d => d.BatchId)
            .Distinct()
            .ToList();

        _ledger.Remove(moveIds);

        var used = _ledger.Links(companyId).Select(l => l.BatchId).ToHashSet();

        foreach (var batchId in mine)
        {
            if (used.Contains(batchId)) continue;
            if (_batches.Find(batchId) is not { Opening: false }) continue;

            _batches.Remove(batchId);
        }
    }

    // ------------------------------------------------------------- помощни

    /// <summary>Партидата на едно приемане — нова или обновената на място.</summary>
    private long Batch(long companyId, MoveRequest request)
    {
        var draft = new BatchDraft(
            request.Eik,
            request.Partner,
            request.Ewc,
            request.Date,
            request.Stagenum,
            request.Document,
            Fields: request.Fields);

        if (request.MoveId is { } id &&
            _ledger.Draws(id).Select(d => (long?)d.BatchId).FirstOrDefault() is { } existing &&
            _batches.Find(existing) is { Opening: false })
        {
            _batches.Update(existing, draft);
            return existing;
        }

        return _batches.Add(companyId, draft);
    }

    /// <summary>
    /// Как се разпределя движение, за което никой не е казал друго.
    ///
    /// Излизащото черпи по FIFO от своя склад към датата си; образуваното
    /// наследява дяловете на онова, което ЧАКА в склад „Третиран“. И в двата
    /// случая самото движение се изважда от сметката, когато се поправя: иначе
    /// собствените му дялове биха се броили за чужди.
    ///
    /// Образуваното над чакащото няма произход и не получава такъв. То е
    /// собствен отпадък на площадката — приписан на чужди партиди, той би
    /// обявил за нечие количество, което никой не е донасял.
    /// </summary>
    private IReadOnlyList<Draw> Allocate(long companyId, MoveRequest request)
    {
        var moves = _ledger.Read(companyId, request.Stagenum)
            .Where(m => m.Id != request.MoveId)
            .ToList();

        var links = _ledger.Links(companyId, request.Stagenum)
            .Where(l => l.MoveId != request.MoveId)
            .ToList();

        var batches = _batches.List(companyId, request.Stagenum);

        // Рециклираното наследява като образуваното: и то излиза от „Третиран“.
        if (request.Kind is MoveKind.Generated or MoveKind.Recycled)
        {
            var waiting = StockMath.Waiting(batches, moves, links, request.Date);
            var pool = SplitMath.Round(waiting.Draws.Sum(d => d.Amount));

            return Allocation.Inherit(waiting.Draws, Math.Min(SplitMath.Round(request.Amount), pool));
        }

        if (Moves.From(request.Kind, request.Direct) is not { } store) return [];

        var stock = StockMath.Batches(batches, moves, links, store, request.Date);

        return Allocation.Fifo(StockMath.Available(stock, request.Ewc), request.Amount).Draws;
    }

}
