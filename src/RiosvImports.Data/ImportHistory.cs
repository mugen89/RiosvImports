using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;

namespace RiosvImports.Data;

/// <summary>
/// Какво ще стане, ако едно внасяне се махне.
///
/// Три списъка, а не един отговор „да/не“: човек, който маха петстотин реда,
/// трябва да види какво точно си отива, какво го спира и кое ще остане след
/// него. Изписаното тук се показва ПРЕДИ махането — после е късно.
/// </summary>
public sealed record ImportCheck(
    ImportRecord Import,
    IReadOnlyList<string> Blocked,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Може ли да се махне изобщо.</summary>
    public bool Allowed => Blocked.Count == 0;
}

/// <summary>Какво наистина си отиде.</summary>
public sealed record ImportUndo(
    int Moves,
    int Batches,
    int Documents,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> Blocked)
{
    /// <summary>Дали внасянето е махнато. Спряното не маха нищо.</summary>
    public bool Done => Blocked.Count == 0;

    public int Total => Moves + Batches + Documents;

    /// <summary>Какво си отиде — изписано за човек.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (Moves > 0) parts.Add(Lang.T($"{Moves} движения", $"{Moves} movements"));
            if (Batches > 0) parts.Add(Lang.T($"{Batches} партиди", $"{Batches} batches"));
            if (Documents > 0) parts.Add(Lang.T($"{Documents} отчета", $"{Documents} reports"));

            return parts.Count == 0
                ? Lang.T("нямаше какво да се махне", "there was nothing to remove")
                : string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Махането на цяло внасяне.
///
/// Стои над хранилищата, защото едно внесено движение не е един запис: то носи
/// партида, дялове по партиди и месец, който може да е затворен. Правилата за
/// всяко от тези неща вече са писани по веднъж — в <see cref="LedgerWriter"/> и
/// в <see cref="PeriodStore"/> — и тук се ползват, вместо да се повтарят.
///
/// Махането е по цяло внасяне, а не по ред. Точно това е смисълът: сгрешеният
/// импорт не се познава по нито един ред поотделно — всеки от тях изглежда
/// правилен, — а по това, че са дошли заедно.
///
/// Затворен месец спира махането. Той е обявено решение „този месец е такъв“ и
/// не бива да се мени под ръката на човека; месецът първо се отваря наново.
///
/// Партида, от която вече е черпено, ОСТАВА. Тя носи произхода на истинска
/// работа, а махането ѝ би оставило третирането да сочи към партида, каквато
/// вече няма — по-добре видим недостиг, отколкото тиха дупка в произхода.
/// </summary>
public sealed class ImportHistory(
    ImportStore imports,
    DocumentStore documents,
    BatchStore batches,
    LedgerStore ledger,
    PeriodStore periods,
    LedgerWriter writer)
{
    private readonly ImportStore _imports = imports;
    private readonly DocumentStore _documents = documents;
    private readonly BatchStore _batches = batches;
    private readonly LedgerStore _ledger = ledger;
    private readonly PeriodStore _periods = periods;
    private readonly LedgerWriter _writer = writer;

    /// <summary>Дневникът на внесеното — за списъка, който човек гледа.</summary>
    public IReadOnlyList<ImportRecord> List(long? companyId = null) => _imports.List(companyId);

    /// <summary>
    /// Какво ще стане, ако това внасяне се махне.
    ///
    /// Не пипа нищо. Отговорът му е текстът, който се показва в питането —
    /// затова е тук, а не в прозорчето: същото питане ще трябва и на второ
    /// място, а две негови копия щяха да се разминат.
    /// </summary>
    public ImportCheck? Check(long importId)
    {
        if (_imports.Find(importId) is not { } import) return null;

        var blocked = new List<string>();
        var warnings = new List<string>();

        if (import.CompanyId is { } companyId)
        {
            var mine = _imports.Items(importId, ImportTarget.Move).ToHashSet();

            var moves = _ledger.Read(companyId).Where(m => mine.Contains(m.Id)).ToList();

            // Затворените месеци се изброяват наведнъж: отговорът е за месеца, а
            // не за реда, и човек иска да види кои месеца да отвори.
            foreach (var month in moves
                         .Select(m => (m.Stagenum, Period: LedgerPeriod.Of(m.Date)))
                         .Distinct()
                         .Where(m => _periods.IsClosed(companyId, m.Stagenum, m.Period))
                         .Select(m => m.Period)
                         .Distinct()
                         .OrderBy(m => m))
                blocked.Add(Lang.T(
                    $"{month.Title()} е затворен — отворете го, за да се махне внесеното в него.",
                    $"{month.Title()} is closed — reopen it before the import can be removed."));

            // Партида, от която е черпено ИЗВЪН внасянето, преживява махането.
            var used = _ledger.Links(companyId)
                .Where(l => !mine.Contains(l.MoveId))
                .Select(l => l.BatchId)
                .ToHashSet();

            var kept = _imports.Items(importId, ImportTarget.Batch)
                .Where(used.Contains)
                .Select(_batches.Find)
                .OfType<Batch>()
                .ToList();

            if (kept.Count > 0)
                warnings.Add(Lang.T(
                    $"{kept.Count} партиди остават: от тях вече е черпено по-нататък " +
                    $"({string.Join(", ", kept.Take(3).Select(b => "#" + b.Code))}" +
                    $"{(kept.Count > 3 ? "…" : "")}).",
                    $"{kept.Count} batches will stay: later work already draws on them " +
                    $"({string.Join(", ", kept.Take(3).Select(b => "#" + b.Code))}" +
                    $"{(kept.Count > 3 ? "…" : "")})."));
        }

        // Подписаният отчет е предаден навън — махането му тук не го връща
        // оттам. Не е пречка, но трябва да се каже.
        var signed = _imports.Items(importId, ImportTarget.Document)
            .Select(_documents.Find)
            .OfType<DocumentSummary>()
            .Count(d => d.Status == DocumentStatus.Signed);

        if (signed > 0)
            warnings.Add(Lang.T(
                $"{signed} от отчетите са подписани. Махането им тук не ги връща от НИСО.",
                $"{signed} of the reports are signed. Removing them here does not recall them from NISO."));

        return new ImportCheck(import, blocked, warnings);
    }

    /// <summary>
    /// Маха внасянето изцяло: записите му, а после и следата му.
    ///
    /// Спряното внасяне не маха нищо — наполовина махнат импорт е по-лош от
    /// непокътнат, защото вече не личи кое е останало от кой файл.
    /// </summary>
    public ImportUndo Undo(long importId)
    {
        if (Check(importId) is not { } check)
            return new ImportUndo(0, 0, 0, [], []);

        if (!check.Allowed)
            return new ImportUndo(0, 0, 0, [], check.Blocked);

        var kept = new List<string>();
        var removedMoves = 0;
        var removedBatches = 0;
        var removedDocuments = 0;

        if (check.Import.CompanyId is { } companyId)
        {
            var known = _ledger.Read(companyId).Select(m => m.Id).ToHashSet();

            // Кои партиди на файла още стоят — брои се преди махането, защото
            // писачът отнася част от тях сам.
            var owned = _imports.Items(importId, ImportTarget.Batch)
                .Where(id => _batches.Find(id) is not null)
                .ToList();

            // През писача, а не през хранилището: махането на движение носи и
            // дяловете му, и партидата, ако към нея вече нищо не сочи.
            foreach (var moveId in _imports.Items(importId, ImportTarget.Move).Where(known.Contains))
            {
                _writer.Remove(companyId, moveId);
                removedMoves++;
            }

            var links = _ledger.Links(companyId).Select(l => l.BatchId).ToHashSet();

            foreach (var batchId in owned)
            {
                // Няма я вече: писачът я е отнесъл с движението ѝ. Брои се за
                // махната, защото си отиде с това внасяне.
                if (_batches.Find(batchId) is not { } batch)
                {
                    removedBatches++;
                    continue;
                }

                if (links.Contains(batchId))
                {
                    kept.Add(Lang.T($"Партида #{batch.Code} остава — от нея вече е черпено.",
                                    $"Batch #{batch.Code} stays — later work already draws on it."));
                    continue;
                }

                _batches.Remove(batchId);
                removedBatches++;
            }
        }

        foreach (var documentId in _imports.Items(importId, ImportTarget.Document))
        {
            if (_documents.Find(documentId) is null) continue;

            _documents.Delete(documentId);
            removedDocuments++;
        }

        _imports.Forget(importId);

        return new ImportUndo(removedMoves, removedBatches, removedDocuments, kept, []);
    }

    /// <summary>
    /// Маха само следата, без записите.
    ///
    /// За внасяне, от което вече нищо не е останало: списък с ред, чийто бутон
    /// няма какво да свърши, е обещание, което не се спазва.
    /// </summary>
    public void Forget(long importId) => _imports.Forget(importId);
}
