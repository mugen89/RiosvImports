using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Един изходен код и колко излиза по него.
///
/// От третирането рядко излиза един отпадък: петдесет тона стар кабел се
/// разпадат на мед, пластмаса и остатък. Затова образуването не е число, а
/// списък — същото, което прозорчето „Дели“ прави в отчета, но тръгнало от
/// склада, където количеството е известно и произходът се пази.
/// </summary>
public sealed record GenPart(string Ewc, decimal Amount);

/// <summary>
/// Какво се образува от една купчина: колко се изчерпва от нея и в какво излиза.
///
/// Двете страни стоят в един запис, защото се проверяват една срещу друга:
/// сборът на <paramref name="Parts"/> е онова, което трябва да съвпадне с
/// изчерпаното. Разминаване между тях е най-честата грешка тук и се хваща на
/// едно място, вместо да се събира наум по два екрана.
///
/// <paramref name="Picks"/> са самите дялове — коя партида по колко дава.
/// Произходът на образуваното не се пита: той се наследява от третираното в
/// същото съотношение (<see cref="Allocation.Inherit"/>), защото друго
/// съотношение никой не е измервал.
/// </summary>
public sealed record GenOrder(
    TreatPile Pile, IReadOnlyList<TreatPick> Picks, IReadOnlyList<GenPart> Parts)
{
    /// <summary>Колко се изчерпва от купчината.</summary>
    public decimal Consumed => SplitMath.Round(Picks.Sum(p => p.Amount));

    /// <summary>Колко излиза по всички изходни кодове.</summary>
    public decimal Produced => SplitMath.Round(Parts.Sum(p => p.Amount));

    /// <summary>Колко от изчерпаното още не е разпределено по кодове.</summary>
    public decimal Left => SplitMath.Round(Consumed - Produced);
}

/// <summary>
/// Готово движение „образуван“: изходен код, количество и от кои партиди.
///
/// <paramref name="Source"/> е кодът, от който е излязло — третираният. В
/// движението той не влиза (книгата иска само изходния), но екранът го показва,
/// а обединяването по код го губи нарочно: тогава зад един ред стоят две
/// купчини и „от кой код“ вече няма един отговор.
/// </summary>
public sealed record GenMove(
    string Ewc, decimal Amount, IReadOnlyList<Draw> Draws, string Source = "");

/// <summary>Какво не е наред с поисканото образуване.</summary>
public enum GenProblem
{
    /// <summary>Отрицателно количество.</summary>
    Negative,

    /// <summary>Иска се повече, отколкото чака по този код.</summary>
    OverStock,

    /// <summary>Ред с количество, но без код на образувания отпадък.</summary>
    NoCode,

    /// <summary>Сборът на изходните кодове не е равен на изчерпаното.</summary>
    Mismatch,

    /// <summary>Нито един ред не носи количество.</summary>
    Nothing
}

/// <summary>Едно намерено несъответствие — текстът е за екрана, видът за кода.</summary>
public sealed record GenIssue(GenProblem Kind, string Ewc, decimal Amount)
{
    public string Text => Say(MoveKind.Generated);

    /// <summary>
    /// Текстът с думите на действието — образуване или рециклиране.
    ///
    /// Проверката е една и съща за двете (те са едно и също нещо с различен
    /// край), но съобщение „без код на образувания отпадък“ в прозорец, който
    /// рециклира, кара човек да се чуди в кой прозорец е.
    /// </summary>
    public string Say(MoveKind action)
    {
        var recycled = action == MoveKind.Recycled;

        return Kind switch
        {
            GenProblem.Negative =>
                Lang.T($"{Ewc} — количеството не може да е отрицателно.",
                       $"{Ewc} — the quantity cannot be negative."),
            GenProblem.OverStock =>
                Lang.T($"{Ewc} — в склад „Третиран“ чакат само {SplitMath.Amount(Amount)} т.",
                       $"{Ewc} — only {SplitMath.Amount(Amount)} t are waiting in the “Treated” store."),
            GenProblem.NoCode => recycled
                ? Lang.T($"{Ewc} — ред с количество, но без код на рециклирания отпадък.",
                         $"{Ewc} — a row carries a quantity but no code for the waste recycled.")
                : Lang.T($"{Ewc} — ред с количество, но без код на образувания отпадък.",
                         $"{Ewc} — a row carries a quantity but no code for the waste generated."),
            GenProblem.Mismatch when Amount > 0 =>
                Lang.T($"{Ewc} — остават {SplitMath.Amount(Amount)} т, неразпределени по кодове.",
                       $"{Ewc} — {SplitMath.Amount(Amount)} t are left unassigned to a code."),
            GenProblem.Mismatch => recycled
                ? Lang.T($"{Ewc} — рециклираното надхвърля третираното с {SplitMath.Amount(-Amount)} т.",
                         $"{Ewc} — what is recycled exceeds what was treated by {SplitMath.Amount(-Amount)} t.")
                : Lang.T($"{Ewc} — образуваното надхвърля третираното с {SplitMath.Amount(-Amount)} т.",
                         $"{Ewc} — what is generated exceeds what was treated by {SplitMath.Amount(-Amount)} t."),
            _ => recycled
                ? Lang.T("Няма нито един ред с количество за рециклиране.",
                         "No row carries a quantity to recycle.")
                : Lang.T("Няма нито един ред с количество за образуване.",
                         "No row carries a quantity to generate.")
        };
    }
}

/// <summary>
/// Образуването, тръгнало от склада.
///
/// Третирането питаше „колко от тази купчина“; образуването пита същото и още
/// едно: „и в какво излиза“. Второто е разликата, заради която прозорчето не е
/// същото — един вход дава няколко изхода, всеки със свой код по ЕКО.
///
/// Купчината е същата <see cref="TreatPile"/>, защото е същото нещо: код и
/// всичко, което стои под него. Само складът е друг — „Третиран“ вместо
/// „Получен“ — а купчината не знае от кой склад е събрана.
///
/// Сметката стои тук, а не в прозореца, по същата причина, както при
/// третирането: едно правило важи и за един ред, и за двайсет избрани, и се
/// проверява с тест без нито един прозорец (правило 4).
/// </summary>
public static class Generation
{
    /// <summary>
    /// Купчините, от които се образува.
    ///
    /// Същото свеждане като при третирането — и нарочно същото: човек, който е
    /// видял едните, не бива да заварва другите подредени иначе.
    /// </summary>
    public static IReadOnlyList<TreatPile> Piles(IEnumerable<StockShare> shares) =>
        Treatment.Piles(shares);

    /// <summary>
    /// Дяловете, сведени до „от коя партида колко“.
    ///
    /// Два дяла към една партида се събират: ключът на връзката е (движение,
    /// партида) и вторият иначе би изместил първия.
    /// </summary>
    public static IReadOnlyList<Draw> Draws(IEnumerable<TreatPick> picks)
    {
        var taken = new Dictionary<long, decimal>();

        foreach (var pick in picks)
        {
            if (pick.Amount <= 0) continue;

            taken[pick.Share.Batch.Id] = taken.GetValueOrDefault(pick.Share.Batch.Id) + pick.Amount;
        }

        return [.. taken.Select(pair => new Draw(pair.Key, SplitMath.Round(pair.Value)))];
    }

    /// <summary>
    /// Разписва рецепта по дадено количество.
    ///
    /// Дяловете се четат като СЪОТНОШЕНИЕ, а не като проценти от сто: делят се
    /// на собствения си сбор. Разликата се вижда при шаблон, чиито проценти не
    /// правят точно сто — 33,33 три пъти прави 99,99, а стар или поправен на
    /// ръка шаблон може да прави и деветдесет. Делено на сто, такова съотношение
    /// би струпало всичко неразпределено върху последния ред; делено на сбора
    /// си, то остава онова, което човек е имал предвид.
    ///
    /// <b>Сборът е точно колкото е влязло — винаги.</b> Остатъкът от
    /// закръглянето пада върху последния дял, който изобщо участва: три равни
    /// дяла от петдесет тона иначе правят 50,001, а НИСО брои до грам.
    ///
    /// Върху дял с нула не пада нищо. Редът без код стои празен нарочно —
    /// няколко грама, паднали в него, го превръщат в „ред с количество, но без
    /// код“ и спират записа за нещо, което никой не е писал.
    /// </summary>
    public static IReadOnlyList<GenPart> Recipe(
        decimal total, IReadOnlyList<(string Ewc, decimal Percent)> parts)
    {
        if (parts.Count == 0) return [];

        var made = SplitMath.Round(total);
        var whole = parts.Sum(p => Math.Max(0m, p.Percent));

        if (made <= 0 || whole <= 0)
            return [.. parts.Select(part => new GenPart(part.Ewc, 0m))];

        // Последният УЧАСТВАЩ дял поема остатъка от закръглянето.
        var last = -1;

        for (var i = 0; i < parts.Count; i++)
            if (parts[i].Percent > 0) last = i;

        var amounts = new decimal[parts.Count];
        decimal running = 0;

        for (var i = 0; i < parts.Count; i++)
        {
            if (i == last || parts[i].Percent <= 0) continue;

            amounts[i] = SplitMath.Round(made * parts[i].Percent / whole);
            running += amounts[i];
        }

        if (last >= 0) amounts[last] = SplitMath.Round(made - running);

        return [.. parts.Select((part, i) => new GenPart(part.Ewc, amounts[i]))];
    }

    /// <summary>
    /// Свежда рецептата до съотношение, което събира точно сто.
    ///
    /// Това е онова, което се записва в шаблон: не тонове, а дялове. Тонове,
    /// записани веднъж, важат за едно-единствено количество; съотношението важи
    /// за всяко и затова следващия месец не се пише наново.
    ///
    /// Изравнява се до сто и тук, а не само при прилагането: шаблонът се чете и
    /// от прозорчето „Дели“ в отчета, което дели на сто. Оставени на 99,99, три
    /// равни дяла биха се разминали между двата прозореца.
    ///
    /// Редовете без код и без количество отпадат: в шаблона те не са дял, а
    /// празен ред, който някой не е попълнил.
    /// </summary>
    public static IReadOnlyList<(string Ewc, decimal Percent)> Ratio(IEnumerable<GenPart> parts)
    {
        var kept = parts
            .Where(p => p.Amount > 0 && ValueConverter.NormalizeCode(p.Ewc).Length > 0)
            .ToList();

        var whole = SplitMath.Round(kept.Sum(p => p.Amount));

        if (kept.Count == 0 || whole <= 0) return [];

        var shares = new decimal[kept.Count];
        decimal running = 0;

        for (var i = 0; i < kept.Count - 1; i++)
        {
            shares[i] = SplitMath.Percent(kept[i].Amount, whole);
            running += shares[i];
        }

        // Последният поема разликата до сто — по същата сметка, по която после
        // ще поеме и остатъка от закръглянето на тоновете.
        shares[^1] = Math.Round(100m - running, 2, MidpointRounding.AwayFromZero);

        return
        [
            .. kept.Select((part, i) => (ValueConverter.NormalizeCode(part.Ewc), shares[i]))
        ];
    }

    /// <summary>
    /// Смята същите дялове за друго количество.
    ///
    /// Ползва се, когато човек смени колко се изчерпва от купчината: рецептата
    /// е съотношение, а не тонове. Смените ли 15 т на 10, „70 % мед“ си остава
    /// 70 % мед — иначе всяко пипване по количеството би изисквало и списъкът
    /// да се пише наново.
    /// </summary>
    public static IReadOnlyList<GenPart> Rescale(IReadOnlyList<GenPart> parts, decimal total)
    {
        var was = SplitMath.Round(parts.Sum(p => p.Amount));
        var now = SplitMath.Round(total);

        if (parts.Count == 0) return [];
        if (was == now) return parts;

        // Без предишен сбор няма съотношение: всичко отива в първия ред, за да
        // не изчезне количеството мълчаливо.
        if (was <= 0)
            return [.. parts.Select((part, i) => new GenPart(part.Ewc, i == 0 ? now : 0m))];

        // Самите тонове са съотношението — без процент по пътя. Закръглен до
        // сотни, процентът отклоняваше дяловете с килограми при големи
        // количества (<see cref="Recipe"/> така или иначе дели на сбора).
        return Recipe(now, [.. parts.Select(p => (p.Ewc, p.Amount))]);
    }

    /// <summary>
    /// Проверява поисканото срещу чакащото и срещу самото себе си.
    ///
    /// Празен списък значи, че образуването се държи. Връщат се причини, не
    /// „да/не“ — питането и съобщението са работа на повикващия.
    ///
    /// Купчина без изчерпано количество не е грешка: човек избира двайсет кода
    /// и обявява три от тях, вместо да избира наново.
    /// </summary>
    public static IReadOnlyList<GenIssue> Check(IEnumerable<GenOrder> orders)
    {
        var issues = new List<GenIssue>();
        decimal total = 0;

        foreach (var order in orders)
        {
            if (order.Picks.Any(p => p.Amount < 0))
            {
                issues.Add(new GenIssue(GenProblem.Negative, order.Pile.Ewc, 0m));
                continue;
            }

            var taken = order.Consumed;
            if (taken <= 0) continue;

            if (taken > order.Pile.Available)
            {
                issues.Add(new GenIssue(GenProblem.OverStock, order.Pile.Ewc, order.Pile.Available));
                continue;
            }

            decimal named = 0;
            var blank = false;
            var negative = false;

            foreach (var part in order.Parts)
            {
                var amount = SplitMath.Round(part.Amount);

                if (amount < 0) { negative = true; continue; }
                if (amount == 0) continue;

                if (ValueConverter.NormalizeCode(part.Ewc).Length == 0) { blank = true; continue; }

                named += amount;
            }

            if (negative)
            {
                issues.Add(new GenIssue(GenProblem.Negative, order.Pile.Ewc, 0m));
                continue;
            }

            if (blank)
            {
                issues.Add(new GenIssue(GenProblem.NoCode, order.Pile.Ewc, taken));
                continue;
            }

            named = SplitMath.Round(named);

            // Разминаването е самата грешка на този прозорец: количеството е
            // влязло в склада и трябва да излезе цялото под някакъв код.
            if (named != taken)
            {
                issues.Add(new GenIssue(GenProblem.Mismatch, order.Pile.Ewc, SplitMath.Round(taken - named)));
                continue;
            }

            total += named;
        }

        if (total <= 0) issues.Add(new GenIssue(GenProblem.Nothing, string.Empty, 0m));

        return issues;
    }

    /// <summary>
    /// Сглобява движенията от поисканото.
    ///
    /// Всеки изходен код става ред, а произходът му се наследява от дяловете на
    /// купчината в тяхното съотношение: образуваната мед е от Метал ООД и от
    /// Скрап ЕООД точно толкова, колкото е бил кабелът, от който е излязла.
    ///
    /// <paramref name="perPile"/> решава колко реда излизат, когато два кода се
    /// образуват в едно и също: по подразбиране те се сливат в един ред —
    /// книгата иска код и количество, а не откъде е дошло. С вдигнат флаг всяка
    /// купчина си получава свой ред, когато отчетът трябва да върви купчина по
    /// купчина.
    ///
    /// Редовете без количество и без код отпадат тук, а не при повикващия: „не
    /// тази купчина“ не бива да става движение от нула тона.
    /// </summary>
    public static IReadOnlyList<GenMove> Plan(IEnumerable<GenOrder> orders, bool perPile = false)
    {
        var made = new List<GenMove>();

        foreach (var order in orders)
        {
            var source = Draws(order.Picks);
            if (source.Count == 0) continue;

            var parts = order.Parts
                .Select(part => (Code: ValueConverter.NormalizeCode(part.Ewc), Amount: SplitMath.Round(part.Amount)))
                .Where(part => part.Amount > 0 && part.Code.Length > 0)
                .ToList();

            // Всички изходни кодове НАВЕДНЪЖ, а не всеки поотделно: само така
            // всяка партида дава точно колкото е взето от нея. Поотделно всеки
            // код закръгляше по своему и по партиди оставаха грамове да чакат
            // в „Третиран“, а други грамове сочеха празни партиди.
            var split = Allocation.Split(source, [.. parts.Select(part => part.Amount)]);

            for (var i = 0; i < parts.Count; i++)
                made.Add(new GenMove(parts[i].Code, parts[i].Amount, split[i], order.Pile.Ewc));
        }

        if (perPile)
            return
            [
                .. made
                    .OrderBy(m => m.Ewc, StringComparer.Ordinal)
                    .ThenBy(m => m.Source, StringComparer.Ordinal)
            ];

        var merged = new List<GenMove>();

        foreach (var group in made
                     .GroupBy(m => m.Ewc, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var draws = new Dictionary<long, decimal>();

            foreach (var move in group)
                foreach (var draw in move.Draws)
                    draws[draw.BatchId] = draws.GetValueOrDefault(draw.BatchId) + draw.Amount;

            var sources = group.Select(m => m.Source).Distinct(StringComparer.Ordinal).ToList();

            merged.Add(new GenMove(
                group.Key,
                SplitMath.Round(group.Sum(m => m.Amount)),
                [.. draws.Select(pair => new Draw(pair.Key, SplitMath.Round(pair.Value)))],
                sources.Count == 1 ? sources[0] : string.Empty));
        }

        return merged;
    }
}
