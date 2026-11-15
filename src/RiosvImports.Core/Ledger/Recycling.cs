using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Какво става с едно рециклиране, когато част от него се върне в „Третиран“.
///
/// <paramref name="Keep"/> са дяловете, които остават рециклирани. Празно значи,
/// че рециклирането си отива цялото — движението се маха.
/// </summary>
public sealed record RevertStep(Move Move, IReadOnlyList<Draw> Keep)
{
    /// <summary>Рециклирането не остава изобщо.</summary>
    public bool Gone => Keep.Count == 0;

    /// <summary>Колко остава рециклирано след връщането.</summary>
    public decimal Amount => Gone ? 0m : SplitMath.Round(Keep.Sum(d => d.Amount));

    /// <summary>Колко се връща в „Третиран“.</summary>
    public decimal Returned => SplitMath.Round(Move.Amount - Amount);
}

/// <summary>
/// Връщането на сгрешено рециклиране.
///
/// Рециклирането изважда от сметката нещо, което иначе би влязло в отчета, и
/// затова сгрешеното трябва да може да се върне лесно и точно — без да се рови
/// в дневника и без да се пипа месецът на ръка. Връща се РЕД по ред: един ред в
/// склад „Рециклиран“ е дялът на една партида в едно рециклиране. Върнатото
/// отново чака в „Третиран“ и се образува или рециклира наново.
///
/// Сметката е тук, а не в страницата: кое движение се маха и кое само олеква
/// се проверява с тест, без прозорец (правило 4).
/// </summary>
public static class Recycling
{
    /// <summary>
    /// Какво трябва да стане с рециклиранията, за да се върнат тези редове.
    ///
    /// Движение, от което се връщат всичките видими дялове, си отива цялото —
    /// с него и количеството без посочена партида, ако е имало такова: то не се
    /// вижда като ред и не може да бъде оставено нарочно. Иначе движението
    /// остава с другите си дялове и олеква с върнатото.
    /// </summary>
    public static IReadOnlyList<RevertStep> Revert(
        IEnumerable<StockLine> lines, IEnumerable<Move> moves, IEnumerable<LedgerLink> links)
    {
        var chosen = lines
            .Where(line => line.Single)
            .Select(line => (line.MoveId, BatchId: line.Batch.Id))
            .ToHashSet();

        var recycled = moves.Where(m => m.Kind == MoveKind.Recycled).ToDictionary(m => m.Id);
        var all = links as IReadOnlyList<LedgerLink> ?? [.. links];

        var steps = new List<RevertStep>();

        foreach (var moveId in chosen.Select(c => c.MoveId).Distinct().OrderBy(id => id))
        {
            if (!recycled.TryGetValue(moveId, out var move)) continue;

            var keep = all
                .Where(l => l.MoveId == moveId && !chosen.Contains((moveId, l.BatchId)))
                .Select(l => new Draw(l.BatchId, l.Amount))
                .ToList();

            steps.Add(new RevertStep(move, keep));
        }

        return steps;
    }
}
