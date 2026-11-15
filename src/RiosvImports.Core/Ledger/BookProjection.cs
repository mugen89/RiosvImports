using System.Globalization;
using RiosvImports.Core.Excel;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Заглавната част на книгата — каквото НИСО иска над самите редове.
///
/// Тримесечие, а не месец: схемата иска <c>quarter</c> и обявен период, така че
/// подаването е тримесечно, колкото и месечно да се работи. Месецът е нашата
/// единица, тримесечието е тяхната, и превръщането между двете живее тук.
/// </summary>
public sealed record BookHeader(
    int Year,
    int Quarter,
    string Eik,
    DateOnly From,
    DateOnly To,
    string Type = "REPLACE");

/// <summary>
/// Един ред за книгата: движението плюс колоните, които не участват в сметка.
///
/// Тези колони пътуват от прозорчето до отчета, без складът да ги разбира —
/// той знае само дата, код, количество и площадка. Затова тук те са торба, а
/// не полета: щом схемата ги е дала, схемата и ще ги провери.
/// </summary>
public sealed record BookRow(
    MoveKind Kind,
    DateOnly Date,
    string Ewc,
    decimal Amount,
    string Stagenum,
    IReadOnlyDictionary<string, string> Fields,
    string EwcOut = "")
{
    /// <summary>Кодът, който отива в отчета — изходният, ако е обявен такъв.</summary>
    public string Reported => EwcOut.Length > 0 ? EwcOut : Ewc;
}

/// <summary>
/// Складът, преведен на езика на отчетната книга.
///
/// Тук се обръща посоката, заради която е цялата преработка: досега отчетът
/// беше истината, а наличността — извод от него; отсега движението е истината,
/// а отчетът — проекция.
///
/// Нищо не се подава наново по пътя надолу. Излиза същият
/// <see cref="ParsedWorkbook"/>, който излиза и от попълнен Excel шаблон, и
/// оттам нататък <c>ImportProcessor</c> → <c>XsdValidator</c> → <c>XmlSigner</c>
/// работят, без да знаят откъде са дошли редовете.
///
/// Началните количества НЕ влизат. Те са заварено състояние отпреди воденето,
/// а не приемане — да се подадат като получен отпадък би значело да се обяви за
/// прието нещо, което никой не е донасял през периода.
/// </summary>
public static class BookProjection
{
    /// <summary>
    /// Заглавната част за период от месеци.
    ///
    /// Тримесечието се взема от ПОСЛЕДНИЯ месец: подаването се прави след края
    /// на периода и се числи към него. Обявеният период са истинските дати, а
    /// не границите на тримесечието — те могат да не съвпадат, когато се подава
    /// само един месец.
    /// </summary>
    public static BookHeader Header(string eik, LedgerPeriod from, LedgerPeriod to) =>
        new(to.Year, to.Quarter, eik.Trim(), from.First, to.Last);

    /// <summary>
    /// Кои движения изобщо влизат в отчет за периода.
    ///
    /// Правилото стои тук, а не в страницата, защото е правило, а не подредба
    /// на екрана: началните количества са заварено състояние отпреди воденето и
    /// не са приемане. Подадени като получен отпадък, те биха обявили за прието
    /// нещо, което никой не е донасял през периода — и биха се броили втори път
    /// в следващия отчет.
    ///
    /// Рециклираното не влиза по същата причина, по която съществува: то е
    /// образуван отпадък, изваден от сметката на отчета (<see cref="Moves.InBook"/>).
    /// Отпада ТУК, а не чак при разделите: оттук се брои и колко реда ще влязат,
    /// и за кои площадки се прави лист — рециклирано, стигнало дотам, би
    /// раздуло първото и би родило празен отчет за площадка, която само
    /// рециклира.
    /// </summary>
    public static IEnumerable<Move> Submittable(
        IEnumerable<Move> moves, LedgerPeriod from, LedgerPeriod to) =>
        moves
            .Where(m => !m.Opening && Moves.InBook(m.Kind) && m.Period >= from && m.Period <= to)
            .OrderBy(m => m.Date)
            .ThenBy(m => m.Id);

    /// <summary>
    /// Редовете по възли — това, което се записва в отчет.
    ///
    /// Ключът е името на възела от схемата, а стойността е точно каквото
    /// <c>DocumentStore.SaveRows</c> приема. Така съставеният отчет е обикновен
    /// отчет: отваря се в редактора, проверява се и се подписва по същия път
    /// като всеки друг.
    /// </summary>
    public static Dictionary<string, List<Dictionary<string, string>>> Rows(
        SchemaDefinition schema, IReadOnlyList<BookRow> rows)
    {
        var byNode = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        var layout = Layout(schema);

        foreach (var plan in layout.Sheets)
        {
            if (Moves.Of(plan.Node.ElementName) is not { } kind) continue;

            var known = plan.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var built = Ordered(rows, kind).Select(row => Values(row, known)).ToList();

            if (built.Count > 0) byNode[plan.Node.ElementName] = built;
        }

        // Главният лист носи само номера на площадката, но без него отчетът
        // няма към какво да закачи подтаблиците си.
        byNode[BookNodes.Report] =
        [
            .. Stages(rows).Select(stage => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [WorkbookLayout.StagenumLink] = stage
            })
        ];

        return byNode;
    }

    /// <summary>
    /// Готовата работна книга — входът на проверката и на XML-а.
    ///
    /// Схемата трябва да е СЪЩИЯТ екземпляр, който после ще се подаде на
    /// <c>ImportProcessor</c>: той свързва листовете с възлите по препратка, а
    /// ново четене на схемите дава нови обекти, които не са равни на тези.
    /// </summary>
    public static ParsedWorkbook Build(
        SchemaDefinition schema, BookHeader header, IReadOnlyList<BookRow> rows)
    {
        var layout = Layout(schema);
        var report = layout.Reports[0];
        var sheets = new List<SheetData>();

        foreach (var plan in layout.Sheets)
        {
            List<RawRow> built;

            if (ReferenceEquals(plan.Node, report))
            {
                built =
                [
                    .. Stages(rows).Select((stage, index) => Raw(index + 1,
                        new Dictionary<string, CellValue>(StringComparer.Ordinal)
                        {
                            [WorkbookLayout.StagenumLink] = CellValue.FromText(stage)
                        }))
                ];
            }
            else if (Moves.Of(plan.Node.ElementName) is { } kind)
            {
                var known = plan.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

                built =
                [
                    .. Ordered(rows, kind).Select((row, index) => Raw(index + 1, Cells(row, known)))
                ];

                // Празен раздел не се подава: схемата го допуска липсващ, а
                // празен лист изглежда като пропуснат, вместо като нямащ какво.
                if (built.Count == 0) continue;
            }
            else
            {
                // „Други бележки“ и подобни — складът няма какво да сложи там.
                continue;
            }

            sheets.Add(new SheetData { Plan = plan, Rows = built, UnknownColumns = [] });
        }

        return new ParsedWorkbook
        {
            Schema = schema,
            Config = Config(header),
            Sheets = sheets,
            Warnings = []
        };
    }

    // ------------------------------------------------------------- помощни

    private static WorkbookLayout Layout(SchemaDefinition schema)
    {
        var report = schema.FindReport(BookNodes.Report)
            ?? throw new InvalidOperationException($"Схемата не описва {BookNodes.Report}.");

        return WorkbookLayout.Build(schema, [report]);
    }

    /// <summary>
    /// Редовете на един раздел, подредени както човек би ги писал: по площадка,
    /// после по ден.
    /// </summary>
    private static IEnumerable<BookRow> Ordered(IReadOnlyList<BookRow> rows, MoveKind kind) =>
        rows.Where(r => r.Kind == kind)
            .OrderBy(r => Stage(r.Stagenum), StringComparer.Ordinal)
            .ThenBy(r => r.Date)
            .ThenBy(r => r.Reported, StringComparer.Ordinal);

    /// <summary>Площадките, за които има какво да се подаде.</summary>
    private static IReadOnlyList<string> Stages(IReadOnlyList<BookRow> rows)
    {
        var stages = rows
            .Select(r => Stage(r.Stagenum))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        return stages.Count > 0 ? stages : ["1"];
    }

    private static string Stage(string? stagenum) =>
        string.IsNullOrWhiteSpace(stagenum) ? "1" : stagenum.Trim();

    /// <summary>
    /// Клетките на един ред.
    ///
    /// Тръгва се от колоните на схемата и от тях се взема каквото движението
    /// носи — а не обратното. Колона, която схемата не познава, не се подава:
    /// тя би останала в отчета като чужда дума, която никой надолу не чете.
    /// </summary>
    private static Dictionary<string, CellValue> Cells(BookRow row, IReadOnlySet<string> known)
    {
        var cells = new Dictionary<string, CellValue>(StringComparer.Ordinal);

        foreach (var (name, value) in row.Fields)
            if (known.Contains(name) && value.Length > 0)
                cells[name] = CellValue.FromText(value);

        if (known.Contains("date_record"))
            cells["date_record"] = new CellValue(
                row.Date.ToString(ValueConverter.BulgarianDateFormat, CultureInfo.InvariantCulture),
                row.Date.ToDateTime(TimeOnly.MinValue),
                null);

        // Кодът в отчета е този на реда — освен при третиране с обявен изходен
        // код, където складът се вади по приетия, а книгата носи изходния.
        if (known.Contains("ewc"))
            cells["ewc"] = CellValue.FromText(ValueConverter.NormalizeCode(row.Reported));

        if (known.Contains("amount_ton"))
            cells["amount_ton"] = CellValue.FromText(Amount(row.Amount));

        if (known.Contains(WorkbookLayout.StagenumLink))
            cells[WorkbookLayout.StagenumLink] = CellValue.FromText(Stage(row.Stagenum));

        return cells;
    }

    private static Dictionary<string, string> Values(BookRow row, IReadOnlySet<string> known)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, cell) in Cells(row, known)) values[name] = cell.Text;

        return values;
    }

    /// <summary>
    /// Количеството като текст с точка.
    ///
    /// Не като число: <see cref="CellValue.Number"/> е <c>double</c>, а тонажите
    /// се водят в <c>decimal</c> точно защото двоичната дроб не побира 12,345.
    /// </summary>
    private static string Amount(decimal amount) =>
        SplitMath.Round(amount).ToString("0.###", CultureInfo.InvariantCulture);

    private static Dictionary<string, CellValue> Config(BookHeader header) => new(StringComparer.Ordinal)
    {
        ["year"] = CellValue.FromText(header.Year.ToString(CultureInfo.InvariantCulture)),
        ["quarter"] = CellValue.FromText(header.Quarter.ToString(CultureInfo.InvariantCulture)),
        ["eik"] = CellValue.FromText(header.Eik),
        ["type"] = CellValue.FromText(header.Type),
        ["period_start"] = Day(header.From),
        ["period_end"] = Day(header.To)
    };

    private static CellValue Day(DateOnly date) => new(
        date.ToString(ValueConverter.BulgarianDateFormat, CultureInfo.InvariantCulture),
        date.ToDateTime(TimeOnly.MinValue),
        null);

    private static RawRow Raw(int line, Dictionary<string, CellValue> cells) =>
        new() { ExcelRow = line, Cells = cells };
}
