using System.Globalization;
using System.Xml.Linq;

using RiosvImports.Core.Localization;

namespace RiosvImports.Core.Schema;

/// <summary>
/// Чете XSD файловете на НИСО и ги превръща в модел, който програмата ползва
/// за шаблоните, валидацията и генерирането на XML.
///
/// Нищо в приложението не е дописано на ръка по отчет — всичко идва от схемата,
/// така че при промяна на XSD файловете е достатъчно да се подменят файловете
/// в папка "schemas".
/// </summary>
public sealed class XsdReader
{
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    private readonly string _schemaFolder;
    private readonly Dictionary<string, ValueDomain> _domainCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement> _simpleTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement> _complexTypes = new(StringComparer.Ordinal);

    public XsdReader(string schemaFolder) => _schemaFolder = schemaFolder;

    /// <summary>Зарежда и трите корена: годишен, тримесечен, отчетна книга.</summary>
    public IReadOnlyList<SchemaDefinition> ReadAll()
    {
        // Общите типове са споделени между трите схеми.
        LoadSimpleTypes("common-types.xsd");
        LoadSimpleTypes("ewc.xsd");

        return
        [
            Read("year.xsd", "year", Lang.T("Годишни отчети", "Annual reports")),
            Read("quarter.xsd", "quarter", Lang.T("Тримесечни отчети", "Quarterly reports")),
            Read("book.xsd", "book", Lang.T("Отчетни книги", "Log books"))
        ];
    }

    private void LoadSimpleTypes(string fileName)
    {
        var doc = Load(fileName);
        foreach (var st in doc.Root!.Elements(Xs + "simpleType"))
        {
            var name = st.Attribute("name")?.Value;
            if (name is not null)
                _simpleTypes[name] = st;
        }
    }

    private XDocument Load(string fileName)
    {
        var path = Path.Combine(_schemaFolder, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(Lang.T($"Липсва файл със схема: {path}", $"Schema file missing: {path}"), path);

        // LoadOptions.None запазва коментарите — заглавията на български са в тях.
        return XDocument.Load(path, LoadOptions.None);
    }

    private SchemaDefinition Read(string fileName, string key, string title)
    {
        var doc = Load(fileName);
        var root = doc.Root!;

        // Локалните за файла прости/сложни типове.
        _complexTypes.Clear();
        foreach (var ct in root.Elements(Xs + "complexType"))
        {
            var name = ct.Attribute("name")?.Value;
            if (name is not null)
                _complexTypes[name] = ct;
        }

        foreach (var st in root.Elements(Xs + "simpleType"))
        {
            var name = st.Attribute("name")?.Value;
            if (name is not null && !_simpleTypes.ContainsKey(name))
                _simpleTypes[name] = st;
        }

        var reportsEl = root.Elements(Xs + "element")
                            .First(e => e.Attribute("name")?.Value == "reports");
        var reportsType = reportsEl.Element(Xs + "complexType")!;

        var rootAttributes = ReadAttributes(reportsType);

        var reports = new List<ReportNode>();
        foreach (var decl in ParticleElements(reportsType))
        {
            // <xs:element ref="dsig:Signature"/> — КЕП, добавя се от портала.
            if (decl.Attribute("name") is null) continue;
            reports.Add(BuildNode(decl, null));
        }

        return new SchemaDefinition
        {
            Key = key,
            Title = title,
            FileName = fileName,
            TargetNamespace = root.Attribute("targetNamespace")!.Value,
            RootAttributes = rootAttributes,
            Reports = reports
        };
    }

    // ---------------------------------------------------------------- възли

    private ReportNode BuildNode(XElement decl, ReportNode? parent)
    {
        var name = decl.Attribute("name")!.Value;
        var complexType = ResolveComplexType(decl);

        var repeating = decl.Attribute("maxOccurs")?.Value == "unbounded";

        // Разпознаваме формата <wrapper><item .../></wrapper>, която схемата
        // ползва за почти всички подтаблици.
        var itemDecl = TryGetItemWrapper(complexType);
        if (itemDecl is not null)
        {
            var itemType = ResolveComplexType(itemDecl);
            var node = new ReportNode
            {
                ElementName = name,
                Label = LabelOf(decl) ?? name,
                Repeating = true,
                ItemWrapper = true,
                Attributes = ReadAttributes(itemType),
                Children = BuildChildren(itemType, out var pending)
            };
            node.Parent = parent;
            foreach (var c in pending) c.Parent = node;
            return node;
        }

        var attrs = ReadAttributes(complexType);
        var result = new ReportNode
        {
            ElementName = name,
            Label = LabelOf(decl) ?? name,
            Repeating = repeating,
            ItemWrapper = false,
            Attributes = attrs,
            Children = BuildChildren(complexType, out var kids)
        };
        result.Parent = parent;
        foreach (var c in kids) c.Parent = result;
        return result;
    }

    private IReadOnlyList<ReportNode> BuildChildren(XElement? complexType, out List<ReportNode> created)
    {
        created = [];
        if (complexType is null) return created;

        foreach (var decl in ParticleElements(complexType))
        {
            if (decl.Attribute("name") is null) continue;
            if (decl.Attribute("name")!.Value == "item") continue; // вече обработен
            created.Add(BuildNode(decl, null));
        }

        return created;
    }

    /// <summary>
    /// Ако сложният тип е точно "контейнер от item-и", връща декларацията на item.
    /// </summary>
    private XElement? TryGetItemWrapper(XElement? complexType)
    {
        if (complexType is null) return null;

        var elements = ParticleElements(complexType).ToList();
        if (elements.Count != 1) return null;

        var only = elements[0];
        if (only.Attribute("name")?.Value != "item") return null;
        return only;
    }

    /// <summary>
    /// Изброява декларациите на елементи, като минава през вложените
    /// xs:sequence / xs:choice / xs:all (схемата има такива на едно място).
    /// </summary>
    private static IEnumerable<XElement> ParticleElements(XElement container)
    {
        foreach (var child in container.Elements())
        {
            if (child.Name == Xs + "element")
            {
                yield return child;
            }
            else if (child.Name == Xs + "sequence" ||
                     child.Name == Xs + "choice" ||
                     child.Name == Xs + "all")
            {
                foreach (var nested in ParticleElements(child))
                    yield return nested;
            }
        }
    }

    private XElement? ResolveComplexType(XElement decl)
    {
        var inline = decl.Element(Xs + "complexType");
        if (inline is not null) return inline;

        var typeRef = decl.Attribute("type")?.Value;
        if (typeRef is null) return null;

        var local = LocalName(typeRef);
        return _complexTypes.GetValueOrDefault(local);
    }

    // ------------------------------------------------------------ атрибути

    private IReadOnlyList<ColumnDef> ReadAttributes(XElement? complexType)
    {
        if (complexType is null) return [];

        var list = new List<ColumnDef>();
        foreach (var attr in complexType.Elements(Xs + "attribute"))
        {
            var name = attr.Attribute("name")?.Value;
            if (name is null) continue;

            list.Add(new ColumnDef
            {
                Name = name,
                Label = LabelOf(attr) ?? name,
                Required = attr.Attribute("use")?.Value == "required",
                Domain = ResolveDomain(attr.Attribute("type")?.Value)
            });
        }

        return list;
    }

    // --------------------------------------------------------------- типове

    private ValueDomain ResolveDomain(string? typeRef)
    {
        if (typeRef is null) return ValueDomain.FreeText;

        if (_domainCache.TryGetValue(typeRef, out var cached)) return cached;

        var domain = BuildDomain(typeRef);
        _domainCache[typeRef] = domain;
        return domain;
    }

    private ValueDomain BuildDomain(string typeRef)
    {
        var local = LocalName(typeRef);

        if (typeRef.StartsWith("xs:", StringComparison.Ordinal))
        {
            return new ValueDomain
            {
                TypeName = typeRef,
                Kind = BuiltInKind(local)
            };
        }

        if (!_simpleTypes.TryGetValue(local, out var st))
            return ValueDomain.FreeText;

        // Няколко типа са обединение на други (xs:union) — напр. wasteOpRD е
        // обединение на кодовете за оползотворяване и обезвреждане.
        var union = st.Element(Xs + "union");
        if (union is not null) return BuildUnionDomain(local, union);

        var restriction = st.Element(Xs + "restriction");
        if (restriction is null) return ValueDomain.FreeText;

        var baseType = restriction.Attribute("base")?.Value ?? "xs:string";

        // Ограничение върху друг именуван тип — наследяваме неговите фасети.
        ValueDomain? inherited = null;
        if (!baseType.StartsWith("xs:", StringComparison.Ordinal))
            inherited = ResolveDomain(baseType);

        var options = new List<EnumOption>();
        foreach (var en in restriction.Elements(Xs + "enumeration"))
        {
            var value = en.Attribute("value")?.Value;
            if (value is null) continue;
            options.Add(new EnumOption(value, LabelOf(en)));
        }

        // Един и същ код може да се повтори с различно описание (напр.
        // baselConventionDanger). Пазим първото описание.
        var distinct = options
            .GroupBy(o => o.Value, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var fractionDigits = FacetInt(restriction, "fractionDigits") ?? inherited?.FractionDigits;
        var kind = distinct.Count > 0
            ? ValueKind.Enum
            : (inherited?.Kind ?? BuiltInKind(LocalName(baseType)));

        if (kind is ValueKind.Decimal && fractionDigits == 0)
            kind = ValueKind.Integer;

        return new ValueDomain
        {
            TypeName = local,
            Kind = kind,
            MinLength = FacetInt(restriction, "minLength") ?? inherited?.MinLength,
            MaxLength = FacetInt(restriction, "maxLength") ?? inherited?.MaxLength,
            MinInclusive = FacetDecimal(restriction, "minInclusive") ?? inherited?.MinInclusive,
            MaxInclusive = FacetDecimal(restriction, "maxInclusive") ?? inherited?.MaxInclusive,
            FractionDigits = fractionDigits,
            Pattern = restriction.Element(Xs + "pattern")?.Attribute("value")?.Value
                      ?? inherited?.Pattern,
            Options = distinct
        };
    }

    /// <summary>
    /// Обединява допустимите стойности на всички типове-членове. Ако някой от
    /// тях не е изброен, обединението също не е изброено.
    /// </summary>
    private ValueDomain BuildUnionDomain(string typeName, XElement union)
    {
        var members = new List<ValueDomain>();

        var memberTypes = union.Attribute("memberTypes")?.Value;
        if (memberTypes is not null)
        {
            foreach (var member in memberTypes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                members.Add(ResolveDomain(member));
        }

        // xs:union може да съдържа и вградени определения на типове.
        foreach (var inline in union.Elements(Xs + "simpleType"))
        {
            var restriction = inline.Element(Xs + "restriction");
            if (restriction is null) continue;

            var options = restriction.Elements(Xs + "enumeration")
                .Select(e => e.Attribute("value")?.Value)
                .Where(v => v is not null)
                .Select(v => new EnumOption(v!, null))
                .ToList();

            members.Add(new ValueDomain
            {
                TypeName = typeName + "_inline",
                Kind = options.Count > 0 ? ValueKind.Enum : ValueKind.Text,
                Options = options
            });
        }

        if (members.Count == 0 || members.Any(m => !m.IsEnum))
        {
            return new ValueDomain
            {
                TypeName = typeName,
                Kind = members.FirstOrDefault()?.Kind ?? ValueKind.Text
            };
        }

        var merged = members
            .SelectMany(m => m.Options)
            .GroupBy(o => o.Value, StringComparer.Ordinal)
            .Select(g => g.FirstOrDefault(o => o.Description is not null) ?? g.First())
            .ToList();

        return new ValueDomain
        {
            TypeName = typeName,
            Kind = ValueKind.Enum,
            Options = merged
        };
    }

    private static ValueKind BuiltInKind(string localName) => localName switch
    {
        "date" => ValueKind.Date,
        "dateTime" => ValueKind.Date,
        "decimal" or "double" or "float" => ValueKind.Decimal,
        "int" or "integer" or "long" or "short" or "nonNegativeInteger"
            or "positiveInteger" or "unsignedInt" => ValueKind.Integer,
        _ => ValueKind.Text
    };

    private static int? FacetInt(XElement restriction, string facet)
    {
        var v = restriction.Element(Xs + facet)?.Attribute("value")?.Value;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    private static decimal? FacetDecimal(XElement restriction, string facet)
    {
        var v = restriction.Element(Xs + facet)?.Attribute("value")?.Value;
        return decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    // -------------------------------------------------------------- надписи

    /// <summary>
    /// Българското заглавие идва или от xs:annotation/xs:documentation,
    /// или от XML коментара непосредствено преди декларацията.
    /// </summary>
    private static string? LabelOf(XElement element)
    {
        var doc = element.Element(Xs + "annotation")?.Element(Xs + "documentation")?.Value;
        if (!string.IsNullOrWhiteSpace(doc)) return Squash(doc);

        var node = element.PreviousNode;
        while (node is XText text && string.IsNullOrWhiteSpace(text.Value))
            node = node.PreviousNode;

        if (node is XComment comment && !string.IsNullOrWhiteSpace(comment.Value))
        {
            var value = Squash(comment.Value);

            // В схемата има закоментиран XSD код — той не е надпис.
            if (value.Contains("<xs:", StringComparison.Ordinal)) return null;
            if (value == "?") return null;

            return value.TrimEnd('!', ' ');
        }

        return null;
    }

    private static string Squash(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string LocalName(string qualified)
    {
        var i = qualified.IndexOf(':');
        return i < 0 ? qualified : qualified[(i + 1)..];
    }
}
