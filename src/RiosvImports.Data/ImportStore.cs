using System.Globalization;
using Microsoft.Data.Sqlite;
using RiosvImports.Core.Localization;

namespace RiosvImports.Data;

/// <summary>Какво ражда едно внасяне — по това се познава какво да се махне.</summary>
public enum ImportTarget
{
    /// <summary>Движение в склада.</summary>
    Move,

    /// <summary>Партида, заведена от приемане в същия файл.</summary>
    Batch,

    /// <summary>Отчет, прочетен от попълнен шаблон.</summary>
    Document
}

/// <summary>
/// Откъде е дошъл файлът.
///
/// Текст, а не число, по същата причина, поради която и произходът на отчета е
/// текст: утре ще има износ и от друга програма, а новият вид трябва да е нов
/// ред в кода, не нова стъпка по базата.
/// </summary>
public static class ImportSource
{
    /// <summary>Дневник на „Деметра“.</summary>
    public const string Demetra = "demetra";

    /// <summary>Наш шаблон за движения.</summary>
    public const string Template = "template";

    /// <summary>Попълнен шаблон на отчет.</summary>
    public const string Report = "report";

    public static string Label(string source) => source switch
    {
        Demetra => Lang.T("Дневник „Деметра“", "Demetra journal"),
        Template => Lang.T("Наш шаблон", "Our template"),
        Report => Lang.T("Отчет от Excel", "Report from Excel"),
        _ => source
    };
}

/// <summary>
/// Едно внасяне и какво още стои от него.
///
/// Броевете са на ЖИВИТЕ записи, а не на записаните тогава: ред, махнат на
/// ръка, вече го няма и не бива да се обещава, че ще си отиде втори път.
/// </summary>
public sealed record ImportRecord(
    long Id,
    long? CompanyId,
    string Source,
    string FileName,
    string Note,
    DateTime CreatedUtc,
    int Moves,
    int Batches,
    int Documents)
{
    /// <summary>Колко записа още стоят зад това внасяне.</summary>
    public int Total => Moves + Batches + Documents;

    /// <summary>Дали от него е останало нещо за махане.</summary>
    public bool Empty => Total == 0;

    public string SourceLabel => ImportSource.Label(Source);

    /// <summary>Какво носи — изписано за човек: „412 движения · 380 партиди“.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (Moves > 0) parts.Add(Lang.T($"{Moves} движения", $"{Moves} movements"));
            if (Batches > 0) parts.Add(Lang.T($"{Batches} партиди", $"{Batches} batches"));
            if (Documents > 0) parts.Add(Lang.T($"{Documents} отчета", $"{Documents} reports"));

            return parts.Count == 0
                ? Lang.T("нищо не е останало", "nothing is left")
                : string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Дневникът на внесените файлове.
///
/// Внасянето е единственото действие, при което в базата влизат стотици редове
/// наведнъж — и единственото, чиято грешка не се поправя на ръка. Същият файл,
/// внесен два пъти, не личи по нито един ред: всеки от тях изглежда като
/// правилен запис и само сборът излиза двоен. Затова се пази не съдържанието
/// на файла, а СЛЕДАТА му: кои записи е родил.
///
/// Тук се пази следата. Какво значи да се махне тя — движение с дяловете му,
/// партида, от която още се черпи — решава <see cref="ImportHistory"/>: това е
/// работа на склада, а хранилището не знае правилата му.
/// </summary>
public sealed class ImportStore(SqliteConnection connection)
{
    private readonly SqliteConnection _connection = connection;

    // ------------------------------------------------------------- четене

    /// <summary>
    /// Внесеното, най-новото отгоре.
    ///
    /// Броевете идват от свързване с истинските таблици, а не от броя връзки:
    /// записът може да е махнат и без нас — от таблицата, от изтрита фирма, от
    /// махнат отчет — и тогава връзката към него е спомен, не задължение.
    /// </summary>
    public IReadOnlyList<ImportRecord> List(long? companyId = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT i.id, i.company_id, i.source, i.file_name, i.note, i.created_utc,
                   (SELECT COUNT(*) FROM import_items t JOIN ledger_moves m ON m.id = t.record_id
                     WHERE t.import_id = i.id AND t.target = 'move'),
                   (SELECT COUNT(*) FROM import_items t JOIN batches b ON b.id = t.record_id
                     WHERE t.import_id = i.id AND t.target = 'batch'),
                   (SELECT COUNT(*) FROM import_items t JOIN documents d ON d.id = t.record_id
                     WHERE t.import_id = i.id AND t.target = 'document')
            FROM imports i
            WHERE $company IS NULL OR i.company_id = $company
            ORDER BY i.created_utc DESC, i.id DESC;
            """;

        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value);

        var imports = new List<ImportRecord>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
            imports.Add(new ImportRecord(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ReadTime(reader.GetString(5)),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8)));

        return imports;
    }

    public ImportRecord? Find(long importId) => List().FirstOrDefault(i => i.Id == importId);

    /// <summary>Номерата на записите от един вид, родени от това внасяне.</summary>
    public IReadOnlyList<long> Items(long importId, ImportTarget target)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT record_id FROM import_items
            WHERE import_id = $import AND target = $target
            ORDER BY record_id;
            """;

        command.Parameters.AddWithValue("$import", importId);
        command.Parameters.AddWithValue("$target", Name(target));

        var ids = new List<long>();
        using var reader = command.ExecuteReader();

        while (reader.Read()) ids.Add(reader.GetInt64(0));

        return ids;
    }

    /// <summary>
    /// От кое внасяне е дошъл един запис.
    ///
    /// Питането е от другата страна: човек гледа ред и иска да знае откъде се е
    /// появил. Оттам нататък махането е на цялото внасяне, не на реда.
    /// </summary>
    public long? Of(ImportTarget target, long recordId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT import_id FROM import_items
            WHERE target = $target AND record_id = $record
            ORDER BY import_id DESC LIMIT 1;
            """;

        command.Parameters.AddWithValue("$target", Name(target));
        command.Parameters.AddWithValue("$record", recordId);

        return command.ExecuteScalar() is { } value and not DBNull ? Convert.ToInt64(value) : null;
    }

    // ------------------------------------------------------------- писане

    /// <summary>
    /// Отваря внасяне и връща номера му.
    ///
    /// Заглавието се записва ПРЕДИ записите: така дори прекъснато по средата
    /// внасяне оставя следа, а половин внесен файл е точно случаят, в който
    /// следата трябва да я има.
    /// </summary>
    public long Begin(long? companyId, string source, string fileName, string note = "")
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO imports (company_id, source, file_name, note, created_utc)
            VALUES ($company, $source, $file, $note, $now)
            RETURNING id;
            """;

        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", source.Trim());
        command.Parameters.AddWithValue("$file", Path.GetFileName(fileName ?? string.Empty));
        command.Parameters.AddWithValue("$note", note.Trim());
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>Отбелязва, че този запис е дошъл от това внасяне.</summary>
    public void Track(long importId, ImportTarget target, long recordId) =>
        Track(importId, target, [recordId]);

    /// <summary>
    /// Същото за цял куп наведнъж — в една транзакция.
    ///
    /// Петстотин обиколки до базата за петстотин реда правят от бърз импорт
    /// бавен, а следата е цяла или никаква.
    /// </summary>
    public void Track(long importId, ImportTarget target, IEnumerable<long> recordIds)
    {
        var ids = recordIds.Distinct().ToList();
        if (ids.Count == 0) return;

        using var transaction = _connection.BeginTransaction();

        foreach (var recordId in ids)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;

            // Повторно отбелязване не е грешка: един ред може да мине два пъти
            // през записа, ако човекът е записал, погледнал и записал пак.
            command.CommandText = """
                INSERT OR IGNORE INTO import_items (import_id, target, record_id)
                VALUES ($import, $target, $record);
                """;

            command.Parameters.AddWithValue("$import", importId);
            command.Parameters.AddWithValue("$target", Name(target));
            command.Parameters.AddWithValue("$record", recordId);

            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Маха самата следа. Записите зад нея НЕ се пипат.
    ///
    /// Ползва се от <see cref="ImportHistory"/>, след като записите вече са
    /// махнати, и от внасяне, което не е родило нищо: празно заглавие в списъка
    /// е покана да се натисне бутон, който няма какво да направи.
    /// </summary>
    public void Forget(long importId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM imports WHERE id = $id;";
        command.Parameters.AddWithValue("$id", importId);
        command.ExecuteNonQuery();
    }

    // ------------------------------------------------------------- помощни

    internal static string Name(ImportTarget target) => target switch
    {
        ImportTarget.Move => "move",
        ImportTarget.Batch => "batch",
        _ => "document"
    };

    private static DateTime ReadTime(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var moment)
            ? moment
            : DateTime.UtcNow;
}
