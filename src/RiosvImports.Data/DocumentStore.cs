using System.Text.Json;
using Microsoft.Data.Sqlite;
using RiosvImports.Core.Localization;

namespace RiosvImports.Data;

/// <summary>Докъде е стигнал един отчет.</summary>
public enum DocumentStatus
{
    /// <summary>Попълва се.</summary>
    Draft,

    /// <summary>Проверен без грешки, но още неподписан.</summary>
    Checked,

    /// <summary>Подписан и записан — готов за качване.</summary>
    Signed
}

/// <summary>
/// Откъде е дошъл отчетът.
///
/// Пази се, защото пътят му нататък е различен: съставеният на ръка се
/// проверява цял, внесеният от Excel носи чужди грешки, а бъдещият, съставен
/// от друга програма, ще носи и своята версия. Стойността е текст в базата,
/// за да не иска нова стъпка при нов произход.
/// </summary>
public enum DocumentOrigin
{
    /// <summary>Създаден в програмата.</summary>
    Manual,

    /// <summary>Внесен от попълнен Excel шаблон.</summary>
    Excel,

    /// <summary>Съставен от друга програма. Още няма кой да го създава.</summary>
    External,

    /// <summary>
    /// Съставен от склада.
    ///
    /// Не е „в програмата“, макар да е направен в нея: редовете му не са
    /// писани на ръка, а са проекция на движенията. Разликата има значение при
    /// поправка — поправеният тук ред се разминава със склада, от който е
    /// излязъл, и това трябва да се вижда.
    /// </summary>
    Ledger
}

/// <summary>Отчет в списъка — без съдържанието на редовете.</summary>
public sealed record DocumentSummary(
    long Id,
    string SchemaKey,
    string Report,
    string Title,
    DocumentStatus Status,
    DocumentOrigin Origin,
    long? CompanyId,
    DateTime UpdatedUtc,
    string? OutputPath)
{
    public string StatusLabel => Status switch
    {
        DocumentStatus.Signed => Lang.T("Подписан", "Signed"),
        DocumentStatus.Checked => Lang.T("Проверен", "Checked"),
        _ => Lang.T("Чернова", "Draft")
    };

    public string OriginLabel => Origin switch
    {
        DocumentOrigin.Excel => Lang.T("от Excel", "from Excel"),
        DocumentOrigin.External => Lang.T("от друга програма", "from another program"),
        DocumentOrigin.Ledger => Lang.T("от склада", "from the store"),
        _ => Lang.T("в програмата", "in the program")
    };
}

/// <summary>Един ред от подтаблица: имената на колоните сочат към текста в тях.</summary>
public sealed record SheetRow(long Id, int Ordinal, Dictionary<string, string> Values);

/// <summary>
/// Отчетите и редовете им.
///
/// Редовете се пазят като JSON нарочно — виж бележката в <see cref="Workspace"/>.
/// </summary>
public sealed class DocumentStore(SqliteConnection connection)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly SqliteConnection _connection = connection;

    // ------------------------------------------------------------- четене

    public IReadOnlyList<DocumentSummary> List()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, schema_key, report, title, status, origin, company_id, updated_utc, output_path
            FROM documents
            ORDER BY updated_utc DESC;
            """;

        var result = new List<DocumentSummary>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            result.Add(new DocumentSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ParseStatus(reader.GetString(4)),
                ParseOrigin(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                DateTime.Parse(reader.GetString(7)).ToUniversalTime(),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return result;
    }

    public DocumentSummary? Find(long id) => List().FirstOrDefault(d => d.Id == id);

    /// <summary>Заглавните стойности: година, ЕИК, тримесечие и подобни.</summary>
    public Dictionary<string, string> ReadHeader(long documentId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT header FROM documents WHERE id = $id;";
        command.Parameters.AddWithValue("$id", documentId);

        var json = command.ExecuteScalar() as string;
        return Deserialize(json);
    }

    /// <summary>Редовете на един възел, подредени както ги е оставил потребителят.</summary>
    public List<SheetRow> ReadRows(long documentId, string node)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, ordinal, data FROM sheet_rows
            WHERE document_id = $doc AND node = $node
            ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$doc", documentId);
        command.Parameters.AddWithValue("$node", node);

        var rows = new List<SheetRow>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
            rows.Add(new SheetRow(reader.GetInt64(0), reader.GetInt32(1), Deserialize(reader.GetString(2))));

        return rows;
    }

    // ------------------------------------------------------------- писане

    public long Create(
        string schemaKey,
        string report,
        string title,
        Dictionary<string, string>? header = null,
        DocumentOrigin origin = DocumentOrigin.Manual,
        long? companyId = null)
    {
        var now = DateTime.UtcNow.ToString("O");

        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents
                (schema_key, report, title, status, origin, company_id, header, created_utc, updated_utc)
            VALUES ($schema, $report, $title, 'draft', $origin, $company, $header, $now, $now)
            RETURNING id;
            """;

        command.Parameters.AddWithValue("$schema", schemaKey);
        command.Parameters.AddWithValue("$report", report);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$origin", origin.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value);
        command.Parameters.AddWithValue("$header", JsonSerializer.Serialize(header ?? [], Json));
        command.Parameters.AddWithValue("$now", now);

        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>Сменя фирмата на отчета — прави се от падащия списък в редактора.</summary>
    public void SetCompany(long documentId, long? companyId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "UPDATE documents SET company_id = $company, updated_utc = $now WHERE id = $id;";

        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", documentId);

        command.ExecuteNonQuery();
    }

    public void Rename(long documentId, string title) =>
        Update(documentId, "title = $value", title);

    public void SaveHeader(long documentId, Dictionary<string, string> header) =>
        Update(documentId, "header = $value", JsonSerializer.Serialize(header, Json));

    public void SetStatus(long documentId, DocumentStatus status, string? outputPath = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE documents
            SET status = $status,
                output_path = COALESCE($path, output_path),
                signed_utc = CASE WHEN $status = 'signed' THEN $now ELSE signed_utc END,
                updated_utc = $now
            WHERE id = $id;
            """;

        command.Parameters.AddWithValue("$status", status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$path", (object?)outputPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", documentId);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Записва всички редове на един възел наведнъж.
    ///
    /// Изтрива и записва наново вместо да сверява ред по ред: таблиците са
    /// десетки редове, а разликата в бързина е незабележима срещу
    /// съществената разлика в това колко неща могат да се объркат.
    /// </summary>
    public void SaveRows(long documentId, string node, IReadOnlyList<Dictionary<string, string>> rows)
    {
        using var transaction = _connection.BeginTransaction();

        using (var clear = _connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM sheet_rows WHERE document_id = $doc AND node = $node;";
            clear.Parameters.AddWithValue("$doc", documentId);
            clear.Parameters.AddWithValue("$node", node);
            clear.ExecuteNonQuery();
        }

        using (var insert = _connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO sheet_rows (document_id, node, ordinal, data)
                VALUES ($doc, $node, $ordinal, $data);
                """;

            var ordinal = insert.CreateParameter();
            ordinal.ParameterName = "$ordinal";
            var data = insert.CreateParameter();
            data.ParameterName = "$data";

            insert.Parameters.AddWithValue("$doc", documentId);
            insert.Parameters.AddWithValue("$node", node);
            insert.Parameters.Add(ordinal);
            insert.Parameters.Add(data);

            for (var i = 0; i < rows.Count; i++)
            {
                // Празните редове не се пазят: те са само място за писане в мрежата.
                if (rows[i].Values.All(string.IsNullOrWhiteSpace)) continue;

                ordinal.Value = i;
                data.Value = JsonSerializer.Serialize(rows[i], Json);
                insert.ExecuteNonQuery();
            }
        }

        Touch(documentId, transaction);
        transaction.Commit();
    }

    public void Delete(long documentId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM documents WHERE id = $id;";
        command.Parameters.AddWithValue("$id", documentId);
        command.ExecuteNonQuery();
    }

    /// <summary>Прави копие с всички редове — най-бързият начин за отчет за следващ период.</summary>
    public long Duplicate(long documentId, string title)
    {
        using var transaction = _connection.BeginTransaction();

        long created;

        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO documents
                    (schema_key, report, title, status, origin, company_id, header, created_utc, updated_utc)
                SELECT schema_key, report, $title, 'draft', origin, company_id, header, $now, $now
                FROM documents WHERE id = $id
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$id", documentId);

            created = Convert.ToInt64(command.ExecuteScalar());
        }

        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO sheet_rows (document_id, node, ordinal, data)
                SELECT $new, node, ordinal, data FROM sheet_rows WHERE document_id = $old;
                """;
            command.Parameters.AddWithValue("$new", created);
            command.Parameters.AddWithValue("$old", documentId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return created;
    }

    // ------------------------------------------------------------- помощни

    private void Update(long documentId, string assignment, string value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"UPDATE documents SET {assignment}, updated_utc = $now WHERE id = $id;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", documentId);
        command.ExecuteNonQuery();
    }

    private void Touch(long documentId, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE documents SET updated_utc = $now WHERE id = $id;";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", documentId);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, string> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    private static DocumentOrigin ParseOrigin(string value) => value switch
    {
        "excel" => DocumentOrigin.Excel,
        "external" => DocumentOrigin.External,
        "ledger" => DocumentOrigin.Ledger,
        _ => DocumentOrigin.Manual
    };

    private static DocumentStatus ParseStatus(string value) => value switch
    {
        "signed" => DocumentStatus.Signed,
        "checked" => DocumentStatus.Checked,
        _ => DocumentStatus.Draft
    };
}
