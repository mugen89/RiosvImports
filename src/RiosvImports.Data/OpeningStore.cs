using System.Globalization;
using Microsoft.Data.Sqlite;
using RiosvImports.Core.Values;

namespace RiosvImports.Data;

/// <summary>Колко има на площадката от този код в началото на годината.</summary>
public sealed record Opening(long CompanyId, int Year, string Ewc, decimal Amount);

/// <summary>
/// Началните количества за баланса.
///
/// Водят се по фирма, защото и балансът се води по фирма: две дружества на
/// един счетоводител нямат общ склад, а сборният изглед се получава чрез
/// събиране, не чрез смесване в базата.
///
/// Количеството се пази като текст с точка за десетичен знак — SQLite няма
/// точен десетичен тип, а числата тук се събират и вадят. Обратното четене
/// минава през същия разбор, който приема и „12,5“: файлът, от който са
/// дошли, е бил попълван на ръка.
/// </summary>
public sealed class OpeningStore(SqliteConnection connection)
{
    private readonly SqliteConnection _connection = connection;

    // ------------------------------------------------------------- четене

    /// <summary>Всичко въведено — ползва се при изнасяне обратно в шаблона.</summary>
    public IReadOnlyList<Opening> All() => Query(null, null);

    /// <summary>Началните количества за една година и избраните фирми.</summary>
    public IReadOnlyList<Opening> List(int year, IReadOnlyCollection<long>? companies = null) =>
        Query(year, companies);

    /// <summary>
    /// Сборът по код за избраните фирми — точно това, което иска балансът.
    ///
    /// Събира се тук, а не на екрана: сборният изглед на няколко фирми е
    /// сбор и на началните им количества, а иначе всяка страница, която ги
    /// показва, би трябвало да помни това сама.
    /// </summary>
    public Dictionary<string, decimal> Totals(int year, IReadOnlyCollection<long>? companies = null)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var opening in List(year, companies))
            totals[opening.Ewc] = totals.GetValueOrDefault(opening.Ewc) + opening.Amount;

        return totals;
    }

    private List<Opening> Query(int? year, IReadOnlyCollection<long>? companies)
    {
        var result = new List<Opening>();

        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT company_id, year, ewc, amount FROM opening_balances
            WHERE ($year IS NULL OR year = $year)
            ORDER BY year, company_id, ewc;
            """;
        command.Parameters.AddWithValue("$year", (object?)year ?? DBNull.Value);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var companyId = reader.GetInt64(0);

            // Ситото по фирми е тук, а не в заявката: списъкът идва от екрана
            // и е с променлива дължина, а SQLite няма как да приеме масив.
            if (companies is { Count: > 0 } && !companies.Contains(companyId)) continue;

            result.Add(new Opening(
                companyId,
                reader.GetInt32(1),
                reader.GetString(2),
                ValueConverter.TryAmount(reader.GetString(3)) ?? 0m));
        }

        return result;
    }

    // ------------------------------------------------------------- писане

    /// <summary>Слага или заменя едно количество. Нула изтрива реда.</summary>
    public void Put(long companyId, int year, string ewc, decimal amount)
    {
        var code = ValueConverter.NormalizeCode(ewc);
        if (code.Length == 0) return;

        if (amount == 0)
        {
            Remove(companyId, year, code);
            return;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO opening_balances (company_id, year, ewc, amount, updated_utc)
            VALUES ($company, $year, $ewc, $amount, $now)
            ON CONFLICT(company_id, year, ewc)
            DO UPDATE SET amount = excluded.amount, updated_utc = excluded.updated_utc;
            """;

        Bind(command, companyId, year, code);
        command.Parameters.AddWithValue("$amount", amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        command.ExecuteNonQuery();
    }

    public void Remove(long companyId, int year, string ewc)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "DELETE FROM opening_balances WHERE company_id = $company AND year = $year AND ewc = $ewc;";

        Bind(command, companyId, year, ValueConverter.NormalizeCode(ewc));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Внася цял файл наведнъж.
    ///
    /// Заместване, а не добавяне: човек, който тегли шаблона, поправя едно
    /// число и го връща, очаква да види поправеното, а не двойно количество.
    /// Затова годините, които файлът споменава, се изчистват преди записа —
    /// но само те и само за фирмите в него. Година, за която файлът мълчи,
    /// не се пипа.
    ///
    /// Всичко е в една транзакция: наполовина внесен баланс е по-лош от
    /// невнесен.
    /// </summary>
    public int Replace(IReadOnlyList<Opening> openings)
    {
        if (openings.Count == 0) return 0;

        using var transaction = _connection.BeginTransaction();

        foreach (var (companyId, year) in openings.Select(o => (o.CompanyId, o.Year)).Distinct())
        {
            using var clear = _connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText =
                "DELETE FROM opening_balances WHERE company_id = $company AND year = $year;";
            clear.Parameters.AddWithValue("$company", companyId);
            clear.Parameters.AddWithValue("$year", year);
            clear.ExecuteNonQuery();
        }

        // Два реда за един и същ код се събират: файлът е човешки, а не
        // извадка от база, и повторение в него значи „и това още“.
        var merged = new Dictionary<(long, int, string), decimal>();

        foreach (var opening in openings)
        {
            var code = ValueConverter.NormalizeCode(opening.Ewc);
            if (code.Length == 0) continue;

            var key = (opening.CompanyId, opening.Year, code);
            merged[key] = merged.GetValueOrDefault(key) + opening.Amount;
        }

        var written = 0;

        foreach (var (key, amount) in merged)
        {
            if (amount == 0) continue;

            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO opening_balances (company_id, year, ewc, amount, updated_utc)
                VALUES ($company, $year, $ewc, $amount, $now);
                """;

            Bind(insert, key.Item1, key.Item2, key.Item3);
            insert.Parameters.AddWithValue("$amount", amount.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

            insert.ExecuteNonQuery();
            written++;
        }

        transaction.Commit();

        return written;
    }

    private static void Bind(SqliteCommand command, long companyId, int year, string ewc)
    {
        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$ewc", ewc);
    }
}
