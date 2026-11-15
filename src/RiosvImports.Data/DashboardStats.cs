using RiosvImports.Core.Localization;

namespace RiosvImports.Data;

/// <summary>Един дял от разбивка: какво е и колко отчета има в него.</summary>
public sealed record MetricSlice(string Label, int Value);

/// <summary>Недовършен отчет и от колко дни стои така.</summary>
public sealed record IdleDocument(DocumentSummary Document, int Days);

/// <summary>
/// Числата за таблото — сметнати наведнъж от списъка с отчети.
///
/// Стоят тук, а не в екрана, по две причини. Първата е, че се проверяват с
/// тест без прозорци: разбивка, която брои погрешно, изглежда точно като
/// вярна. Втората е, че екранът тогава само рисува вече готови числа и няма
/// как да сбърка реда на подреждане между две отваряния.
///
/// Всичко се сметва от вече прочетения списък, а не с отделни заявки към
/// базата: отчетите са десетки, не десетки хиляди, а една обиколка в паметта
/// не може да се разсинхронизира с втора заявка.
/// </summary>
public sealed record DashboardStats(
    int Total,
    int Draft,
    int Checked,
    int Signed,
    IReadOnlyList<MetricSlice> ByCompany,
    IReadOnlyList<MetricSlice> ByReport,
    IReadOnlyList<MetricSlice> ByMonth,
    IReadOnlyList<IdleDocument> Idle)
{
    /// <summary>Докъде е стигнала работата — дял подписани от всички.</summary>
    public int SignedPercent => Total == 0 ? 0 : (int)Math.Round(100.0 * Signed / Total);

    public static readonly DashboardStats Empty =
        new(0, 0, 0, 0, [], [], [], []);

    /// <summary>
    /// Сглобява таблото.
    /// </summary>
    /// <param name="documents">Всички отчети, както ги връща хранилището.</param>
    /// <param name="companies">Заведените фирми — за имената в разбивката.</param>
    /// <param name="reportName">
    /// Как се казва видът отчет на човешки език. Подава се отвън, защото
    /// имената идват от схемите на НИСО, а хранилището не ги чете.
    /// </param>
    /// <param name="now">Местно време — спрямо него се мерят дните и месеците.</param>
    /// <param name="idleAfterDays">След колко дни без пипане отчетът чака внимание.</param>
    /// <param name="months">Колко месеца назад да се покажат в лентата с работата.</param>
    /// <param name="topSlices">Колко реда най-много има една разбивка.</param>
    public static DashboardStats Build(
        IReadOnlyList<DocumentSummary> documents,
        IReadOnlyList<Company> companies,
        Func<DocumentSummary, string> reportName,
        DateTime now,
        int idleAfterDays = 7,
        int months = 6,
        int topSlices = 6)
    {
        var names = companies.ToDictionary(c => c.Id, c => c.Name);
        var noCompany = Lang.T("Без фирма", "No company");

        var byCompany = Top(
            documents,
            d => d.CompanyId is { } id && names.TryGetValue(id, out var name) ? name : noCompany,
            topSlices);

        var byReport = Top(documents, d => Fallback(reportName(d)), topSlices);

        // Месеците се изброяват сами, а не се вадят от данните: месец без нито
        // един отчет също е сведение и трябва да се види като празно място, а
        // не да липсва от редицата.
        var first = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));

        var byMonth = Enumerable.Range(0, months)
            .Select(i => first.AddMonths(i))
            .Select(month => new MetricSlice(
                month.ToString("MM.yyyy"),
                documents.Count(d => SameMonth(d.UpdatedUtc.ToLocalTime(), month))))
            .ToList();

        // Подписаният отчет е приключен и не чака никого, колкото и отдавна да
        // е пипан. Чакат само черновите и проверените — тъкмо те са работата,
        // която някой е оставил по средата.
        var idle = documents
            .Where(d => d.Status != DocumentStatus.Signed)
            .Select(d => new IdleDocument(d, (int)(now.Date - d.UpdatedUtc.ToLocalTime().Date).TotalDays))
            .Where(d => d.Days >= idleAfterDays)
            .OrderByDescending(d => d.Days)
            .Take(topSlices)
            .ToList();

        return new DashboardStats(
            documents.Count,
            documents.Count(d => d.Status == DocumentStatus.Draft),
            documents.Count(d => d.Status == DocumentStatus.Checked),
            documents.Count(d => d.Status == DocumentStatus.Signed),
            byCompany,
            byReport,
            byMonth,
            idle);
    }

    /// <summary>
    /// Най-многобройните дялове. Равните бройки се подреждат по азбучен ред —
    /// иначе две отваряния на таблото дават различна подредба при едни и същи
    /// данни.
    /// </summary>
    private static List<MetricSlice> Top(
        IReadOnlyList<DocumentSummary> documents,
        Func<DocumentSummary, string> key,
        int count) =>
        documents
            .GroupBy(key)
            .Select(g => new MetricSlice(g.Key, g.Count()))
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Label, StringComparer.CurrentCulture)
            .Take(count)
            .ToList();

    private static string Fallback(string? name) =>
        string.IsNullOrWhiteSpace(name) ? Lang.T("Неизвестен вид", "Unknown type") : name;

    private static bool SameMonth(DateTime value, DateTime month) =>
        value.Year == month.Year && value.Month == month.Month;
}
