using RiosvImports.Core.Schema;

namespace RiosvImports.Core.Values;

/// <summary>
/// Един кратък списък: кои стойности на едно поле да се предлагат.
///
/// Обхватът се пази като три текста, а не като три отделни вида запис:
/// празен <see cref="Report"/> значи „за всички отчети от този вид“, празен
/// <see cref="Node"/> — „за целия отчет“. Така един ред в базата описва и
/// трите нива и правилото по-долу може да ги подрежда, вместо да ги разделя.
///
/// Пазят се само кодовете. Номенклатурата им идва от схемата и НИСО я мени;
/// кодът е това, което не се мени, и това, което влиза в XML-а.
/// </summary>
public sealed record ValueListEntry(
    string Report,
    string Node,
    string Column,
    IReadOnlyList<string> Allowed);

/// <summary>
/// Кратките списъци на една фирма за един вид отчетност.
///
/// Правилото е едно изречение: <b>стеснява се какво се предлага, никога
/// какво се разбира.</b> Затова тук има само <see cref="Offer"/> — списъкът
/// за менюто. Търсенето на код, разчитането на внесена клетка и проверката
/// преди подаване продължават да питат схемата: файл отвън няма как да знае
/// за нечий кратък списък, а мълчаливо отхвърлена стойност е по-лоша от
/// дълго меню.
///
/// Живее в Core и не знае нищо за база и за прозорци — затова се проверява с
/// тест, без нито един прозорец.
/// </summary>
public sealed class ValueLimits
{
    /// <summary>Нищо не е ограничено. Ползва се, когато фирмата е неизвестна.</summary>
    public static readonly ValueLimits None = new([]);

    /// <summary>Ключ: отчет + таблица + поле. Празният текст е „всички“.</summary>
    private readonly Dictionary<(string Report, string Node, string Column), HashSet<string>> _entries = [];

    /// <summary>
    /// Готовите къси менюта, по същия ключ.
    ///
    /// Пази се не заради скоростта на пресмятането, а заради това, че
    /// повикващият пази СВОИ неща по този списък: <see cref="EnumOption"/>
    /// не се мени, а <c>Editor/Codes</c> държи по едно готово падащо меню за
    /// всеки списък и го намира по самия него. Нов списък при всяко питане би
    /// правил ново меню при всяко питане — а точно това Codes съществува да
    /// избегне.
    /// </summary>
    private readonly Dictionary<(string Report, string Node, string Column), IReadOnlyList<EnumOption>> _ready = [];

    /// <summary>Списъците се четат и от нишка, различна от тази на екрана — шаблоните.</summary>
    private readonly object _gate = new();

    /// <summary>
    /// <paramref name="fills"/> е отметката на фирмата: попълва ли се само
    /// полето, което списъкът е свел до една стойност. Пътува заедно със
    /// списъците, а не отделно, защото се пита на същото място и от същия
    /// повикващ — разделени, двете се разминават при първото прозорче, което
    /// е взело едното и е забравило другото.
    /// </summary>
    public ValueLimits(IEnumerable<ValueListEntry> entries, bool fills = false)
    {
        Fills = fills;

        foreach (var entry in entries)
        {
            // Празен списък не е ограничение, а недовършена настройка: ако го
            // спазвахме, полето би останало без нито една стойност.
            if (entry.Allowed.Count == 0) continue;

            _entries[(entry.Report, entry.Node, entry.Column)] =
                new HashSet<string>(entry.Allowed, StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool IsEmpty => _entries.Count == 0;

    /// <summary>Иска ли фирмата да ѝ се попълва останалото едно. Вж. <see cref="Fill"/>.</summary>
    public bool Fills { get; }

    /// <summary>Има ли изобщо кратък списък за това поле — за надписа „избрани 20 от 842“.</summary>
    public bool Restricts(ReportNode report, ReportNode node, ColumnDef column) =>
        Lookup(report.ElementName, node.ElementName, column.Name) is not null;

    /// <summary>
    /// Стойностите, които да се предложат в менюто.
    ///
    /// <paramref name="inUse"/> са стойностите, които вече стоят в редовете.
    /// Те се връщат винаги, дори да са извън списъка: отчет отпреди
    /// ограничението — или внесен от Excel — не бива да си изгуби попълненото,
    /// а клетка с падащо меню показва празно всичко, което не е в менюто ѝ.
    /// </summary>
    public IReadOnlyList<EnumOption> Offer(
        ReportNode report,
        ReportNode node,
        ColumnDef column,
        IEnumerable<string>? inUse = null) =>
        Offer(report.ElementName, node.ElementName, column, inUse);

    /// <summary>
    /// Същото, но по имена. Ползва се там, където подръка е само таблицата:
    /// коренният ѝ отчет е <see cref="ReportNode.Root"/>.
    /// </summary>
    public IReadOnlyList<EnumOption> Offer(
        string report,
        string node,
        ColumnDef column,
        IEnumerable<string>? inUse = null)
    {
        if (!column.Domain.IsEnum) return column.Domain.Options;

        var allowed = Lookup(report, node, column.Name);
        if (allowed is null) return column.Domain.Options;

        // Вече попълненото извън списъка е рядкост и прави списъка различен от
        // общия за обхвата — затова минава отстрани, без да се пази.
        var extra = inUse?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => !allowed.Contains(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (extra is { Count: > 0 })
            return Narrow(column, o => allowed.Contains(o.Value) || extra.Contains(o.Value));

        lock (_gate)
        {
            if (_ready.TryGetValue((report, node, column.Name), out var ready)) return ready;

            ready = Narrow(column, o => allowed.Contains(o.Value));
            _ready[(report, node, column.Name)] = ready;

            return ready;
        }
    }

    /// <summary>
    /// Стойността, с която полето да се попълни само — или null, ако няма такава.
    ///
    /// Смисълът е в думата „останала“: тук не се гадае коя от няколко е вярната,
    /// а се дописва това, което човекът вече е решил. Списък с една стойност
    /// казва „при нас това поле е винаги това“, и всяко попълване след него е
    /// преписване на едно и също.
    ///
    /// Иска се ИЗРИЧЕН кратък списък, а не просто меню с една възможност:
    /// стеснението е решение на фирмата и се вижда в „Списъци“, а едноредното
    /// меню е на схемата — утре НИСО добавя второ и полето мълчаливо почва да
    /// се попълва наслуки.
    ///
    /// Иска се и отметката. Попълнено поле, което човек не е пипал, е изненада
    /// за този, който не я е поискал — а стойността оттам нататък тръгва към
    /// НИСО като негова.
    ///
    /// Пита се от прозорчетата на склада. Редакторът НЕ пита: там се пипат
    /// стотици реда наведнъж и „попълва се само“ значи друго — за него това е
    /// работа на пренасянето и на шаблоните.
    /// </summary>
    public EnumOption? Fill(ReportNode report, ReportNode node, ColumnDef column) =>
        Fill(report.ElementName, node.ElementName, column);

    public EnumOption? Fill(string report, string node, ColumnDef column)
    {
        if (!Fills) return null;
        if (!column.Domain.IsEnum) return null;

        // Без кратък списък няма какво да се е свело: пълното меню с една
        // стойност е на схемата и не е ничие решение.
        if (Lookup(report, node, column.Name) is null) return null;

        var offered = Offer(report, node, column);

        return offered.Count == 1 ? offered[0] : null;
    }

    /// <summary>
    /// Позволява ли списъкът тази стойност.
    ///
    /// Различава се от <see cref="Offer"/> по едно: вече попълненото тук НЕ се
    /// брои за позволено. Точно то се търси — редът, попълнен преди списъка или
    /// с чужд списък, и заради който отчетът не бива да тръгне към НИСО, преди
    /// човек да го е погледнал.
    ///
    /// Празната стойност минава: липсващото задължително поле е друга грешка и
    /// се съобщава от проверката, не оттук.
    /// </summary>
    public bool Allows(ReportNode report, ReportNode node, ColumnDef column, string? value) =>
        Allows(report.ElementName, node.ElementName, column, value);

    public bool Allows(string report, string node, ColumnDef column, string? value)
    {
        if (!column.Domain.IsEnum) return true;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var allowed = Lookup(report, node, column.Name);
        if (allowed is null) return true;

        // Същият предпазител като в Narrow: списък, от който не е останало
        // нищо познато на схемата, не ограничава — иначе би обявил за сгрешен
        // всеки ред в отчета.
        if (!column.Domain.Options.Any(o => allowed.Contains(o.Value))) return true;

        return allowed.Contains(value.Trim());
    }

    /// <summary>
    /// Кой списък спира тази стойност — обхватът, изписан като ключ.
    ///
    /// Ползва се от „Защо липсва“: човек вижда празно място в менюто и трябва
    /// да разбере кой точно запис го е направил, а не просто че е ограничено.
    /// Връща null, когато стойността минава.
    /// </summary>
    public (string Report, string Node)? BlockedBy(string report, string node, ColumnDef column, string? value)
    {
        if (Allows(report, node, column, value)) return null;

        if (_entries.ContainsKey((report, node, column.Name))) return (report, node);
        if (_entries.ContainsKey((report, string.Empty, column.Name))) return (report, string.Empty);

        return (string.Empty, string.Empty);
    }

    /// <summary>
    /// Свива менюто на едно поле.
    ///
    /// Списък, който не оставя нищо, е сгрешен списък — най-често защото НИСО
    /// е сменила кодовете под него. Тогава се връща пълното меню: по-добре
    /// дълго, отколкото поле, което не може да се попълни.
    /// </summary>
    private static IReadOnlyList<EnumOption> Narrow(ColumnDef column, Func<EnumOption, bool> keep)
    {
        var offered = column.Domain.Options.Where(keep).ToList();

        return offered.Count > 0 ? offered : column.Domain.Options;
    }

    /// <summary>
    /// От най-точното към най-широкото: този лист → този отчет → всички отчети.
    /// Първото намерено печели и нищо не се слива.
    ///
    /// Не се слива, защото сливането прави махането невъзможно: щом по-точният
    /// списък добавя към по-широкия, никой не може да каже „тук тези три, и
    /// нищо друго“ — а точно това е смисълът на нивото „само за един лист“.
    /// </summary>
    private HashSet<string>? Lookup(string report, string node, string column)
    {
        if (_entries.TryGetValue((report, node, column), out var sheet)) return sheet;
        if (_entries.TryGetValue((report, string.Empty, column), out var whole)) return whole;
        if (_entries.TryGetValue((string.Empty, string.Empty, column), out var all)) return all;

        return null;
    }
}
