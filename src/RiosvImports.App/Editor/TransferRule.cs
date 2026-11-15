using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;

namespace RiosvImports.App.Editor;

/// <summary>
/// Прехвърляне на ред от един раздел в друг вътре в същия отчет.
///
/// Отчетната книга за оползотворяване се пълни на вълни: каквото е дошло в
/// „Получен отпадък“, после се появява почти същото в „Обработен отпадък“ —
/// същият работен лист, същата категория, същото количество. Различават се
/// две-три полета, а човек преписва петдесет реда на ръка. Грешката не е
/// въпрос на внимание, а на брой.
///
/// Затова редът се пренася с копче: каквото се повтаря, се копира; каквото
/// се различава, стои като предварително зададена стойност в страничния
/// панел и се потвърждава веднъж.
/// </summary>
internal sealed record TransferRule
{
    /// <summary>Отчетът, в който важи правилото.</summary>
    public required string Report { get; init; }

    /// <summary>Разделът, от който се тръгва.</summary>
    public required string FromNode { get; init; }

    /// <summary>Разделът, в който се появява новият ред.</summary>
    public required string ToNode { get; init; }

    /// <summary>
    /// Надписът върху копчето. Кратък до неприличие: колоната стои на всеки
    /// ред и всяка нейна буква се плаща с ширина от таблицата. Какво прави,
    /// пише в подсказката.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>Разгадава краткия надпис в подсказката.</summary>
    public required string ActionTip { get; init; }

    /// <summary>Надписът на колоната, която отбелязва вече пренесен ред.</summary>
    public required string Mark { get; init; }

    /// <summary>
    /// Полетата, които човек задава веднъж — в страничния панел или в
    /// прозорчето при всеки ред.
    /// </summary>
    public required IReadOnlyList<string> Defaults { get; init; }

    /// <summary>
    /// Полетата, които се вземат от самия ред. Тук е и връзката към работния
    /// лист: без нея новият ред увисва без родител.
    /// </summary>
    public required IReadOnlyList<string> Carried { get; init; }

    /// <summary>
    /// Полета, чиято стойност е описанието на друго поле.
    ///
    /// „Описание на дейност“ е точно текстът на избраната дейност R/D —
    /// схемата ги държи разделени, но съдържанието е едно и също. Пише се
    /// само, вместо да се преписва.
    /// </summary>
    public required IReadOnlyList<(string From, string To)> Derived { get; init; }

    /// <summary>
    /// Надписът на второто копче — това, което дели количеството.
    ///
    /// От третирането рядко излиза един отпадък: петдесет тона стар кабел се
    /// разпадат на мед, пластмаса и остатък. Но също толкова често излиза
    /// един, и тогава прозорче с таблица е излишна церемония. Затова двете
    /// са отделни копчета, а не едно с брой по подразбиране.
    ///
    /// Празно значи, че този раздел не се дели.
    /// </summary>
    public string? SplitAction { get; init; }

    /// <summary>Разгадава краткия надпис на второто копче.</summary>
    public string? SplitTip { get; init; }

    /// <summary>Дали правилото изобщо може да дели.</summary>
    public bool CanSplit => SplitAction is not null;

    /// <summary>
    /// Поле, чиято стойност може да се вземе наготово от изходния ред.
    ///
    /// При третиране кодът на отпадъка се мени рядко: отпадъкът влиза с един
    /// код и в повечето случаи излиза със същия. Тогава изборът в панела е
    /// преписване — човек намира в списък от осемстотин реда точно това,
    /// което вече пише на реда отсреща, и има шанс да сбърка.
    ///
    /// Затова полето може да се заключи: стойността идва от реда, менюто не
    /// се пипа и няма какво да се разминава. Празно значи, че при това
    /// правило такова поле няма.
    /// </summary>
    public string? SameAsSource { get; init; }

    /// <summary>Дали правилото предлага „същият код“.</summary>
    public bool CanReuseSource => SameAsSource is not null;

    /// <summary>
    /// Надписи, които в контекста на прехвърлянето казват повече от името в
    /// схемата.
    ///
    /// В таблицата „Код на отпадъка“ е ясно — колоната стои в раздела си. В
    /// панела обаче до него стои редът, от който се тръгва, и същият надпис
    /// започва да значи две неща: кодът, с който отпадъкът е дошъл, и кодът,
    /// с който излиза след третирането. Тук се казва кой от двата е.
    ///
    /// Двойката е български и английски, а не готов текст: правилата са
    /// статични и се сглобяват веднъж, преди езикът да е избран.
    /// </summary>
    public required IReadOnlyDictionary<string, (string Bg, string En)> Captions { get; init; }

    /// <summary>Надписът на поле в панела и в прозорчето.</summary>
    public string Caption(ColumnDef column) =>
        Captions.TryGetValue(column.Name, out var pair)
            ? Lang.T(pair.Bg, pair.En)
            : ReportTitles.ColumnShort(column);

    /// <summary>Заглавието на панела и на прозорчето.</summary>
    public string Title(SchemaDefinition schema)
    {
        var node = FindNode(schema, ToNode);
        var name = node is null ? ToNode : ReportTitles.Table(node);

        return Lang.T($"Нов ред в „{name}“", $"A new row in “{name}”");
    }

    /// <summary>Възелът, в който влиза новият ред.</summary>
    public ReportNode? Target(SchemaDefinition schema) => FindNode(schema, ToNode);

    private ReportNode? FindNode(SchemaDefinition schema, string elementName) =>
        schema.FindReport(Report)?
            .SelfAndDescendants()
            .FirstOrDefault(n => n.ElementName == elementName);

    // ------------------------------------------------------------- списък

    /// <summary>
    /// Правилата, които програмата познава.
    ///
    /// Списъкът е нарочно кратък и поименен. Общо правило „копирай еднаквите
    /// полета“ би тръгнало да пренася и там, където съвпадението на имена е
    /// случайно; тук всяко прехвърляне е решено от човек, който познава
    /// отчета.
    /// </summary>
    private static readonly TransferRule[] Known =
    [
        new()
        {
            Report = "r_b_recovery_dispose",
            FromNode = "r_b_rd_received",
            ToNode = "r_b_rd_treated",
            Action = "Тр.",
            ActionTip = "Третиране.",
            Mark = "Третиран",

            // Кодът след третирането най-често е същият, с който отпадъкът е
            // дошъл — затова полето може да се заключи и да се вземе от реда.
            SameAsSource = "ewc",

            Defaults = ["ewc", "date_record", "op_id_code"],
            Captions = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["ewc"] = ("Код на отпадъка СЛЕД третирането", "Waste code AFTER treatment"),
                ["date_record"] = ("Дата на третирането", "Date of the treatment")
            },
            Carried = ["stagenum", "cat_nuba", "origin_nuba", "amount_ton"],
            Derived = [("op_id_code", "op_descr")]
        },

        new()
        {
            Report = "r_b_recovery_dispose",
            FromNode = "r_b_rd_treated",
            ToNode = "r_b_rd_generated",
            Action = "Обр.",
            ActionTip = "Образуване едно към едно.",
            Mark = "Образуван",

            SplitAction = "Дели",
            SplitTip = "Разделяне между няколко кода.",

            // Кодът и датата важат за пренасянето едно към едно. При делене
            // кодовете са толкова, на колкото се дели редът, и се избират в
            // прозорчето; оттук се взема само датата.
            Defaults = ["ewc", "date_record"],
            Carried = ["stagenum", "cat_nuba", "origin_nuba", "amount_ton"],

            // Описанието на отпадъка е името на кода по ЕКО. Схемата иска и
            // двете, но пише едно и също нещо два пъти.
            Derived = [("ewc", "waste_desc")],

            Captions = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["ewc"] = ("Код на образувания отпадък", "Code of the waste generated"),
                ["date_record"] = ("Дата на образуването", "Date the waste was generated")
            }
        }
    ];

    /// <summary>Правилото, което тръгва от този раздел — ако има такова.</summary>
    public static TransferRule? From(string report, string node) =>
        Known.FirstOrDefault(r => r.Report == report && r.FromNode == node);

    /// <summary>Всички правила в даден отчет.</summary>
    public static IEnumerable<TransferRule> ForReport(string report) =>
        Known.Where(r => r.Report == report);
}
