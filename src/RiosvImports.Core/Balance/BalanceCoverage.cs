using System.Globalization;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Balance;

/// <summary>
/// Един отчет, както го вижда проверката за покритие: кой е, за коя фирма и
/// какъв период твърди, че покрива.
///
/// „Твърди“, защото периодът е заглавна стойност, а датите в редовете са друго
/// нещо. Затова тук стоят и двете — обявеният период и това, което наистина
/// има вътре.
/// </summary>
public sealed record ReportPeriod(
    long Id,
    string Title,
    string Company,
    string Status,
    bool Draft,
    DateTime? From,
    DateTime? To,
    DateTime? FirstRow,
    DateTime? LastRow,
    int Rows)
{
    /// <summary>Отчет без обявен период — покритието му не може да се провери.</summary>
    public bool Unknown => From is null || To is null;

    /// <summary>Колко реда падат извън обявения период. Не е грешка — виж бележката в дневника.</summary>
    public int Outside { get; init; }
}

public enum CoverageProblem
{
    /// <summary>Между два отчета има дни, които никой не покрива.</summary>
    Gap,

    /// <summary>Два отчета покриват едни и същи дни.</summary>
    Overlap,

    /// <summary>Отчетът не казва какъв период покрива.</summary>
    Unknown,

    /// <summary>Периодът свършва преди да е започнал.</summary>
    Backwards
}

/// <summary>Едно намерено несъответствие. Отчетите се сочат по номер, за да се отворят.</summary>
public sealed record CoverageIssue(CoverageProblem Kind, string Company, string Text, long? First, long? Second)
{
    public string KindText => Kind switch
    {
        CoverageProblem.Gap => Lang.T("Пропуск", "Gap"),
        CoverageProblem.Overlap => Lang.T("Застъпване", "Overlap"),
        CoverageProblem.Backwards => Lang.T("Обърнат период", "Backwards period"),
        _ => Lang.T("Без период", "No period")
    };
}

/// <summary>
/// Проверява дали отчетите в баланса покриват годината без пропуски и без
/// застъпвания.
///
/// Балансът събира редове от няколко книги. Ако две от тях покриват един и същ
/// месец, количествата в него се броят два пъти; ако между тях зее седмица,
/// липсва точно толкова. И в двата случая балансът е вярно пресметнат от
/// сгрешени данни — числото изглежда наред и никой не го поглежда втори път.
///
/// Затова проверката е отделна и се вика нарочно: тя не поправя нищо и не
/// спира нищо. Казва кой отчет с кой се разминава, а поправката става в самия
/// отчет.
///
/// Гледа се по фирма: две дружества с еднакви периоди не се застъпват — те са
/// две различни площадки и всяко води своя книга.
/// </summary>
public static class BalanceCoverage
{
    /// <summary>
    /// Изброява несъответствията. Празен списък значи, че годината е покрита
    /// точно веднъж.
    /// </summary>
    public static IReadOnlyList<CoverageIssue> Check(
        IReadOnlyList<ReportPeriod> reports, int year, int from = 1, int to = 12)
    {
        var issues = new List<CoverageIssue>();

        var start = new DateTime(year, Math.Clamp(from, 1, 12), 1);
        var end = new DateTime(year, Math.Clamp(to, from, 12), 1).AddMonths(1).AddDays(-1);

        foreach (var company in reports.Select(r => r.Company).Distinct().OrderBy(c => c, StringComparer.Ordinal))
        {
            var mine = reports.Where(r => r.Company == company).ToList();

            foreach (var report in mine.Where(r => r.Unknown))
            {
                issues.Add(new CoverageIssue(CoverageProblem.Unknown, company,
                    Lang.T($"„{report.Title}“ не казва какъв период покрива.",
                           $"“{report.Title}” does not say which period it covers."),
                    report.Id, null));
            }

            foreach (var report in mine.Where(r => !r.Unknown && r.From > r.To))
            {
                issues.Add(new CoverageIssue(CoverageProblem.Backwards, company,
                    Lang.T($"„{report.Title}“ свършва преди да е започнал ({Show(report.From)} – {Show(report.To)}).",
                           $"“{report.Title}” ends before it begins ({Show(report.From)} – {Show(report.To)})."),
                    report.Id, null));
            }

            var ordered = mine
                .Where(r => !r.Unknown && r.From <= r.To)
                .OrderBy(r => r.From)
                .ToList();

            if (ordered.Count == 0) continue;

            // Началото на периода: непокритото преди първия отчет е пропуск
            // точно както непокритото между два.
            if (ordered[0].From > start)
            {
                issues.Add(Gap(company, start, ordered[0].From!.Value.AddDays(-1), null, ordered[0].Id));
            }

            for (var i = 1; i < ordered.Count; i++)
            {
                var previous = ordered[i - 1];
                var next = ordered[i];

                // Най-късният край досега, а не краят на предишния: отчет,
                // изцяло погълнат от друг, не бива да отваря пропуск след себе си.
                var covered = ordered.Take(i).Max(r => r.To!.Value);

                if (next.From!.Value > covered.AddDays(1))
                {
                    issues.Add(Gap(company, covered.AddDays(1), next.From.Value.AddDays(-1), previous.Id, next.Id));
                }
                else if (next.From.Value <= covered)
                {
                    var days = (covered - next.From.Value).Days + 1;

                    issues.Add(new CoverageIssue(CoverageProblem.Overlap, company,
                        Lang.T($"„{previous.Title}“ и „{next.Title}“ покриват едни и същи {days} дни ({Show(next.From)} – {Show(covered)}).",
                               $"“{previous.Title}” and “{next.Title}” both cover the same {days} days ({Show(next.From)} – {Show(covered)})."),
                        previous.Id, next.Id));
                }
            }

            var last = ordered.Max(r => r.To!.Value);

            if (last < end)
            {
                issues.Add(Gap(company, last.AddDays(1), end, ordered[^1].Id, null));
            }
        }

        return issues;
    }

    private static CoverageIssue Gap(string company, DateTime from, DateTime to, long? before, long? after)
    {
        var days = (to - from).Days + 1;

        return new CoverageIssue(CoverageProblem.Gap, company,
            Lang.T($"Никой отчет не покрива {Show(from)} – {Show(to)} ({days} дни).",
                   $"No report covers {Show(from)} – {Show(to)} ({days} days)."),
            before, after);
    }

    private static string Show(DateTime? date) =>
        date?.ToString(ValueConverter.BulgarianDateFormat, CultureInfo.InvariantCulture) ?? "—";

    /// <summary>
    /// Чете дата така, както е записана в отчета — на ръка, от Excel или по ISO.
    /// </summary>
    public static DateTime? Date(string? text)
    {
        if (!ValueConverter.TryNormalizeDate(text, out var normalized)) return null;

        return DateTime.TryParseExact(normalized, ValueConverter.BulgarianDateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
