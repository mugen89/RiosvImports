namespace RiosvImports.Core.Schema;

/// <summary>Как се тълкува стойността на едно поле.</summary>
public enum ValueKind
{
    Text,
    Integer,
    Decimal,
    Date,
    Enum
}

/// <summary>Една допустима стойност от изброен тип (xs:enumeration).</summary>
public sealed record EnumOption(string Value, string? Description)
{
    /// <summary>Текстът, който се показва в падащото меню на Excel.</summary>
    public string Display =>
        string.IsNullOrWhiteSpace(Description) ? Value : $"{Value} — {Description}";
}

/// <summary>
/// Ограниченията на един прост тип (xs:simpleType) от common-types.xsd / ewc.xsd,
/// сведени до това, което ни трябва за валидация и за шаблона.
/// </summary>
public sealed class ValueDomain
{
    public required string TypeName { get; init; }
    public required ValueKind Kind { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public decimal? MinInclusive { get; init; }
    public decimal? MaxInclusive { get; init; }
    public int? FractionDigits { get; init; }
    public string? Pattern { get; init; }
    public IReadOnlyList<EnumOption> Options { get; init; } = [];

    public bool IsEnum => Kind == ValueKind.Enum && Options.Count > 0;

    /// <summary>
    /// Изброените типове за ЕКО кодове са огромни (над 800 стойности). За тях не
    /// слагаме падащо меню директно в клетката, а ги изнасяме в отделен лист.
    /// </summary>
    public bool IsLargeEnum => IsEnum && Options.Count > 200;

    public static readonly ValueDomain FreeText = new()
    {
        TypeName = "xs:string",
        Kind = ValueKind.Text
    };
}

/// <summary>Една колона в Excel = един XML атрибут.</summary>
public sealed class ColumnDef
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required bool Required { get; init; }
    public required ValueDomain Domain { get; init; }

    /// <summary>
    /// Служебна колона: съществува само в Excel, за да свърже реда с родителския
    /// запис. Не се записва като XML атрибут.
    /// </summary>
    public bool IsLink { get; init; }

    public string HeaderText => Name;
}

/// <summary>
/// Възел от отчета. Един и същ тип описва и самия отчет, и подтаблиците му,
/// защото схемата е рекурсивна (виж r_yr_rd_nuba_iii_e_header).
/// </summary>
public sealed class ReportNode
{
    public required string ElementName { get; init; }
    public required string Label { get; init; }

    /// <summary>maxOccurs="unbounded" — възелът се повтаря.</summary>
    public required bool Repeating { get; init; }

    /// <summary>
    /// Формата в XML е &lt;ElementName&gt;&lt;item ... /&gt;&lt;/ElementName&gt;
    /// вместо повтарящ се &lt;ElementName ... /&gt;.
    /// </summary>
    public required bool ItemWrapper { get; init; }

    /// <summary>Собствените XML атрибути (без служебните връзки).</summary>
    public required IReadOnlyList<ColumnDef> Attributes { get; init; }

    public required IReadOnlyList<ReportNode> Children { get; init; }

    public ReportNode? Parent { get; set; }

    /// <summary>Коренният отчет, към който принадлежи този възел.</summary>
    public ReportNode Root => Parent?.Root ?? this;

    /// <summary>Има ли изобщо данни за попълване (т.е. трябва ли му лист в Excel).</summary>
    public bool HasData => Attributes.Count > 0;

    public IEnumerable<ReportNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }
}

/// <summary>Един от трите корена: годишен, тримесечен, отчетна книга.</summary>
public sealed class SchemaDefinition
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required string TargetNamespace { get; init; }

    /// <summary>Атрибутите на &lt;reports&gt; — year, eik, type и т.н.</summary>
    public required IReadOnlyList<ColumnDef> RootAttributes { get; init; }

    public required IReadOnlyList<ReportNode> Reports { get; init; }

    public ReportNode? FindReport(string elementName) =>
        Reports.FirstOrDefault(r => r.ElementName == elementName);
}
