using System.Text.Json;
using Microsoft.Data.Sqlite;
using RiosvImports.Core.Values;

namespace RiosvImports.Data;

/// <summary>Един запазен кратък списък, както се показва в „Списъци“.</summary>
public sealed record ValueList(
    long Id,
    long CompanyId,
    string SchemaKey,
    string Report,
    string Node,
    string Column,
    IReadOnlyList<string> Allowed,
    bool Enabled,
    DateTime Updated)
{
    /// <summary>Кое ниво описва обхватът — за надписа в списъка и за подредбата.</summary>
    public ValueListScope Scope =>
        Node.Length > 0 ? ValueListScope.Sheet :
        Report.Length > 0 ? ValueListScope.Report :
        ValueListScope.All;
}

/// <summary>Докъде стига един списък. Подредено от най-широкото към най-тясното.</summary>
public enum ValueListScope
{
    All,
    Report,
    Sheet
}

/// <summary>
/// Кратките списъци: какво да се предлага в менютата на всяка фирма.
///
/// Хранилището само чете и пише. Кой ред печели, когато има и по-широк, и
/// по-тесен, решава <see cref="ValueLimits"/> в Core — там правилото се
/// проверява с тест, без база и без прозорци.
/// </summary>
public sealed class ValueListStore(SqliteConnection connection, SettingsStore settings)
{
    private readonly SqliteConnection _connection = connection;
    private readonly SettingsStore _settings = settings;

    private const string Columns =
        "id, company_id, schema_key, report, node, column_name, allowed, enabled, updated_utc";

    public IReadOnlyList<ValueList> List(long? companyId = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = companyId is null
            ? $"SELECT {Columns} FROM value_lists ORDER BY company_id, schema_key, report, node, column_name;"
            : $"SELECT {Columns} FROM value_lists WHERE company_id = $id ORDER BY schema_key, report, node, column_name;";

        if (companyId is not null) command.Parameters.AddWithValue("$id", companyId);

        var result = new List<ValueList>();
        using var reader = command.ExecuteReader();

        while (reader.Read()) result.Add(Read(reader));

        return result;
    }

    public ValueList? Find(long id) =>
        List().FirstOrDefault(entry => entry.Id == id);

    public int Count()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM value_lists WHERE enabled = 1;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Списъците на една фирма за един вид отчетност, готови за питане.
    ///
    /// Без фирма няма ограничение: отчет, който още не е закачен за фирма, се
    /// попълва с пълните менюта. Изключените редове не влизат — изключването е
    /// точно затова: човек да види пълния списък веднъж, без да си трие работата.
    /// </summary>
    public ValueLimits LimitsFor(long? companyId, string schemaKey)
    {
        if (companyId is null) return ValueLimits.None;

        var entries = List(companyId)
            .Where(entry => entry.Enabled && entry.SchemaKey == schemaKey)
            .Select(entry => new ValueListEntry(entry.Report, entry.Node, entry.Column, entry.Allowed));

        return new ValueLimits(entries, Fills(companyId.Value));
    }

    // ------------------------------------------ останалата една стойност

    /// <summary>
    /// Попълва ли се само полето, което кратък списък е свел до една стойност.
    ///
    /// Настройка на фирма, а не обща: една площадка предава на един получател с
    /// една дейност и цял живот преписва едно и също, а съседната работи с
    /// десет — при първата това е спестено писане, при втората би било попълване
    /// наслуки.
    ///
    /// Изключена по подразбиране. Полето, попълнено без човек да го е пипал, е
    /// изненада за този, който не я е поискал — а стойността оттам нататък
    /// тръгва към НИСО като негова. Какво точно значи „останала една“ решава
    /// <see cref="ValueLimits.Fill"/>.
    /// </summary>
    public bool Fills(long companyId) => _settings.GetBool(FillKey(companyId), false);

    public void SetFills(long companyId, bool on) => _settings.Set(FillKey(companyId), on);

    private static string FillKey(long companyId) =>
        $"{SettingsStore.Keys.ListAutoFill}.{companyId}";

    /// <summary>
    /// Записва списък. Втори за същото поле в същия обхват замества първия —
    /// така го иска и ключът в базата.
    /// </summary>
    public void Save(
        long companyId,
        string schemaKey,
        string report,
        string node,
        string column,
        IReadOnlyList<string> allowed,
        bool enabled = true)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO value_lists
                (company_id, schema_key, report, node, column_name, allowed, enabled, updated_utc)
            VALUES ($company, $schema, $report, $node, $column, $allowed, $enabled, $now)
            ON CONFLICT(company_id, schema_key, report, node, column_name) DO UPDATE SET
                allowed = excluded.allowed,
                enabled = excluded.enabled,
                updated_utc = excluded.updated_utc;
            """;

        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$schema", schemaKey);
        command.Parameters.AddWithValue("$report", report);
        command.Parameters.AddWithValue("$node", node);
        command.Parameters.AddWithValue("$column", column);
        command.Parameters.AddWithValue("$allowed", JsonSerializer.Serialize(allowed));
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        command.ExecuteNonQuery();
    }

    public void SetEnabled(long id, bool enabled)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE value_lists SET enabled = $on, updated_utc = $now WHERE id = $id;";
        command.Parameters.AddWithValue("$on", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);

        command.ExecuteNonQuery();
    }

    public void Remove(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM value_lists WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        command.ExecuteNonQuery();
    }

    private static ValueList Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        Parse(reader.GetString(6)),
        reader.GetInt64(7) != 0,
        DateTime.TryParse(reader.GetString(8), out var when) ? when : DateTime.MinValue);

    /// <summary>
    /// Развален JSON не бива да събаря екрана: списък, който не се чете, е
    /// празен списък, а празният не ограничава нищо.
    /// </summary>
    private static IReadOnlyList<string> Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
