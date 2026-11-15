using System.Globalization;
using RiosvImports.Core.Excel;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;

namespace RiosvImports.Core.Help;

/// <summary>Едно поле, както се обяснява на човек.</summary>
public sealed record GuideField(
    string Label,
    bool Required,
    string Kind,
    string? Rule,
    string? Values);

/// <summary>Една таблица от отчета.</summary>
public sealed record GuideTable(
    string Title,
    string Role,
    IReadOnlyList<GuideField> Fields);

/// <summary>Готовото упътване за един вид отчет.</summary>
public sealed record ReportGuide(
    string Title,
    string? Appendix,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Rules,
    IReadOnlyList<GuideTable> Tables);

/// <summary>
/// Съставя упътване за конкретен отчет — какви таблици има, какво влиза във
/// всяко поле и по кои правила.
///
/// Всичко се чете от схемата на НИСО. Нищо не е написано на ръка, затова
/// упътването не може да остарее: при нова схема то се променя само.
/// Ръчно написан текст на такова място живее две години и после лъже.
/// </summary>
public static class ReportGuide_Builder
{
    public static ReportGuide For(SchemaDefinition schema, ReportNode report)
    {
        var layout = WorkbookLayout.Build(schema, [report]);

        return new ReportGuide(
            Title: ReportTitles.Full(report),
            Appendix: ReportTitles.Appendix(report),
            Steps: BuildSteps(layout.Sheets.Count),
            Rules: BuildRules(schema, report),
            Tables: layout.Sheets.Select(sheet => Describe(sheet, report)).ToList());
    }

    private static IReadOnlyList<string> BuildSteps(int tableCount) =>
    [
        "Попълнете общите полета най-горе — година, ЕИК и периода, ако отчетът го иска.",
        tableCount == 1
            ? "Попълнете таблицата с данните."
            : $"Попълнете {tableCount} таблици — всяка е отделен подраздел горе.",
        "Натиснете „Провери“. Проблемите излизат долу; двоен клик върху ред отваря мястото.",
        "Когато няма грешки, натиснете „Създай файл“ и подпишете файла с вашия КЕП."
    ];

    private static IReadOnlyList<string> BuildRules(SchemaDefinition schema, ReportNode report)
    {
        var rules = new List<string>
        {
            "Датите се пишат 31.12.2025. Приемат се и 31/12/2025, и 31-12-2025 — програмата ги подравнява сама.",
            "Числата може да са със запетая или с точка. И двете са наред.",
            "Празните редове се пропускат. Не е нужно да ги триете преди проверката."
        };

        var all = report.SelfAndDescendants().SelectMany(n => n.Attributes).ToList();

        if (all.Any(c => c.Domain.IsLargeEnum))
        {
            rules.Add(
                "Кодовете по ЕКО са над 800. Натиснете F4 в клетката, за да ги търсите по дума или по число.");
        }

        if (all.Any(c => c.Domain.IsEnum && !c.Domain.IsLargeEnum))
        {
            rules.Add(
                "Където има падащ списък, стойността се избира от него. Свободен текст не се приема.");
        }

        if (report.SelfAndDescendants().Count(n => n.HasData) > 1)
        {
            rules.Add(
                $"Колоната „{FieldLabels.Resolve(WorkbookLayout.StagenumLink, null)}“ свързва редовете " +
                "от подтаблиците с реда на отчета. Един и същ номер значи „това се отнася за същия запис“.");
        }

        rules.Add($"Готовият файл се подава по схемата „{schema.Title}“.");

        return rules;
    }

    private static GuideTable Describe(SheetPlan sheet, ReportNode report)
    {
        var isMain = ReferenceEquals(sheet.Node, report);

        var title = isMain
            ? "Основни данни"
            : FieldLabels.Resolve(sheet.Node.ElementName, sheet.Node.Label);

        var role = isMain
            ? "Самият отчет. Обикновено един ред."
            : sheet.Node.Repeating
                ? "Подтаблица — може да има много редове към един запис от основните данни."
                : "Подтаблица — по един ред към запис от основните данни.";

        return new GuideTable(title, role, sheet.Columns.Select(Describe).ToList());
    }

    private static GuideField Describe(ColumnDef column) => new(
        Label: ReportTitles.Column(column),
        Required: column.Required,
        Kind: KindOf(column),
        Rule: RuleOf(column),
        Values: ValuesOf(column));

    private static string KindOf(ColumnDef column) => column.Domain.Kind switch
    {
        ValueKind.Date => "дата",
        ValueKind.Integer => "цяло число",
        ValueKind.Decimal => "число",
        ValueKind.Enum => "избор от списък",
        _ => "текст"
    };

    private static string? RuleOf(ColumnDef column)
    {
        var domain = column.Domain;
        var parts = new List<string>();

        if (column.IsLink) parts.Add("свързва реда с отчета");

        switch (domain.Kind)
        {
            case ValueKind.Date:
                parts.Add("дд.мм.гггг");
                break;

            case ValueKind.Decimal when domain.FractionDigits is { } digits:
                parts.Add($"до {digits} знака след запетаята");
                break;
        }

        if (domain.MinInclusive is { } min && domain.MaxInclusive is { } max)
            parts.Add($"от {Number(min)} до {Number(max)}");
        else if (domain.MinInclusive is { } only)
            parts.Add($"не по-малко от {Number(only)}");
        else if (domain.MaxInclusive is { } cap)
            parts.Add($"не повече от {Number(cap)}");

        if (domain.MaxLength is { } length && domain.Kind == ValueKind.Text)
            parts.Add($"до {length} знака");

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? ValuesOf(ColumnDef column)
    {
        var domain = column.Domain;
        if (!domain.IsEnum) return null;

        if (domain.IsLargeEnum)
            return $"{domain.Options.Count} стойности — търсене с F4";

        // Кратките списъци се изписват изцяло: така упътването отговаря на
        // въпроса, вместо да препраща към още едно място.
        var shown = domain.Options.Take(12).Select(o => o.Display);
        var text = string.Join("; ", shown);

        return domain.Options.Count > 12
            ? text + $"; … и още {domain.Options.Count - 12}"
            : text;
    }

    private static string Number(decimal value) =>
        value == Math.Truncate(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.####", CultureInfo.InvariantCulture);
}
