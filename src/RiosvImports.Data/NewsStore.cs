using Microsoft.Data.Sqlite;

namespace RiosvImports.Data;

/// <summary>Колко настойчиво да се покаже съобщението.</summary>
public enum NewsSeverity
{
    /// <summary>Стои в списъка на началния екран.</summary>
    Info,

    /// <summary>Стои открояено — например наближаващ срок.</summary>
    Important,

    /// <summary>Изскача при стартиране — например сгрешено издание.</summary>
    Critical
}

/// <summary>
/// Едно съобщение. Връзката е по избор: „Прочети още“ води там, а без нея
/// копче няма.
/// </summary>
public sealed record NewsItem(
    string Id,
    DateTime Published,
    NewsSeverity Severity,
    string Title,
    string Body,
    bool Seen,
    string Link = "");

/// <summary>
/// Съобщенията от услугата.
///
/// Идват като обикновен файл, който програмата тегли при стартиране, и се
/// запомнят тук, за да се четат и без връзка и за да не се показва едно и
/// също съобщение по два пъти.
///
/// Файлът е истината: каквото е махнато от него, си отива и оттук. Иначе
/// поправено или оттеглено съобщение („изданието 1.3.0 има грешка“, вече
/// поправена) би седяло на началния екран завинаги.
/// </summary>
public sealed class NewsStore(SqliteConnection connection)
{
    /// <summary>
    /// Съобщенията, които програмата пише сама — „Обновено до …“. Файлът на
    /// услугата не ги познава и опресняването не ги маха.
    /// </summary>
    public const string LocalPrefix = "local:";

    private readonly SqliteConnection _connection = connection;

    /// <summary>Видимите съобщения, най-новото отгоре. Скритите не влизат.</summary>
    public IReadOnlyList<NewsItem> List(int limit = 20)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, published, severity, title, body, seen_utc, link
            FROM news WHERE hidden_utc IS NULL
            ORDER BY published DESC, id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var items = new List<NewsItem>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(new NewsItem(
                reader.GetString(0),
                DateTime.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                Enum.TryParse<NewsSeverity>(reader.GetString(2), ignoreCase: true, out var severity)
                    ? severity
                    : NewsSeverity.Info,
                reader.GetString(3),
                reader.GetString(4),
                !reader.IsDBNull(5),
                reader.GetString(6)));
        }

        return items;
    }

    /// <summary>Непрочетените и нескрити — броячът на „Начало“ в лентата.</summary>
    public int UnseenCount()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM news WHERE seen_utc IS NULL AND hidden_utc IS NULL;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Прибавя съобщение, ако още го няма. Съществуващите не се пипат —
    /// иначе повторното теглене би ги показало като непрочетени.
    /// </summary>
    public void Add(NewsItem item)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO news (id, published, severity, title, body, link)
            VALUES ($id, $published, $severity, $title, $body, $link)
            ON CONFLICT(id) DO NOTHING;
            """;

        Bind(command, item);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Привежда съобщенията към току-що изтегления файл.
    ///
    /// Новото влиза непрочетено; заварено със същия белег получава новия
    /// текст, но остава прочетено и скрито, ако е било — поправена запетая не
    /// е ново съобщение. Махнатото от файла се маха и оттук, освен местните.
    ///
    /// Връща колко съобщения са нови.
    /// </summary>
    public int Sync(IReadOnlyCollection<NewsItem> feed)
    {
        using var transaction = _connection.BeginTransaction();

        var added = 0;

        foreach (var item in feed)
        {
            using (var exists = _connection.CreateCommand())
            {
                exists.Transaction = transaction;
                exists.CommandText = "SELECT COUNT(*) FROM news WHERE id = $id;";
                exists.Parameters.AddWithValue("$id", item.Id);

                if (Convert.ToInt32(exists.ExecuteScalar()) == 0) added++;
            }

            using var upsert = _connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO news (id, published, severity, title, body, link)
                VALUES ($id, $published, $severity, $title, $body, $link)
                ON CONFLICT(id) DO UPDATE SET
                    published = excluded.published,
                    severity  = excluded.severity,
                    title     = excluded.title,
                    body      = excluded.body,
                    link      = excluded.link;
                """;

            Bind(upsert, item);
            upsert.ExecuteNonQuery();
        }

        using (var remove = _connection.CreateCommand())
        {
            remove.Transaction = transaction;

            // По параметър на белег, а не с низ: белезите идват отвън.
            var names = feed.Select((_, index) => "$k" + index).ToList();

            remove.CommandText =
                "DELETE FROM news WHERE substr(id, 1, $prefixLength) <> $prefix" +
                (names.Count > 0 ? $" AND id NOT IN ({string.Join(", ", names)})" : string.Empty) + ";";

            remove.Parameters.AddWithValue("$prefix", LocalPrefix);
            remove.Parameters.AddWithValue("$prefixLength", LocalPrefix.Length);

            var at = 0;
            foreach (var item in feed) remove.Parameters.AddWithValue(names[at++], item.Id);

            remove.ExecuteNonQuery();
        }

        transaction.Commit();

        return added;
    }

    public void MarkSeen(string id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE news SET seen_utc = $now WHERE id = $id AND seen_utc IS NULL;";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Маха съобщението от началния екран. Не се трие: следващото теглене би
    /// го върнало, ако го нямаше тук като скрито.
    /// </summary>
    public void Hide(string id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE news SET hidden_utc = $now, seen_utc = COALESCE(seen_utc, $now)
            WHERE id = $id AND hidden_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, NewsItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$published", item.Published.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$severity", item.Severity.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$body", item.Body);
        command.Parameters.AddWithValue("$link", item.Link);
    }
}
