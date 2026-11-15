using RiosvImports.Core.Ledger;
using RiosvImports.Data;

namespace RiosvImports.App.Shell;

/// <summary>
/// Контролите за един месец — сглобени на едно място.
///
/// Самото правило живее в <see cref="Reconcile"/>; тук стои само сглобяването
/// на въпроса към него — партиди, движения, дялове, месец и начало на воденето.
/// Пита се от два екрана: „Месец“ ги показва, а затварянето ги слуша. Пет реда,
/// повторени на две места, се разминават на първата поправка — и тогава едното
/// място пуска месец, който другото спира.
/// </summary>
internal static class LedgerChecks
{
    public static IReadOnlyList<LedgerIssue> For(
        Workspace workspace, long companyId, string stage, LedgerPeriod period) =>
        Reconcile.Check(
            workspace.Batches.List(companyId, stage),
            workspace.Ledger.Read(companyId, stage),
            workspace.Ledger.Links(companyId, stage),
            period,
            workspace.Setup.Start(companyId));
}
