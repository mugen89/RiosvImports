using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Купчина, която едно изтриване или една поправка оставят без достатъчно.
///
/// <paramref name="BatchId"/> е празно, когато недостигът е по кода изобщо:
/// движение без посочена партида не се вижда през дяловете, но количеството му
/// пак е излязло. <paramref name="Takers"/> са движенията, които висят на купчината
/// — тях човек може да намери.
/// </summary>
public sealed record Shortage(
    StoreKind Store, string Ewc, long? BatchId, DateOnly On, decimal Short, IReadOnlyList<Move> Takers);

/// <summary>
/// Промяна на записано движение така, че нищо след него да не остане без основание.
///
/// Наличността се смята от движенията, затова махнатото само се връща в
/// склада, от който е взето. Това обаче е половината отговор. Движенията са
/// верига — прието, третирано, образувано, предадено — и махнато от средата на
/// веригата връща количеството назад, но онова, което вече е минало напред, си
/// остава там. Десет тона, приети и предадени, с изтрито третиране стават десет
/// в „Получен“ И десет в „Предаден“: двайсет тона от десет.
///
/// Поправката е същият въпрос: тя е изтриване, след което на същото място идва
/// нещо друго. Затова и двете минават оттук, с една и съща сметка. Отделната
/// проверка на поправката гледаше само по партиди и прескачаше всичко, което вече
/// е късо — а в истинската база стотина партиди са къси с грамове от старото
/// закръгляне. Сменен изходен код след предаване минаваше през нея безмълвно и
/// оставяше склада на минус.
///
/// Веригата не се трие сама. Едно образуване може да е взело от петдесет партиди
/// и да виси на това третиране с една от тях; да си отиде заради нея, без човек
/// да го е посочил, би било по-лошо от грешката, която се поправя.
///
/// Сметката е тук, а не в прозореца: кое пречи и кое не се проверява с тест, без
/// прозорец (правило 4).
/// </summary>
public static class Undo
{
    private const decimal Tolerance = 0.0005m;

    /// <summary>
    /// Движенията, които са черпили от изтриваното. Празно значи, че може да се
    /// изтрие и нищо след него няма да остане без основание.
    /// </summary>
    public static IReadOnlyList<Move> Takers(
        IReadOnlyList<Move> moves, IReadOnlyList<LedgerLink> links, IReadOnlySet<long> gone) =>
    [
        .. Shortages(moves, links, gone)
            .SelectMany(s => s.Takers)
            .DistinctBy(m => m.Id)
            .OrderByDescending(m => m.Date)
            .ThenByDescending(m => m.Id)
    ];

    /// <summary>
    /// Какво се разваля, ако <paramref name="gone"/> си отидат и на тяхно място
    /// дойдат <paramref name="made"/>.
    ///
    /// Гледа се всеки ден, а не само краят: купчина, която накрая излиза на нула,
    /// може по средата да е била на минус — и точно там нещо е взето от
    /// махнатото.
    ///
    /// Каквото е било на минус и преди, не пречи: то не е работа на тази промяна.
    /// Пречи само онова, което тя сваля ОЩЕ. Затова заварените грамове не спират
    /// нищо, което не ги пипа — но и не прикриват намаление с четири тона.
    /// </summary>
    public static IReadOnlyList<Shortage> Shortages(
        IReadOnlyList<Move> moves,
        IReadOnlyList<LedgerLink> links,
        IReadOnlySet<long> gone,
        IReadOnlyList<Planned>? made = null)
    {
        // Новите движения получават временни номера под нулята — истинските
        // идват едва със записа. По знака на номера се познава кое ще има след
        // промяната и кое го има сега.
        var planned = (made ?? [])
            .Select((plan, index) => (
                Move: new Move(-(index + 1), plan.Kind, plan.Date, plan.Ewc, plan.Amount,
                    Direct: plan.Direct, EwcOut: plan.EwcOut),
                plan.Draws))
            .ToList();

        var known = moves.ToDictionary(m => m.Id);

        var shares = links
            .Where(l => known.ContainsKey(l.MoveId))
            .Select(l => (Move: known[l.MoveId], l.BatchId, l.Amount))
            .Concat(planned.SelectMany(p => p.Draws.Select(d => (p.Move, d.BatchId, d.Amount))))
            .ToList();

        var whole = moves.Concat(planned.Select(p => p.Move)).ToList();

        var found = new List<Shortage>();

        void Walk(StoreKind store, IEnumerable<(string Ewc, long? Batch, Move Move, decimal Signed)> steps)
        {
            foreach (var pile in steps.GroupBy(s => (s.Ewc, s.Batch)))
            {
                // Купчината се мени само ако в нея нещо си отива или идва.
                if (!pile.Any(s => s.Move.Id < 0 || gone.Contains(s.Move.Id))) continue;

                decimal before = 0, after = 0;

                foreach (var day in pile.GroupBy(s => s.Move.Date).OrderBy(d => d.Key))
                {
                    foreach (var step in day)
                    {
                        if (step.Move.Id > 0) before += step.Signed;
                        if (!gone.Contains(step.Move.Id)) after += step.Signed;
                    }

                    if (after >= -Tolerance || after >= before - Tolerance) continue;

                    found.Add(new Shortage(store, pile.Key.Ewc, pile.Key.Batch, day.Key, SplitMath.Round(-after),
                    [
                        .. pile
                            .Where(s => s.Signed < 0 && s.Move.Id > 0 && !gone.Contains(s.Move.Id))
                            .Select(s => s.Move)
                            .DistinctBy(m => m.Id)
                            .OrderByDescending(m => m.Date)
                            .ThenByDescending(m => m.Id)
                    ]));

                    break;
                }
            }
        }

        foreach (var store in Moves.Stocks)
        {
            // По партиди. В „Третиран“ влизащото и излизащото носят различни кодове
            // по устройство — влиза кабел, излиза мед — и купчината е самата
            // партида. Другаде и кодът е част от нея: образувана мед и образувана
            // пластмаса от една партида са две различни неща, и предадената мед
            // виси на образуваната мед, а не на партидата изобщо.
            Walk(store, shares
                .Where(s => s.Move.Touches(store))
                .Select(s => (
                    store == StoreKind.Treated ? string.Empty : ValueConverter.NormalizeCode(s.Move.Ewc),
                    (long?)s.BatchId,
                    s.Move,
                    s.Move.Sign(store) * s.Amount)));

            // По кодове, с цялото движение. Предаване без посочена партида не се
            // вижда през дяловете, но количеството му пак е излязло от склада и
            // виси на образуваното. В „Третиран“ тази сметка не важи — кодовете
            // там нарочно не си съвпадат (виж StockMath.Waiting).
            if (store != StoreKind.Treated)
                Walk(store, whole
                    .Where(m => m.Touches(store))
                    .Select(m => (ValueConverter.NormalizeCode(m.Ewc), (long?)null, m, m.Signed(store))));
        }

        return found;
    }
}
