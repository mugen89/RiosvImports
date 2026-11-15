using RiosvImports.Core.Schema;

namespace RiosvImports.Core.Values;

/// <summary>Какво става с една стойност.</summary>
public enum ValueVerdict
{
    /// <summary>Схемата не я познава — грешно набрана или махната от НИСО.</summary>
    Unknown,

    /// <summary>Предлага се. Никой списък не я спира.</summary>
    Offered,

    /// <summary>Има я в схемата, но кратък списък я държи извън менюто.</summary>
    Blocked
}

/// <summary>
/// Един отговор за една стойност: къде я има и какво става с нея там.
///
/// <see cref="Report"/> и <see cref="Node"/> сочат СПИСЪКА, който я спира, а не
/// мястото, където е потърсена: „всички отчети“ е празен отчет, „целият отчет“ —
/// празен лист. Точно това човек трябва да отвори, за да я върне.
/// </summary>
public sealed record ValueFinding(
    string Value,
    string SchemaKey,
    ColumnDef Column,
    string Report,
    string Node,
    ValueVerdict Verdict);

/// <summary>
/// Обратното търсене: човек вижда, че една стойност я няма в менюто, и пита
/// защо.
///
/// Без този екран отговорът се намира чрез отваряне на записите един по един и
/// сверяване на обхватите — работа, която програмата може да свърши сама. С
/// него въпросът „защо не мога да избера 15 01 10*“ има отговор на един ред.
///
/// Живее в Core и не знае за база и за прозорци: пита схемите и един готов
/// <see cref="ValueLimits"/>.
/// </summary>
public static class ValueSearch
{
    /// <summary>
    /// Разчита въведеното на отделни стойности.
    ///
    /// Хората ги донасят отвсякъде — по един на ред от списък, през запетая от
    /// писмо, слепени с интервали от таблица. По-евтино е да се приемат и
    /// трите, отколкото да се обяснява кой е правилният.
    /// </summary>
    public static IReadOnlyList<string> Split(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries |
                                                                StringSplitOptions.TrimEntries)
                      .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Какво става с една стойност във всички полета, които изобщо я познават.
    ///
    /// Един ЕКО код стои в шест таблици на отчетната книга и в още толкова
    /// годишни отчети. Отговорът обаче не е шестнайсет реда: за всяко поле се
    /// връща по един ред, а когато различни списъци го спират на различни
    /// места — по един за всеки от тях. Останалото е повторение.
    /// </summary>
    public static IReadOnlyList<ValueFinding> Explain(
        IEnumerable<SchemaDefinition> schemas,
        ValueLimits limits,
        string value)
    {
        var found = new List<ValueFinding>();
        var known = false;

        foreach (var schema in schemas)
        {
            // Ключ: името на полето. Едно и също поле в няколко таблици е един
            // въпрос, докато отговорите не се разминат.
            var blocked = new Dictionary<(string Column, string Report, string Node), ColumnDef>();
            var offered = new Dictionary<string, ColumnDef>(StringComparer.Ordinal);

            foreach (var report in schema.Reports)
            {
                foreach (var node in report.SelfAndDescendants())
                {
                    foreach (var column in node.Attributes)
                    {
                        if (!column.Domain.IsEnum || column.IsLink) continue;
                        if (!column.Domain.Options.Any(o =>
                                string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        known = true;

                        if (limits.BlockedBy(report.ElementName, node.ElementName, column, value) is { } scope)
                            blocked[(column.Name, scope.Report, scope.Node)] = column;
                        else
                            offered.TryAdd(column.Name, column);
                    }
                }
            }

            foreach (var ((_, report, node), column) in blocked)
                found.Add(new ValueFinding(value, schema.Key, column, report, node, ValueVerdict.Blocked));

            // Поле, което на едно място е спряно, а на друго не, дава и двата
            // реда — и това е верният отговор, а не шум: точно така е нагласено.
            foreach (var column in offered.Values)
                found.Add(new ValueFinding(value, schema.Key, column, string.Empty, string.Empty, ValueVerdict.Offered));
        }

        if (!known)
        {
            found.Add(new ValueFinding(
                value, string.Empty, UnknownColumn, string.Empty, string.Empty, ValueVerdict.Unknown));
        }

        return found;
    }

    /// <summary>Заместител за стойност, която схемата не познава.</summary>
    private static readonly ColumnDef UnknownColumn = new()
    {
        Name = string.Empty,
        Label = string.Empty,
        Required = false,
        Domain = ValueDomain.FreeText
    };
}
