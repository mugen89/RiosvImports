using RiosvImports.Core.Schema;

namespace RiosvImports.Core.Excel;

/// <summary>Един лист от работната книга = един възел от схемата.</summary>
public sealed class SheetPlan
{
    public required string SheetName { get; init; }
    public required ReportNode Node { get; init; }
    public required ReportNode Report { get; init; }

    /// <summary>Служебни колони, които свързват реда с родителския запис.</summary>
    public required IReadOnlyList<ColumnDef> LinkColumns { get; init; }

    /// <summary>Собствената служебна колона-идентификатор (само за вложени повтарящи се възли).</summary>
    public ColumnDef? IdentityColumn { get; init; }

    public IReadOnlyList<ColumnDef> Columns { get; private set; } = [];

    internal void Finish()
    {
        var cols = new List<ColumnDef>(LinkColumns);
        if (IdentityColumn is not null) cols.Add(IdentityColumn);
        cols.AddRange(Node.Attributes);
        Columns = cols;
    }
}

/// <summary>Планът на цялата работна книга за даден избор от отчети.</summary>
public sealed class WorkbookLayout
{
    public const string ConfigSheet = "НАСТРОЙКИ";
    public const string InstructionsSheet = "УКАЗАНИЯ";
    public const string MetaSheet = "_МЕТА";
    public const string ListsSheet = "_СПИСЪЦИ";

    /// <summary>Над този брой стойности в падащото меню показваме само кода.</summary>
    public const int CodeOnlyDropdownThreshold = 40;

    public required SchemaDefinition Schema { get; init; }
    public required IReadOnlyList<ReportNode> Reports { get; init; }
    public required IReadOnlyList<SheetPlan> Sheets { get; init; }

    /// <summary>
    /// <paramref name="knownNames"/> идва от „_МЕТА“ на вече записан шаблон: имената
    /// на листовете зависят от това кои отчети са били избрани, затова при четене
    /// не бива да се измислят наново, а да се вземат от самия файл.
    /// </summary>
    public static WorkbookLayout Build(
        SchemaDefinition schema,
        IEnumerable<ReportNode> reports,
        IReadOnlyDictionary<(string Report, string Node), string>? knownNames = null)
    {
        var selected = reports.ToList();
        var sheets = new List<SheetPlan>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ConfigSheet, InstructionsSheet, MetaSheet, ListsSheet
        };

        foreach (var report in selected)
        {
            foreach (var node in report.SelfAndDescendants())
            {
                if (!node.HasData) continue;

                var known = knownNames is not null &&
                            knownNames.TryGetValue((report.ElementName, node.ElementName), out var name)
                    ? name
                    : null;

                if (known is not null) used.Add(known);

                var plan = new SheetPlan
                {
                    SheetName = known ?? UniqueSheetName(node, report, used),
                    Node = node,
                    Report = report,
                    LinkColumns = BuildLinkColumns(node, report),
                    IdentityColumn = NeedsIdentity(node) ? IdentityColumnFor(node) : null
                };
                plan.Finish();
                sheets.Add(plan);
            }
        }

        return new WorkbookLayout
        {
            Schema = schema,
            Reports = selected,
            Sheets = sheets
        };
    }

    /// <summary>
    /// Ключът, който свързва редовете от подтаблиците с отчета, е stagenum —
    /// номерът на работния лист. Той присъства във всички 42 вида отчети.
    /// </summary>
    public const string StagenumLink = "stagenum";

    private static IReadOnlyList<ColumnDef> BuildLinkColumns(ReportNode node, ReportNode report)
    {
        if (ReferenceEquals(node, report)) return [];

        // Ако таблицата има собствен stagenum, той служи и за връзка —
        // не добавяме втора колона със същото име.
        if (node.Attributes.Any(a => a.Name == StagenumLink)) return [];

        var links = new List<ColumnDef>
        {
            new()
            {
                Name = StagenumLink,
                Label = "Номер на работен лист — трябва да съвпада с листа на отчета",
                Required = true,
                Domain = report.Attributes.FirstOrDefault(a => a.Name == StagenumLink)?.Domain
                         ?? ValueDomain.FreeText,
                IsLink = true
            }
        };

        // Междинните повтарящи се възли (има само един такъв в цялата схема)
        // изискват допълнителен ключ.
        var chain = new List<ReportNode>();
        for (var p = node.Parent; p is not null && !ReferenceEquals(p, report); p = p.Parent)
            chain.Insert(0, p);

        foreach (var ancestor in chain.Where(NeedsIdentity))
            links.Add(IdentityColumnFor(ancestor));

        return links;
    }

    /// <summary>
    /// Вложен повтарящ се възел със собствени подтаблици има нужда от собствен
    /// номер, за да могат децата му да сочат към конкретния запис.
    /// </summary>
    private static bool NeedsIdentity(ReportNode node) =>
        node.Parent is not null && node.Repeating && node.Children.Any(c => c.HasData);

    private static ColumnDef IdentityColumnFor(ReportNode node) => new()
    {
        Name = node.ElementName + "_no",
        Label = $"Пореден номер на записа в „{node.ElementName}“",
        Required = true,
        Domain = ValueDomain.FreeText,
        IsLink = true
    };

    private static string UniqueSheetName(ReportNode node, ReportNode report, HashSet<string> used)
    {
        var baseName = Truncate(node.ElementName, 31);
        if (used.Add(baseName)) return baseName;

        // Случаят r_b_notes — една и съща подтаблица в шест различни отчета.
        var qualified = Truncate($"{report.ElementName}_{node.ElementName}", 31);
        if (used.Add(qualified)) return qualified;

        for (var i = 2; i < 100; i++)
        {
            var suffix = "~" + i;
            var candidate = Truncate(baseName, 31 - suffix.Length) + suffix;
            if (used.Add(candidate)) return candidate;
        }

        throw new InvalidOperationException($"Не може да се избере име на лист за {node.ElementName}.");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
