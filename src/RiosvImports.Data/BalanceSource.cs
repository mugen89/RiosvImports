using System.Globalization;
using RiosvImports.Core.Balance;

namespace RiosvImports.Data;

/// <summary>
/// Откъде идват движенията за баланса.
///
/// Тук се решават два въпроса и нищо друго. Първият е кои отчети изобщо се
/// броят: недовършен отчет съдържа половин истина — редовете в него са
/// въведени, но не са проверени, — и събран с готовите я разваля. Затова
/// влизат само проверените и подписаните.
///
/// Вторият е изключението от първото. Човек, който тъкмо попълва отчет,
/// иска да види какво прави той с наличностите ПРЕДИ да го затвори — иначе
/// поправката идва след подписа. Затова към готовите може да се добави
/// ЕДИН недовършен, избран поименно. Един, а не всички: две чернови с
/// припокриващи се редове дават число, което не отговаря на нищо.
/// </summary>
public sealed class BalanceSource(DocumentStore documents)
{
    private readonly DocumentStore _documents = documents;

    /// <summary>Отчетите, чиито редове се събират — готовите плюс избраната чернова.</summary>
    public static IReadOnlyList<DocumentSummary> Eligible(
        IEnumerable<DocumentSummary> all,
        IReadOnlyCollection<long?>? companies = null,
        long? draft = null) =>
    [
        .. all.Where(d => Mine(d, companies) && (Ready(d) || d.Id == draft))
    ];

    /// <summary>Недовършените отчети, между които се избира временното допълнение.</summary>
    public static IReadOnlyList<DocumentSummary> Drafts(
        IEnumerable<DocumentSummary> all,
        IReadOnlyCollection<long?>? companies = null) =>
    [
        .. all.Where(d => Mine(d, companies) && !Ready(d))
    ];

    /// <summary>Проверен или подписан: и в двата случая числата в него са минали проверка.</summary>
    private static bool Ready(DocumentSummary document) =>
        document.Status is DocumentStatus.Checked or DocumentStatus.Signed;

    /// <summary>
    /// Отчет от книгата за оползотворяване, на някоя от избраните фирми.
    ///
    /// Празен списък значи всички фирми — това е поведението по подразбиране
    /// на четенето, а не на екрана: там непосочена фирма значи непосочен
    /// обхват и се казва изрично.
    ///
    /// Списъкът приема и null: отчет без фирма съществува — стъпката, която
    /// въведе фирмите, остави заварените без такава — и трябва да може да се
    /// посочи наравно с останалите, вместо да е видим само когато сито няма.
    /// </summary>
    private static bool Mine(DocumentSummary document, IReadOnlyCollection<long?>? companies)
    {
        if (document.Report != BalanceSheets.Report) return false;
        if (companies is not { Count: > 0 }) return true;

        return companies.Contains(document.CompanyId);
    }

    /// <summary>
    /// Годините, за които изобщо има какво да се гледа.
    ///
    /// Чете се заглавната стойност на отчета, а не редовете: за списък от
    /// години не си струва да се разгъват хиляди записа. Текущата година стои
    /// винаги — новата книга още няма нито ред, а балансът ѝ трябва отсега.
    /// </summary>
    public IReadOnlyList<int> Years(IEnumerable<DocumentSummary> all, int today)
    {
        var years = new HashSet<int> { today };

        foreach (var document in all.Where(d => d.Report == BalanceSheets.Report))
        {
            if (!_documents.ReadHeader(document.Id).TryGetValue("year", out var text)) continue;

            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) &&
                year is >= 1990 and <= 2100)
                years.Add(year);
        }

        return [.. years.OrderByDescending(y => y)];
    }

    /// <summary>
    /// Какъв период твърди, че покрива всеки от избраните отчети — и какво
    /// наистина има вътре.
    ///
    /// Двете не са едно и също и точно затова се четат заедно: обявеният
    /// период идва от заглавните стойности, а датите в редовете ги е писал
    /// човек. Разминаването между тях понякога е грешка, а понякога е нарочно
    /// — затова тук то само се показва, без да се нарича проблем.
    /// </summary>
    public IReadOnlyList<ReportPeriod> Periods(
        IEnumerable<DocumentSummary> chosen, Func<long?, string> company)
    {
        var periods = new List<ReportPeriod>();

        foreach (var document in chosen)
        {
            var header = _documents.ReadHeader(document.Id);

            var from = BalanceCoverage.Date(header.GetValueOrDefault("period_start"));
            var to = BalanceCoverage.Date(header.GetValueOrDefault("period_end"));

            DateTime? first = null;
            DateTime? last = null;
            var rows = 0;
            var outside = 0;

            foreach (var sheet in BalanceSheets.All)
            {
                foreach (var row in _documents.ReadRows(document.Id, BalanceSheets.Node(sheet)))
                {
                    if (BalanceCoverage.Date(row.Values.GetValueOrDefault("date_record")) is not { } date) continue;

                    rows++;

                    if (first is null || date < first) first = date;
                    if (last is null || date > last) last = date;

                    if (from is not null && date < from) outside++;
                    else if (to is not null && date > to) outside++;
                }
            }

            periods.Add(new ReportPeriod(
                document.Id,
                document.Title,
                company(document.CompanyId),
                document.StatusLabel,
                document.Status == DocumentStatus.Draft,
                from, to, first, last, rows)
            {
                Outside = outside
            });
        }

        return periods;
    }

    /// <summary>
    /// Движенията от избраните отчети за една година.
    ///
    /// Редовете идват такива, каквито човекът ги е оставил — дата, писана на
    /// ръка, количество със запетая. Каквото не се чете, не се брои и не се
    /// оплаква: то ще падне при проверката на самия отчет, а балансът не е
    /// мястото да се научава за него.
    /// </summary>
    public IReadOnlyList<BalanceEntry> Read(IEnumerable<DocumentSummary> chosen, int year)
    {
        var entries = new List<BalanceEntry>();

        foreach (var document in chosen)
        {
            foreach (var sheet in BalanceSheets.All)
            {
                foreach (var row in _documents.ReadRows(document.Id, BalanceSheets.Node(sheet)))
                {
                    if (BalanceMath.Entry(sheet, row.Values, year) is { } entry) entries.Add(entry);
                }
            }
        }

        return entries;
    }
}
