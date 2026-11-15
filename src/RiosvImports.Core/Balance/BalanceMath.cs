using System.Globalization;
using RiosvImports.Core.Values;

namespace RiosvImports.Core.Balance;

/// <summary>
/// Сметката зад изгледа „Баланс“.
///
/// Нищо тук не знае за прозорци, за база и за Excel — влизат движения и
/// начални количества, излиза таблица. Така правилото „образуваният се събира,
/// предаденият се вади“ се проверява с тест, а не с оглед на екрана.
/// </summary>
public static class BalanceMath
{
    /// <summary>Месеците в годината. Колоните на таблицата са точно толкова.</summary>
    public const int Months = 12;

    /// <summary>
    /// Превръща записан ред в движение.
    ///
    /// Връща null за всичко, което не може да се брои: ред без код, ред с
    /// нечетима дата, ред от друга година, ред без количество. Такъв ред не е
    /// грешка на баланса — той е недовършен отчет и ще падне при проверката
    /// му; тук просто не участва.
    /// </summary>
    public static BalanceEntry? Entry(BalanceSheet sheet, IReadOnlyDictionary<string, string> row, int year)
    {
        if (!row.TryGetValue("ewc", out var raw)) return null;

        var ewc = ValueConverter.NormalizeCode(raw);
        if (ewc.Length == 0) return null;

        if (!row.TryGetValue("date_record", out var written)) return null;
        if (!ValueConverter.TryNormalizeDate(written, out var normalized)) return null;

        if (!DateTime.TryParseExact(normalized, ValueConverter.BulgarianDateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        if (date.Year != year) return null;

        if (ValueConverter.TryAmount(row.GetValueOrDefault("amount_ton")) is not { } amount) return null;

        return new BalanceEntry(sheet, ewc, date.Month, amount);
    }

    /// <summary>
    /// Сглобява таблицата.
    ///
    /// <paramref name="sheet"/> избира изгледа. Един раздел се показва такъв,
    /// какъвто е записан — количествата са положителни числа и знак не се
    /// налага отвън. Кръстосаният изглед (null) събира образуваното и вади
    /// предаденото, а началните количества застават пред януари.
    ///
    /// Началните количества важат само за кръстосания изглед: те са налично
    /// количество, а един раздел поотделно не е наличност, а движение.
    /// </summary>
    public static BalanceTable Build(
        IEnumerable<BalanceEntry> entries,
        IReadOnlyDictionary<string, decimal>? openings = null,
        BalanceSheet? sheet = null)
    {
        var cross = sheet is null;
        var lines = new Dictionary<string, decimal[]>(StringComparer.Ordinal);
        var opening = new Dictionary<string, decimal>(StringComparer.Ordinal);

        if (cross && openings is not null)
        {
            foreach (var (ewc, amount) in openings)
            {
                var code = ValueConverter.NormalizeCode(ewc);
                if (code.Length == 0) continue;

                opening[code] = opening.GetValueOrDefault(code) + amount;
                Row(lines, code);
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Month is < 1 or > Months) continue;

            var sign = cross ? BalanceSheets.CrossSign(entry.Sheet) : entry.Sheet == sheet ? 1 : 0;
            if (sign == 0) continue;

            Row(lines, entry.Ewc)[entry.Month - 1] += sign * entry.Amount;
        }

        var built = lines
            .Select(pair => Line(pair.Key, opening.GetValueOrDefault(pair.Key), pair.Value))
            .Where(line => !line.IsEmpty)
            .OrderBy(line => line.Ewc, StringComparer.Ordinal)
            .ToList();

        return new BalanceTable(built, Sum(built));
    }

    /// <summary>
    /// Кои клетки докосват тези движения — код и месец.
    ///
    /// Ползва се за оцветяването на включената незавършена книга: вместо да се
    /// смята таблицата два пъти и да се сравнява, се пита самата чернова кои
    /// клетки изобщо е пипнала. Отговорът е същият и струва една обиколка.
    /// </summary>
    public static HashSet<(string Ewc, int Month)> Touched(
        IEnumerable<BalanceEntry> entries, BalanceSheet? sheet)
    {
        var touched = new HashSet<(string, int)>();

        foreach (var entry in entries)
        {
            if (entry.Month is < 1 or > Months) continue;
            if (entry.Amount == 0) continue;

            var counts = sheet is null
                ? BalanceSheets.Crosses(entry.Sheet)
                : entry.Sheet == sheet;

            if (counts) touched.Add((entry.Ewc, entry.Month));
        }

        return touched;
    }

    /// <summary>
    /// Свежда таблицата до няколко месеца.
    ///
    /// Не отрязване, а превъртане: началното количество на периода е салдото
    /// ПРЕДИ първия му месец, а не онова от януари. Иначе „второ тримесечие“
    /// би тръгвало от нула и всяко число в него щеше да е вярно само по
    /// себе си.
    ///
    /// Извън прозореца движенията се занулят, вместо редовете да се режат:
    /// така всичко надолу — мрежата, таблото, изнасянето, оцветяването —
    /// работи с една и съща таблица и не знае, че е гледан период.
    /// </summary>
    public static BalanceTable Window(BalanceTable table, int from, int to)
    {
        from = Math.Clamp(from, 1, Months);
        to = Math.Clamp(to, from, Months);

        if (from == 1 && to == Months) return table;

        var lines = table.Lines
            .Select(line => Cut(line, from, to))
            .Where(line => !line.IsEmpty)
            .ToList();

        return new BalanceTable(lines, Cut(table.Totals, from, to));
    }

    /// <summary>
    /// Свежда таблицата до един код.
    ///
    /// Сборният ред става самият ред: при един код „общо“ и „този код“ са едно
    /// и също число, а показани две отделно приличат на две различни.
    /// </summary>
    public static BalanceTable Only(BalanceTable table, string ewc)
    {
        if (ewc.Length == 0) return table;

        var lines = table.Lines.Where(line => line.Ewc == ewc).ToList();

        return lines.Count == 0
            ? Build([])
            : new BalanceTable(lines, lines[0]);
    }

    private static BalanceLine Cut(BalanceLine line, int from, int to)
    {
        var opening = from == 1 ? line.Opening : line.Running[from - 2];
        var movement = new decimal[Months];

        for (var month = from; month <= to; month++) movement[month - 1] = line.Movement[month - 1];

        return Line(line.Ewc, opening, movement);
    }

    private static decimal[] Row(Dictionary<string, decimal[]> lines, string ewc)
    {
        if (lines.TryGetValue(ewc, out var months)) return months;

        months = new decimal[Months];
        lines[ewc] = months;

        return months;
    }

    /// <summary>Движението и натрупаното от него — второто е сборът на първото отляво.</summary>
    private static BalanceLine Line(string ewc, decimal opening, decimal[] movement)
    {
        var running = new decimal[Months];
        var carried = opening;

        for (var i = 0; i < Months; i++)
        {
            carried += movement[i];
            running[i] = carried;
        }

        return new BalanceLine(ewc, opening, movement, running);
    }

    /// <summary>
    /// Сборният ред. Смята се от движенията, а не от натрупаното: сборът на
    /// салдата по кодове в даден месец не е салдото на площадката, а сборът
    /// на нещо, което вече е събрано веднъж.
    /// </summary>
    private static BalanceLine Sum(IReadOnlyList<BalanceLine> lines)
    {
        var movement = new decimal[Months];

        foreach (var line in lines)
            for (var i = 0; i < Months; i++)
                movement[i] += line.Movement[i];

        return Line(string.Empty, lines.Sum(l => l.Opening), movement);
    }
}
