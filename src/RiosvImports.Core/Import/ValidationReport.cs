using System.Globalization;
using System.Text;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;

namespace RiosvImports.Core.Import;

public enum IssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed record Issue(IssueSeverity Severity, string Sheet, int? ExcelRow, string Message)
{
    /// <summary>
    /// Българският надпис на таблицата — същият, който стои на раздела ѝ.
    ///
    /// <see cref="Sheet"/> остава името от схемата, защото то е и името на
    /// листа в работната книга: в отчета за проверката то е точното място, на
    /// което човек трябва да отвори Excel. На екрана обаче няма листове, а
    /// раздели с български имена — там се показва този надпис.
    /// </summary>
    public string? SheetTitle { get; init; }

    public string SeverityText => Severity switch
    {
        IssueSeverity.Error => Lang.T("ГРЕШКА", "ERROR"),
        IssueSeverity.Warning => Lang.T("ПРЕДУПРЕЖДЕНИЕ", "WARNING"),
        _ => Lang.T("ИНФОРМАЦИЯ", "INFO")
    };

    /// <summary>Как се казва мястото пред потребителя.</summary>
    public string Where => string.IsNullOrWhiteSpace(SheetTitle) ? Sheet : SheetTitle;

    /// <summary>Мястото, както се показва на екрана.</summary>
    public string Location => ExcelRow is { } row
        ? Lang.T($"„{Where}“, ред {row}", $"“{Where}”, row {row}")
        : Where.Length > 0 ? $"„{Where}“" : Lang.T("общо", "general");

    /// <summary>
    /// Мястото, както се записва в отчета за проверката. Тук името от схемата
    /// се добавя в скоби: файлът се чете, когато нещо трябва да се разнищи, а
    /// тогава точното име на листа е това, което помага.
    /// </summary>
    public string LogLocation =>
        string.IsNullOrWhiteSpace(SheetTitle) || SheetTitle == Sheet || Sheet.Length == 0
            ? Location
            : ExcelRow is { } row
                ? Lang.T($"„{SheetTitle}“ ({Sheet}), ред {row}", $"“{SheetTitle}” ({Sheet}), row {row}")
                : $"„{SheetTitle}“ ({Sheet})";

    public override string ToString() => $"[{SeverityText}] {LogLocation}: {Message}";
}

/// <summary>Резултатът за един вид отчет — това, което се показва в отделния таб.</summary>
public sealed class ReportOutcome
{
    public required ReportNode Report { get; init; }
    public required string Title { get; init; }

    public int Instances { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }

    public List<Issue> Issues { get; } = [];

    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
}

public sealed class ImportResult
{
    public required SchemaDefinition Schema { get; init; }
    public required IReadOnlyList<ReportOutcome> Reports { get; init; }
    public required IReadOnlyList<Issue> GeneralIssues { get; init; }

    /// <summary>Грешки, върнати от проверката срещу самите XSD файлове.</summary>
    public required IReadOnlyList<string> SchemaErrors { get; init; }

    public System.Xml.Linq.XDocument? Xml { get; init; }

    public int TotalValidRows => Reports.Sum(r => r.ValidRows);
    public int TotalInvalidRows => Reports.Sum(r => r.InvalidRows);

    public IEnumerable<Issue> AllIssues =>
        GeneralIssues.Concat(Reports.SelectMany(r => r.Issues));

    public bool HasErrors =>
        AllIssues.Any(i => i.Severity == IssueSeverity.Error) || SchemaErrors.Count > 0;

    public bool CanExport => Xml is not null && SchemaErrors.Count == 0;

    /// <summary>Пълният отчет за проверката, който се записва до XML файла.</summary>
    public string BuildLog(string sourceFile)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

        sb.AppendLine(Lang.T("ОТЧЕТ ЗА ПРОВЕРКАТА", "CHECK REPORT"));
        sb.AppendLine(new string('=', 70));
        sb.AppendLine(Lang.T($"Дата на генериране : {now}", $"Generated on      : {now}"));
        sb.AppendLine(Lang.T($"Изходен файл       : {sourceFile}", $"Source file       : {sourceFile}"));
        sb.AppendLine(Lang.T($"Схема              : {Schema.Title} ({Schema.FileName})", $"Schema            : {Schema.Title} ({Schema.FileName})"));
        sb.AppendLine();
        sb.AppendLine(Lang.T($"Приети редове      : {TotalValidRows}", $"Accepted rows     : {TotalValidRows}"));
        sb.AppendLine(Lang.T($"Отхвърлени редове  : {TotalInvalidRows}", $"Rejected rows     : {TotalInvalidRows}"));
        sb.AppendLine();

        if (GeneralIssues.Count > 0)
        {
            sb.AppendLine(Lang.T("ОБЩИ ЗАБЕЛЕЖКИ", "GENERAL NOTES"));
            sb.AppendLine(new string('-', 70));
            foreach (var issue in GeneralIssues) sb.AppendLine(issue.ToString());
            sb.AppendLine();
        }

        foreach (var report in Reports)
        {
            sb.AppendLine(report.Title);
            sb.AppendLine(new string('-', 70));
            sb.AppendLine(Lang.T(
                $"Отчети: {report.Instances}   приети редове: {report.ValidRows}   " +
                $"отхвърлени редове: {report.InvalidRows}",
                $"Reports: {report.Instances}   accepted rows: {report.ValidRows}   " +
                $"rejected rows: {report.InvalidRows}"));

            foreach (var issue in report.Issues) sb.AppendLine(issue.ToString());
            sb.AppendLine();
        }

        if (SchemaErrors.Count > 0)
        {
            sb.AppendLine(Lang.T("ГРЕШКИ ПРИ ПРОВЕРКА СРЕЩУ СХЕМАТА", "ERRORS FROM THE SCHEMA CHECK"));
            sb.AppendLine(new string('-', 70));
            foreach (var error in SchemaErrors) sb.AppendLine(error);
            sb.AppendLine();
        }
        else if (Xml is not null)
        {
            sb.AppendLine(Lang.T("Проверката на съдържанието срещу XSD схемата премина успешно.", "The content check against the XSD schema passed."));
            sb.AppendLine(Lang.T(
                "ВАЖНО: схемата изисква и електронен подпис (dsig:Signature). НИСО",
                "IMPORTANT: the schema also requires an electronic signature (dsig:Signature)."));
            sb.AppendLine(Lang.T(
                "отхвърля неподписан файл още при импорта, затова подпишете файла с",
                "NWIS rejects an unsigned file at import, so sign the file with your"));
            sb.AppendLine(Lang.T(
                "вашия КЕП при записването.",
                "certificate when saving."));
        }

        return sb.ToString();
    }

    /// <summary>Само проблемните редове — за бърз преглед.</summary>
    public string BuildProblemRows()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Lang.T("ПРОБЛЕМНИ РЕДОВЕ", "PROBLEM ROWS"));
        sb.AppendLine(new string('=', 70));

        var problems = AllIssues
            .Where(i => i.Severity == IssueSeverity.Error && i.ExcelRow is not null)
            .OrderBy(i => i.Sheet, StringComparer.CurrentCulture)
            .ThenBy(i => i.ExcelRow);

        var any = false;
        foreach (var issue in problems)
        {
            sb.AppendLine(issue.ToString());
            any = true;
        }

        if (!any) sb.AppendLine(Lang.T("Няма отхвърлени редове.", "No rejected rows."));

        return sb.ToString();
    }

    public static string TitleFor(ReportNode report) =>
        FieldLabels.Resolve(report.ElementName, report.Label) + $"  [{report.ElementName}]";
}
