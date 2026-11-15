namespace RiosvImports.Core.Balance;

/// <summary>
/// Какво да се оцветява в таблицата.
///
/// Флагове, а не отделни свойства: изборът се пази в настройките като едно
/// число и се предава на изнасянето наведнъж. Всяко оцветяване отговаря на
/// въпрос, който човек иначе задава наум, ред по ред.
/// </summary>
[Flags]
public enum BalanceMark
{
    None = 0,

    /// <summary>Клетките, до които стига включената незавършена книга.</summary>
    Draft = 1,

    /// <summary>Отрицателни стойности — предадено повече, отколкото е налично.</summary>
    Negative = 2,

    /// <summary>Топлинна карта: колкото по-голямо количество, толкова по-плътен фон.</summary>
    Heat = 4,

    /// <summary>Месецът с най-голямо движение по този код.</summary>
    Peak = 8,

    /// <summary>Ред с начално количество и нито едно движение — залежал отпадък.</summary>
    Idle = 16
}

/// <summary>С какъв смисъл е оцветена една клетка. Едно оцветяване на клетка.</summary>
public enum BalanceTint
{
    None,
    Draft,
    Negative,
    Peak,
    Heat
}

/// <summary>
/// Изборът за оцветяване плюс това, което само таблицата не знае — кои клетки
/// идват от незавършената книга.
/// </summary>
public sealed record BalanceHighlight
{
    public BalanceMark Marks { get; init; } = BalanceMark.Draft | BalanceMark.Negative;

    /// <summary>Клетките, докоснати от незавършената книга: код и месец (1–12).</summary>
    public IReadOnlySet<(string Ewc, int Month)> Draft { get; init; } =
        new HashSet<(string, int)>();

    public bool Has(BalanceMark mark) => (Marks & mark) != 0;

    /// <summary>Нищо за оцветяване — ползва се, за да се спести обиколка.</summary>
    public bool IsEmpty => Marks == BalanceMark.None;

    public static readonly BalanceHighlight Off = new() { Marks = BalanceMark.None };
}

/// <summary>
/// Решава с какъв цвят се рисува всяка клетка.
///
/// Стои извън екрана по същата причина, по която и сметката: същите цветове
/// трябва да излязат и в изнесения файл. Ако правилото живееше в рисуването
/// на таблицата, Excel щеше да получи или нищо, или второ мнение.
///
/// Едно оцветяване на клетка, в подредба по важност: отрицателното е грешка и
/// бие всичко; докоснатото от чернова е предупреждение „това още ще се мени“;
/// върхът и топлината са четиво, не тревога.
/// </summary>
public sealed class BalanceMarking
{
    private readonly BalanceHighlight _highlight;
    private readonly bool _running;
    private readonly decimal _largest;

    public BalanceMarking(BalanceTable table, BalanceHighlight highlight, bool running)
    {
        _highlight = highlight;
        _running = running;

        // Топлината се мери спрямо най-голямото в самата таблица: „много“ е
        // различно при площадка с петдесет тона и при площадка с пет.
        _largest = table.Lines
            .SelectMany(line => running ? line.Running : line.Movement)
            .Select(Math.Abs)
            .DefaultIfEmpty(0m)
            .Max();
    }

    /// <summary>Дали изобщо има какво да се оцветява.</summary>
    public bool IsEmpty => _highlight.IsEmpty;

    /// <summary>Ред само с начално количество — нищо не е мръднало цяла година.</summary>
    public bool Idle(BalanceLine line) =>
        _highlight.Has(BalanceMark.Idle) &&
        line.Opening != 0 &&
        line.Movement.All(m => m == 0);

    /// <summary>Цветът на клетка от месец (1–12) и колко плътен да е при топлина.</summary>
    public BalanceTint Tint(BalanceLine line, int month, out double heat)
    {
        var value = _running ? line.Running[month - 1] : line.Movement[month - 1];

        return Decide(line, value, Touched(line.Ewc, month), Peak(line, month), out heat);
    }

    /// <summary>Цветът на последната колона — сборът или салдото в края.</summary>
    public BalanceTint Total(BalanceLine line, out double heat)
    {
        var value = _running ? line.Closing : line.Total;

        // Сборът е докоснат, ако е докоснат кой да е месец от реда: черновата
        // променя и него.
        var touched = Enumerable.Range(1, BalanceMath.Months).Any(m => Touched(line.Ewc, m));

        return Decide(line, value, touched, peak: false, out heat);
    }

    private BalanceTint Decide(BalanceLine line, decimal value, bool touched, bool peak, out double heat)
    {
        heat = 0;

        if (_highlight.Has(BalanceMark.Negative) && value < 0) return BalanceTint.Negative;
        if (_highlight.Has(BalanceMark.Draft) && touched) return BalanceTint.Draft;
        if (peak && _highlight.Has(BalanceMark.Peak)) return BalanceTint.Peak;

        if (!_highlight.Has(BalanceMark.Heat) || _largest == 0 || value == 0) return BalanceTint.None;

        heat = (double)(Math.Abs(value) / _largest);

        return BalanceTint.Heat;
    }

    /// <summary>
    /// Докосва ли незавършената книга тази клетка.
    ///
    /// В салдото докоснатото се носи напред: щом черновата е променила март,
    /// от март нататък всяко салдо е друго.
    /// </summary>
    private bool Touched(string ewc, int month)
    {
        if (_highlight.Draft.Count == 0) return false;

        if (!_running) return _highlight.Draft.Contains((ewc, month));

        for (var m = 1; m <= month; m++)
            if (_highlight.Draft.Contains((ewc, m))) return true;

        return false;
    }

    /// <summary>Месецът с най-голямо по стойност движение — само в движението.</summary>
    private bool Peak(BalanceLine line, int month)
    {
        if (_running) return false;

        var largest = line.Movement.Select(Math.Abs).DefaultIfEmpty(0m).Max();
        if (largest == 0) return false;

        // При равни се маркира първият: две еднакви „най-големи“ клетки водят
        // окото към нищо.
        return Math.Abs(line.Movement[month - 1]) == largest &&
               line.Movement.Take(month - 1).All(m => Math.Abs(m) != largest);
    }
}
