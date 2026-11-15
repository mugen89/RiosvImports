using System.Globalization;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Кое от движенията е това.
///
/// Първите четири са същите четири раздела, които отчетната книга иска — но тук
/// те не са „листове“, а движения: нещо влиза някъде или излиза оттам. Разделът
/// е изгледът, движението е случката. Петото — рециклирането — раздел в книгата
/// няма: то изважда образувано от сметката (<see cref="Moves.InBook"/>).
/// </summary>
public enum MoveKind
{
    /// <summary>Получен отпадък — влиза в склад „Получен“ и създава партида.</summary>
    Received,

    /// <summary>Третиран отпадък — излиза от склад „Получен“.</summary>
    Treated,

    /// <summary>Образуван отпадък — влиза в склад „Образуван“.</summary>
    Generated,

    /// <summary>Предаден отпадък — излиза от склад „Образуван“.</summary>
    Submitted,

    /// <summary>
    /// Рециклиран отпадък — излиза от склад „Третиран“ като образуваното, но
    /// не влиза в отчета.
    ///
    /// Това е образуван отпадък, който площадката изважда от сметката: той е
    /// излязъл от третирането — и затова изчерпва „Третиран“ по същите правила
    /// и наследява произхода по партиди, — но в книгата не се обявява и в
    /// „Образуван“ не стои, за да бъде предаден. Отива в свой склад-летопис,
    /// който само расте, както „Предаден“.
    ///
    /// Стои ПОСЛЕДЕН в изброяването: видът се пази в базата като текст
    /// („recycled“), а мястото му тук не мести номерата на заварените четири.
    /// </summary>
    Recycled
}

/// <summary>
/// Кой от четирите склада.
///
/// Три от тях са наличност: каквото стои на площадката в едно или друго
/// състояние. „Третиран“ дълго време минаваше за дневник — записва какво е
/// минало, а не какво стои. Оказа се невярно: третираното количество стои
/// някъде между двете — вече не е полученото, но още не е образуваното — и
/// докато не се обяви какво е излязло от него, то е наличност без изглед.
///
/// Четвъртият, „Предаден“, е друго животно и това трябва да се знае: той е
/// ЛЕТОПИС, а не наличност. Предаденото си е отишло от площадката и нищо не го
/// изчерпва, затова числото му само расте. Стои тук, защото въпросът „какво сме
/// предали, на кого и чие е било“ се задава със същата честота като „какво още
/// стои“ — и се отговаря със същата сметка, през същите партиди.
/// </summary>
public enum StoreKind
{
    /// <summary>Каквото е прието и още не е третирано.</summary>
    Received,

    /// <summary>Каквото е третирано и още не е обявено като образувано.</summary>
    Treated,

    /// <summary>Каквото е образувано и още не е предадено.</summary>
    Formed,

    /// <summary>Каквото е предадено — излязло от площадката и вече само расте.</summary>
    Submitted,

    /// <summary>
    /// Каквото е рециклирано — извадено от сметката на отчета.
    ///
    /// Летопис като „Предаден“: нищо не излиза от него и числото му само расте.
    /// Отговаря на „колко сме изкарали от третираното, без да го обявяваме“.
    /// </summary>
    Recycled
}

/// <summary>
/// Кой склад пипа движението и в коя посока.
///
/// Тази таблица е цялата връзка между четирите движения и двата склада. Всяка
/// сметка надолу — наличност, пренасяне, контроли — минава оттук и никъде
/// другаде не се пише „полученият се събира, третираният се вади“.
/// </summary>
public static class Moves
{
    /// <summary>
    /// Четирите движения НА КНИГАТА, в реда, в който отпадъкът минава през
    /// площадката.
    ///
    /// Рециклирането не е сред тях нарочно: то няма раздел в отчета. Всичко,
    /// което върви по книгата — внасяне, шаблони, таблицата на месеца по вид,
    /// отключването в „Настройки“ — чете този списък и не бива да го срещне.
    /// Където трябват всички движения, има <see cref="Every"/>.
    /// </summary>
    public static readonly IReadOnlyList<MoveKind> All =
    [
        MoveKind.Received, MoveKind.Treated, MoveKind.Generated, MoveKind.Submitted
    ];

    /// <summary>
    /// Всички движения, които складът познава — с рециклирането.
    ///
    /// За сметките и изгледите, които питат за ВСЯКО движение: сборовете на
    /// месеца, ситото по вид. Рециклираното изчерпва склад „Третиран“ и не бива
    /// да изчезва от сбор, който казва къде е отишло третираното.
    /// </summary>
    public static readonly IReadOnlyList<MoveKind> Every =
    [
        .. All, MoveKind.Recycled
    ];

    /// <summary>
    /// Складовете в реда, в който отпадъкът минава през тях.
    ///
    /// Двата летописа — „Предаден“ и „Рециклиран“ — стоят накрая: и двата са
    /// изход от сметката, а не наличност.
    /// </summary>
    public static readonly IReadOnlyList<StoreKind> Stores =
    [
        StoreKind.Received, StoreKind.Treated, StoreKind.Formed, StoreKind.Submitted, StoreKind.Recycled
    ];

    /// <summary>
    /// Складовете, които са наличност — в тях нещо СТОИ.
    ///
    /// „Предаден“ не е сред тях: той е летопис на излязлото и не участва в
    /// сметките, които питат какво още е на площадката.
    /// </summary>
    public static readonly IReadOnlyList<StoreKind> Stocks =
    [
        StoreKind.Received, StoreKind.Treated, StoreKind.Formed
    ];

    /// <summary>Дали складът е наличност, или летопис на излязлото.</summary>
    public static bool IsStock(StoreKind store) =>
        store is StoreKind.Received or StoreKind.Treated or StoreKind.Formed;

    /// <summary>
    /// Дали движението влиза в отчетната книга.
    ///
    /// Рециклираното не влиза — точно това го отличава от образуваното. Едно
    /// място за правилото: отчетът и балансът го питат оттук, вместо всеки да
    /// помни сам кой вид да пропусне.
    /// </summary>
    public static bool InBook(MoveKind kind) => kind != MoveKind.Recycled;

    /// <summary>
    /// От кой склад ВАДИ движението. Празно значи „отникъде“.
    ///
    /// Не всичко минава през третиране. Площадка за събиране приема опаковки от
    /// стотина контрагента и ги предава почти както са дошли — предаденото там
    /// излиза от склад „Получен“, защото никога не е влизало в „Образуван“.
    /// Флагът важи само за предаването; за другите три вида няма какво да се
    /// избира.
    /// </summary>
    public static StoreKind? From(MoveKind kind, bool direct = false) => kind switch
    {
        MoveKind.Treated => StoreKind.Received,
        MoveKind.Generated => StoreKind.Treated,
        MoveKind.Recycled => StoreKind.Treated,
        MoveKind.Submitted => direct ? StoreKind.Received : StoreKind.Formed,
        _ => null
    };

    /// <summary>
    /// В кой склад ВЛИЗА движението. Празно значи „никъде“.
    ///
    /// Предаването влиза в склад „Предаден“ и не излиза от него никога: там
    /// няма движение, което да вади. Затова той расте — и точно това се иска от
    /// него, защото отговаря на „какво е излязло досега“, а не на „какво стои“.
    /// </summary>
    public static StoreKind? To(MoveKind kind) => kind switch
    {
        MoveKind.Received => StoreKind.Received,
        MoveKind.Treated => StoreKind.Treated,
        MoveKind.Generated => StoreKind.Formed,
        MoveKind.Recycled => StoreKind.Recycled,
        _ => StoreKind.Submitted
    };

    /// <summary>Дали видът изобщо може да е пряк.</summary>
    public static bool CanBeDirect(MoveKind kind) => kind == MoveKind.Submitted;

    /// <summary>
    /// Дали видът носи договор.
    ///
    /// Само предаването: договорът е с онзи, който взема отпадъка. Полето е
    /// наше, не на схемата, и в отчета към НИСО не отива.
    /// </summary>
    public static bool HasContract(MoveKind kind) => kind == MoveKind.Submitted;

    /// <summary>
    /// Как се казва полето на екрана.
    ///
    /// На едно място, защото се чете от седем екрана: смяната на думата е смяна
    /// на един ред, а не търсене по прозорците.
    /// </summary>
    public static string ContractTitle => Lang.T("Договор", "Contract");

    /// <summary>
    /// Дали видът може да влезе в книгата с ДРУГ код, а не с този, по който
    /// пипа склада.
    ///
    /// Двете излизащи движения могат. При третирането това е всекидневно:
    /// влиза кабел 160216, в книгата стои мед 191202. При предаването е рядко,
    /// но е същото: отпадък, заведен при нас под един код, се приема от
    /// оползотворителя под друг, и в книгата трябва да стои неговият.
    ///
    /// Приемането и образуването не могат: те ВЛИЗАТ в склад и вторият код там
    /// би бил измислен — няма кой да го е поискал.
    ///
    /// Правилото стои тук, до <see cref="From"/> и <see cref="To"/>, а не в
    /// записа: там то беше едно „ако е третиране“, което мълчаливо изхвърляше
    /// кода на предаването (правило 12а).
    /// </summary>
    public static bool CanRename(MoveKind kind) => kind is MoveKind.Treated or MoveKind.Submitted;

    /// <summary>
    /// С какъв знак движението влиза в наличността на ТОЗИ склад.
    ///
    /// Нула значи „не го пипа“. Това е цялата връзка между четирите движения и
    /// трите склада — всяка сметка надолу минава оттук и никъде другаде не се
    /// пише „третираното се вади от полученото“.
    /// </summary>
    public static int Sign(MoveKind kind, StoreKind store, bool direct = false) =>
        To(kind) == store ? 1 : From(kind, direct) == store ? -1 : 0;

    /// <summary>
    /// Дали движението си избира покритието по FIFO от склада, от който вади.
    ///
    /// Образуваното също вади от склад — „Третиран“ — но покритието му не се
    /// избира: то наследява дяловете на третиранията, които още чакат, в тяхното
    /// съотношение. Затова тук го няма.
    /// </summary>
    public static bool Draws(MoveKind kind) => kind is MoveKind.Treated or MoveKind.Submitted;

    /// <summary>
    /// Името на възела в схемата — връзката към отчетната книга.
    ///
    /// Рециклирането няма възел и връща празно: в книгата то няма раздел. Нито
    /// един възел на схемата не съвпада с празното, затова <see cref="Of"/> не
    /// може да го намери по погрешка.
    /// </summary>
    public static string Node(MoveKind kind) => kind switch
    {
        MoveKind.Received => "r_b_rd_received",
        MoveKind.Treated => "r_b_rd_treated",
        MoveKind.Generated => "r_b_rd_generated",
        MoveKind.Submitted => "r_b_rd_submitted",
        _ => string.Empty
    };

    /// <summary>Кое движение е това — null, ако възелът не е от книгата.</summary>
    public static MoveKind? Of(string node) =>
        All.Select(k => (MoveKind?)k).FirstOrDefault(k => Node(k!.Value) == node);

    /// <summary>
    /// Как се казва движението на екрана.
    ///
    /// Метод, а не готов списък: статично поле се пълни веднъж, с езика, който
    /// е бил в сила тогава, и после не се мени при смяна на езика (правило 8).
    /// </summary>
    public static string Title(MoveKind kind) => kind switch
    {
        MoveKind.Received => Lang.T("Получен", "Received"),
        MoveKind.Treated => Lang.T("Третиран", "Treated"),
        MoveKind.Generated => Lang.T("Образуван", "Generated"),
        MoveKind.Recycled => Lang.T("Рециклиран", "Recycled"),
        _ => Lang.T("Предаден", "Submitted")
    };

    /// <summary>
    /// Краткото име на склада — за ситото, където мястото е на цена.
    ///
    /// Пълното („Склад „Получен““) казва кое е това, когато стои само; в реда на
    /// трите думата „склад“ се повтаря три пъти и не добавя нищо.
    /// </summary>
    public static string Short(StoreKind store) => store switch
    {
        StoreKind.Received => Lang.T("Получен", "Received"),
        StoreKind.Treated => Lang.T("Третиран", "Treated"),
        StoreKind.Formed => Lang.T("Образуван", "Formed"),
        StoreKind.Recycled => Lang.T("Рециклиран", "Recycled"),
        _ => Lang.T("Предаден", "Handed over")
    };

    /// <summary>Как се казва складът на екрана.</summary>
    public static string Title(StoreKind store) => store switch
    {
        StoreKind.Received => Lang.T("Склад „Получен“", "“Received” store"),
        StoreKind.Treated => Lang.T("Склад „Третиран“", "“Treated” store"),
        StoreKind.Formed => Lang.T("Склад „Образуван“", "“Formed” store"),
        StoreKind.Recycled => Lang.T("Рециклирано", "Recycled"),
        _ => Lang.T("Предадено", "Handed over")
    };
}

/// <summary>
/// Връзката към отчетната книга за оползотворяване.
///
/// Складът и отчетът говорят за едно и също нещо с различни думи: движение и
/// ред. Тук стоят имената, които и двете страни трябва да знаят — и нищо
/// повече, защото останалото се чете от схемата (правило 1).
/// </summary>
public static class BookNodes
{
    /// <summary>Отчетът, който складът съставя.</summary>
    public const string Report = "r_b_recovery_dispose";

    /// <summary>
    /// Колоните, които складът води сам.
    ///
    /// Датата, кодът и количеството са самото движение, а площадката е обхватът
    /// му. Те имат свои полета и своя сметка; всичко останало на възела е на
    /// схемата и се сглобява от нея, за да се появи само, когато НИСО добави
    /// колона.
    /// </summary>
    public static readonly IReadOnlySet<string> Owned =
        new HashSet<string>(StringComparer.Ordinal) { "date_record", "ewc", "amount_ton", "stagenum" };
}

/// <summary>
/// Отчетният период е месец.
///
/// Собствен тип, а не двойка числа: месецът се сравнява, увеличава и изписва
/// на десетки места, и всяко от тях иначе би носило по едно „ако е декември,
/// годината става следващата“.
///
/// Изписва се като „2026-09“ — подредбата по азбучен ред съвпада с подредбата
/// по време, затова в базата се пази точно този текст и се пресява с обикновено
/// сравнение.
/// </summary>
public readonly record struct LedgerPeriod(int Year, int Month) : IComparable<LedgerPeriod>
{
    /// <summary>Периодът, в който попада датата.</summary>
    public static LedgerPeriod Of(DateOnly date) => new(date.Year, date.Month);

    /// <summary>Разчита „2026-09“. Приема и „2026-9“, защото се пише и на ръка.</summary>
    public static bool TryParse(string? text, out LedgerPeriod period)
    {
        period = default;

        var parts = text?.Trim().Split('-');
        if (parts is not { Length: 2 }) return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)) return false;

        if (year is < 1990 or > 2100 || month is < 1 or > 12) return false;

        period = new LedgerPeriod(year, month);
        return true;
    }

    /// <summary>Следващият месец — тук живее преходът през декември.</summary>
    public LedgerPeriod Next => Month == 12 ? new(Year + 1, 1) : new(Year, Month + 1);

    /// <summary>Предходният месец.</summary>
    public LedgerPeriod Previous => Month == 1 ? new(Year - 1, 12) : new(Year, Month - 1);

    /// <summary>Първият ден от месеца.</summary>
    public DateOnly First => new(Year, Month, 1);

    /// <summary>Последният ден от месеца.</summary>
    public DateOnly Last => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    /// <summary>Дали датата пада в този месец.</summary>
    public bool Covers(DateOnly date) => date.Year == Year && date.Month == Month;

    /// <summary>Тримесечието, в което пада месецът — НИСО подава по тримесечия.</summary>
    public int Quarter => (Month - 1) / 3 + 1;

    public int CompareTo(LedgerPeriod other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Month.CompareTo(other.Month);

    public static bool operator <(LedgerPeriod a, LedgerPeriod b) => a.CompareTo(b) < 0;
    public static bool operator >(LedgerPeriod a, LedgerPeriod b) => a.CompareTo(b) > 0;
    public static bool operator <=(LedgerPeriod a, LedgerPeriod b) => a.CompareTo(b) <= 0;
    public static bool operator >=(LedgerPeriod a, LedgerPeriod b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Year:D4}-{Month:D2}";

    /// <summary>Как се чете на екрана: „септември 2026“.</summary>
    public string Title() => $"{MonthName(Month)} {Year}";

    /// <summary>Съкратеното име на месеца — заглавие на колона.</summary>
    public static string Short(int month) => month switch
    {
        1 => Lang.T("яну", "Jan"),
        2 => Lang.T("фев", "Feb"),
        3 => Lang.T("мар", "Mar"),
        4 => Lang.T("апр", "Apr"),
        5 => Lang.T("май", "May"),
        6 => Lang.T("юни", "Jun"),
        7 => Lang.T("юли", "Jul"),
        8 => Lang.T("авг", "Aug"),
        9 => Lang.T("сеп", "Sep"),
        10 => Lang.T("окт", "Oct"),
        11 => Lang.T("ное", "Nov"),
        _ => Lang.T("дек", "Dec")
    };

    private static string MonthName(int month) => month switch
    {
        1 => Lang.T("януари", "January"),
        2 => Lang.T("февруари", "February"),
        3 => Lang.T("март", "March"),
        4 => Lang.T("април", "April"),
        5 => Lang.T("май", "May"),
        6 => Lang.T("юни", "June"),
        7 => Lang.T("юли", "July"),
        8 => Lang.T("август", "August"),
        9 => Lang.T("септември", "September"),
        10 => Lang.T("октомври", "October"),
        11 => Lang.T("ноември", "November"),
        _ => Lang.T("декември", "December")
    };
}

/// <summary>
/// Партида: количество, прието наведнъж от един клиент по един код.
///
/// Партидата е носителят на произхода. Договорните отношения искат за всеки
/// предаден тон да се каже чий е бил; отговорът се пази оттук, а не се гадае
/// после по дати и количества.
///
/// Един приемен ред прави една партида. Не една на клиент: същият клиент носи
/// два кода в един ден и те се третират поотделно. Не една на клиент и код:
/// две доставки в различни дни се изчерпват в различен ред, а протоколът, с
/// който е дошла всяка, е различен.
/// </summary>
public sealed record Batch(
    long Id,
    string Code,
    string Eik,
    string PartnerName,
    string Ewc,
    DateOnly ReceivedOn,
    string Document = "",
    bool Opening = false,
    string Stagenum = "1")
{
    /// <summary>Как се показва на човека: „#2026-0042 · Метал ООД“.</summary>
    public string Display => PartnerName.Length == 0 ? $"#{Code}" : $"#{Code} · {PartnerName}";

    /// <summary>Клиентът с ЕИК-а — за списъци и за пресяване.</summary>
    public string Partner => PartnerName.Length == 0 ? Eik : $"{PartnerName} — {Eik}";

    /// <summary>
    /// Мястото на партида, която я няма — количество без посочен произход.
    ///
    /// Номер нула е свободен: истинските номера идват от базата и почват от
    /// едно. Изгледът „Поотделно“ не бива да крие движение само защото не сочи
    /// партида.
    /// </summary>
    public static Batch None(string ewc, DateOnly on) =>
        new(0, string.Empty, string.Empty, string.Empty, ewc, on);

    /// <summary>Дали това е мястото на липсваща партида, а не истинска партида.</summary>
    public bool IsNone => Id == 0;
}

/// <summary>
/// Едно движение, сведено до онова, което сметката гледа.
///
/// Останалите колони от схемата (категория НУБА, основание, дейност R/D) не
/// стигат дотук: те пътуват до отчета, но не участват в нито една сметка.
/// Записът им е JSON в базата — колоните идват от схемата на НИСО и се менят
/// без наше участие.
///
/// <paramref name="Opening"/> отбелязва началното количество. То е движение
/// като всяко друго — така наличността се смята с една и съща сметка от първия
/// ден — но в отчета не влиза: то е заварено състояние, а не приемане.
/// </summary>
public sealed record Move(
    long Id,
    MoveKind Kind,
    DateOnly Date,
    string Ewc,
    decimal Amount,
    long? BatchId = null,
    string Stagenum = "1",
    bool Opening = false,
    bool Direct = false,
    string EwcOut = "")
{
    /// <summary>
    /// Кодът, с който движението влиза в отчета.
    ///
    /// Обикновено е самият <see cref="Ewc"/> — кодът, по който движението пипа
    /// склада. При третиране обаче двата може да се разминат: от склад
    /// „Получен“ излиза приетият код, а в книгата се записва кодът, под който
    /// отпадъкът е третиран. Празно значи „същият“, а не „никакъв“: така
    /// заварените движения остават каквито са били.
    /// </summary>
    public string Reported => EwcOut.Length > 0 ? EwcOut : Ewc;

    /// <summary>С какъв знак движението влиза в наличността на този склад.</summary>
    public int Sign(StoreKind store) => Moves.Sign(Kind, store, Direct);

    /// <summary>Количеството със знака, с който влиза в наличността на този склад.</summary>
    public decimal Signed(StoreKind store) => Sign(store) * Amount;

    /// <summary>Дали движението изобщо пипа този склад.</summary>
    public bool Touches(StoreKind store) => Sign(store) != 0;

    /// <summary>Месецът, в който пада движението.</summary>
    public LedgerPeriod Period => LedgerPeriod.Of(Date);
}

/// <summary>
/// Връзката между движение и партида: този дял от движението е от тази партида.
///
/// Едно движение пипа няколко партиди — третиране, което черпи от три доставки
/// — затова връзката е отделен запис, а не поле върху самото движение. Поле би
/// побрало само първата от трите.
///
/// Връзката е една и съща за четирите движения и точно затова произходът се
/// проследява без изключения: полученото и образуваното добавят към партидата,
/// третираното и предаденото вадят от нея, а знакът идва от вида на
/// движението (<see cref="Moves.Sign"/>). Образуваното наследява дяловете на
/// третирането, което го е произвело — оттам произходът минава през целия път.
/// </summary>
public sealed record LedgerLink(long MoveId, long BatchId, decimal Amount);

/// <summary>
/// Наличното по една партида към даден момент.
///
/// Три числа, а не едно: човекът, който гледа склада, пита и „колко е дошло“,
/// и „колко е останало“, а разликата между тях е отговорът на „какво стана с
/// него“.
/// </summary>
public sealed record BatchStock(Batch Batch, decimal Received, decimal Consumed)
{
    /// <summary>Каквото още стои в склада по тази партида.</summary>
    public decimal Available => Received - Consumed;

    /// <summary>Изчерпана партида — не се предлага при разпределяне.</summary>
    public bool IsEmpty => Available <= 0;
}

/// <summary>
/// Наличност по код и по партида: този код, толкова, с този произход.
///
/// Двете заедно, защото поотделно не отговарят на въпроса. Кодът казва какво
/// има на площадката; партидата — чие е. В склад „Образуван“ те дори не си
/// съвпадат: партидата носи кода, с който отпадъкът е ДОШЪЛ (кабел 160216), а
/// редът — кода, с който е ИЗЛЯЗЪЛ след третирането (мед 191202). Точно в това
/// разминаване живее проследимостта.
/// </summary>
public sealed record StockShare(string Ewc, Batch Batch, decimal Amount);

/// <summary>
/// Заварено количество: какво стои на площадката в мига, преди програмата да
/// започне да води склада.
///
/// Обявява се, а не се извежда — преди началния месец няма движения, от които
/// да излезе. Затова и началният месец е настройка: „водим склада от септември“
/// значи „това са наличностите на 31 август“.
///
/// Клиентът е по избор. За заварено количество често не се знае от кого е
/// дошло, а измисленият произход е по-лош от липсващия: той изглежда като
/// отговор.
/// </summary>
public sealed record OpeningEntry(
    StoreKind Store,
    string Ewc,
    decimal Amount,
    string Eik = "",
    string PartnerName = "",
    string Document = "",
    string Stagenum = "1");

/// <summary>Обявено начално количество и какво е станало с него оттогава.</summary>
public sealed record OpeningStock(Batch Batch, StoreKind Store, decimal Amount, decimal Consumed)
{
    /// <summary>Каквото от заварената наличност още стои.</summary>
    public decimal Available => Amount - Consumed;

    /// <summary>Дали от него вече е черпено — тогава не се маха безнаказано.</summary>
    public bool InUse => Consumed > 0;
}

/// <summary>
/// Една стъпка от пътя на партидата: това движение е взело (или е добавило)
/// толкова от нея.
/// </summary>
public sealed record TrailStep(Move Move, decimal Amount)
{
    /// <summary>Дялът със знака, с който стъпката пипа този склад.</summary>
    public decimal Signed(StoreKind store) => Move.Sign(store) * Amount;
}

/// <summary>
/// Целият път на една партида — от приемането ѝ до последното, което е излязло
/// от нея.
///
/// Това е отговорът на въпроса, който клиентът задава по телефона: „какво стана
/// с моите дванайсет тона“. Наличността казва какво още стои; пътят казва къде
/// е отишло останалото, и точно затова той е един запис, а не изглед: събира се
/// от същите връзки, от които се смята и наличността, и двете не могат да се
/// разминат.
/// </summary>
public sealed record Trail(Batch Batch, IReadOnlyList<TrailStep> Steps)
{
    /// <summary>Колко от партидата е минало през този вид движение.</summary>
    public decimal Total(MoveKind kind) =>
        SplitMath.Round(Steps.Where(s => s.Move.Kind == kind).Sum(s => s.Amount));

    /// <summary>Колко от партидата още стои в този склад.</summary>
    public decimal InStore(StoreKind store) =>
        SplitMath.Round(Steps.Sum(s => s.Signed(store)));

    /// <summary>Партида, към която нищо не сочи — приемането ѝ е махнато.</summary>
    public bool IsEmpty => Steps.Count == 0;

    /// <summary>Последното, което се е случило с нея.</summary>
    public DateOnly? LastMoved => Steps.Count == 0 ? null : Steps[^1].Move.Date;
}

/// <summary>
/// Насрещната страна на едно предаване: на кого е излязло и по какъв договор.
///
/// Името и ЕИК-ът идват от схемата (name_receiver, pin_receiver) и не участват в
/// нито една сметка — затова не са в <see cref="Move"/>. Тук са, защото
/// изгледът „По получател“ ги групира, а групирането е сметка.
///
/// <paramref name="Contract"/> и <paramref name="Note"/> са наши полета, не на
/// схемата, и пътуват с тях по същата причина: четат се заедно, с едно питане, и
/// се пресяват заедно. И двете са само за пресяване: в отчета, който се подписва
/// и качва в НИСО, не влизат. В ключа също не влизат — един получател с два
/// договора си остава един получател.
/// </summary>
public sealed record HandParty(string Name, string Eik, string Contract = "", string Note = "")
{
    public static HandParty None => new(string.Empty, string.Empty);

    /// <summary>Дали изобщо е посочен получател.</summary>
    public bool Known => Name.Length > 0 || Eik.Length > 0;

    /// <summary>Ключът, по който се групира: ЕИК-ът, ако го има, иначе името.</summary>
    public string Key => Eik.Length > 0 ? Eik : Name;

    /// <summary>Как се чете на един ред.</summary>
    public string Display => Name.Length == 0 ? Eik : Eik.Length == 0 ? Name : $"{Name} — {Eik}";
}

/// <summary>
/// Един ред от склада — и на кого е отишъл, ако вече е отишъл.
///
/// Трите склада с наличност нямат получател: там нищо не е излязло от
/// площадката. „Предаден“ има, и точно заради него редът съществува: същият код
/// от същата партида може да е излязъл към трима различни оползотворители, а
/// <see cref="StockShare"/> ги събира в едно число и въпросът „на кого“ остава
/// без отговор.
///
/// Обвивка, а не ново поле в дяла: сметките за наличност не бива да научават за
/// колона, която важи за един от четирите склада.
/// </summary>
public sealed record StockLine(
    StockShare Share, HandParty Party, DateOnly? Moved = null, long MoveId = 0)
{
    public static StockLine Of(StockShare share) => new(share, HandParty.None);

    // Препращат, за да четат изгледите реда както четат дял.
    public string Ewc => Share.Ewc;
    public Batch Batch => Share.Batch;
    public decimal Amount => Share.Amount;

    /// <summary>Дали редът знае на кого е излязъл.</summary>
    public bool Handed => Party.Known;

    /// <summary>По какъв договор е излязъл. Празно значи „не е посочен“.</summary>
    public string Contract => Party.Contract;

    /// <summary>Бележката на предаването, ако има такава.</summary>
    public string Note => Party.Note;

    /// <summary>Дали редът е ЕДНО предаване, а не сбор от няколко.</summary>
    public bool Single => MoveId > 0;
}

/// <summary>
/// Дял от количество, произхождащ от една партида.
///
/// Това е отговорът на „чие е това“. Появява се навсякъде, където количество
/// се проследява назад: в склада, в изгледа на предаденото, в справката по
/// клиент.
/// </summary>
public sealed record OriginShare(long BatchId, string Eik, string PartnerName, decimal Amount)
{
    public string Partner => PartnerName.Length == 0 ? Eik : $"{PartnerName} — {Eik}";
}
