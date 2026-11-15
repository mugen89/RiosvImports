using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.Data;

/// <summary>
/// Един ред от дневника: какво е въведено, кога и къде е паднало.
///
/// <paramref name="Alive"/> казва дали движението още го има. Махнато отвсякъде
/// другаде, то оставя реда тук — и точно това се иска: „това вече го няма“ е
/// отговор, а изчезнал ред не е.
/// </summary>
public sealed record ActionEntry(
    long Id,
    long? CompanyId,
    string Stagenum,
    MoveKind Kind,
    long MoveId,
    DateOnly Date,
    LedgerPeriod Period,
    string Ewc,
    decimal Amount,
    string Note,
    DateTime CreatedUtc,
    bool Alive,
    string Action = "",
    bool Trashed = false,
    DateTime? Amended = null,
    string Was = "")
{
    /// <summary>Как се чете редът: „Предаден · 191202 · 13,000 т“.</summary>
    public string Summary =>
        $"{Moves.Title(Kind)} · {Ewc} · {SplitMath.Amount(Amount)} " + Lang.T("т", "t");

    /// <summary>В кой месец е паднало — заради този въпрос дневникът съществува.</summary>
    public string Where => Period.Title();
}

/// <summary>Една купчина, която прозорецът е предлагал: този код от тази партида.</summary>
public sealed record WindowShare(string Ewc, long Batch);

/// <summary>
/// Прозорецът, от който е дошло едно действие — каквото записаното само не казва.
///
/// Движенията помнят взетото, не предлаганото. А поправка значи точно това: взета е
/// грешната партида и вярната трябва да е там, където е била. Заявеното от кантара
/// също не стои никъде: то е мярката, спрямо която човекът е подреждал редовете.
///
/// Датата, полетата, бележката и количествата не са тук: те са в самите движения и
/// второ копие рано или късно би се разминало с тях.
/// </summary>
public sealed record ActionWindow(
    IReadOnlyList<WindowShare> Scope,
    decimal Target = 0m,
    bool Split = false)
{
    /// <summary>Как се пази. Числото минава през текст заради отделителя на машината.</summary>
    public string Write() => JsonSerializer.Serialize(new Stored(
        [.. Scope.Select(s => new[] { s.Ewc, s.Batch.ToString(CultureInfo.InvariantCulture) })],
        Target.ToString(CultureInfo.InvariantCulture),
        Split));

    /// <summary>Обратното. Нечетимото е като липсващо: действието се сглобява наново.</summary>
    public static ActionWindow? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            if (JsonSerializer.Deserialize<Stored>(text) is not { } stored) return null;

            return new ActionWindow(
            [
                .. stored.Scope
                    .Where(pair => pair.Length == 2)
                    .Select(pair => new WindowShare(pair[0], long.Parse(pair[1], CultureInfo.InvariantCulture)))
            ],
            decimal.Parse(stored.Target, CultureInfo.InvariantCulture),
            stored.Split);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private sealed record Stored(IReadOnlyList<string[]> Scope, string Target, bool Split);
}

/// <summary>
/// Дневник на въведеното от прозорчетата на склада.
///
/// Роден от едно оплакване: „ако съм сбъркал датата, не знам в кой месец да го
/// търся“. Въведеното отива направо в своя месец и оттам нататък е
/// неразличимо от всичко останало — а сгрешената дата значи именно, че месецът
/// не е онзи, в който човек ще погледне. Дванайсет месеца по няколкостотин
/// реда не се обхождат на ръка.
///
/// Затова редът се помни ОТДЕЛНО от движението: кога е въведен, на коя дата е
/// записан и в кой месец е паднал.
///
/// <b>Дневникът е за изхвърляне.</b> Той не е отчет и не е следа за
/// проверяващия — той е тефтерче за собствената работа: докато в него има
/// редове, има какво да се провери; изчистен, той не оставя нищо по склада.
/// Затова е отделна таблица, а не колона по движенията.
///
/// Води се по фирма и се чисти по фирма: работата по едно дружество трае дни, а
/// чуждият дневник не влиза в нея.
/// </summary>
public sealed class ActionLog(SqliteConnection connection)
{
    private readonly SqliteConnection _connection = connection;

    /// <summary>
    /// Въведеното, най-новото отгоре.
    ///
    /// „Живо ли е“ се пита чрез свързване с истинската таблица, а не по това
    /// дали редът съществува: движението може да е махнато отвсякъде другаде и
    /// дневникът трябва да го КАЖЕ, вместо да замълчи.
    ///
    /// Редът е по време на въвеждане, не по номер: поправеното действие получава
    /// нови редове, а си остава на мястото, на което е било.
    /// </summary>
    public IReadOnlyList<ActionEntry> List(long? companyId = null, string? stagenum = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.company_id, a.stagenum, a.kind, a.move_id, a.moved_on,
                   a.period, a.ewc, a.amount, a.note, a.created_utc,
                   (SELECT COUNT(*) FROM ledger_moves m WHERE m.id = a.move_id),
                   a.action, a.trashed, a.amended_utc, a.was
            FROM action_log a
            WHERE ($company IS NULL OR a.company_id = $company)
              AND ($stage IS NULL OR a.stagenum = $stage)
            ORDER BY a.created_utc DESC, a.id DESC;
            """;

        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value);
        command.Parameters.AddWithValue("$stage", (object?)stagenum ?? DBNull.Value);

        var entries = new List<ActionEntry>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            LedgerPeriod.TryParse(reader.GetString(6), out var period);

            entries.Add(new ActionEntry(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetString(2),
                Kind(reader.GetString(3)),
                reader.GetInt64(4),
                ReadDate(reader.GetString(5)),
                period,
                reader.GetString(7),
                ReadAmount(reader.GetString(8)),
                reader.GetString(9),
                ReadTime(reader.GetString(10)),
                reader.GetInt32(11) > 0,
                reader.GetString(12),
                reader.GetInt32(13) != 0,
                reader.GetString(14).Length > 0 ? ReadTime(reader.GetString(14)) : null,
                reader.GetString(15)));
        }

        return entries;
    }

    /// <summary>
    /// Колко реда чакат проверка — числото до копчето. Само от работния
    /// раздел: отложеното вече е проверено.
    /// </summary>
    public int Count(long? companyId = null, string? stagenum = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM action_log
            WHERE ($company IS NULL OR company_id = $company)
              AND ($stage IS NULL OR stagenum = $stage)
              AND trashed = 0;
            """;

        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value);
        command.Parameters.AddWithValue("$stage", (object?)stagenum ?? DBNull.Value);

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Отбелязва едно въведено движение.
    ///
    /// <paramref name="since"/> е за поправката: тя не е ново действие и пази
    /// времето на старото, а с него и мястото му в дневника.
    /// <paramref name="was"/> казва какво е било, преди да се поправи.
    /// </summary>
    public long Add(
        long? companyId,
        string stagenum,
        MoveKind kind,
        long moveId,
        DateOnly date,
        string ewc,
        decimal amount,
        string note = "",
        string action = "",
        DateTime? since = null,
        string was = "")
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO action_log
                (company_id, stagenum, kind, move_id, moved_on, period, ewc, amount, note, created_utc, action,
                 amended_utc, was)
            VALUES
                ($company, $stage, $kind, $move, $on, $period, $ewc, $amount, $note, $created, $action,
                 $amended, $was);
            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value);
        command.Parameters.AddWithValue("$stage", stagenum);
        command.Parameters.AddWithValue("$kind", kind.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$move", moveId);
        command.Parameters.AddWithValue("$on", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$period", LedgerPeriod.Of(date).ToString());
        command.Parameters.AddWithValue("$ewc", ValueConverter.NormalizeCode(ewc));
        command.Parameters.AddWithValue("$amount", SplitMath.Amount(amount));
        command.Parameters.AddWithValue("$note", note.Trim());
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        command.Parameters.AddWithValue("$created",
            since is { } first ? first.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : now);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$amended", since is null ? string.Empty : now);
        command.Parameters.AddWithValue("$was", was);

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Редовете, записани наведнъж с този — цялото действие.
    ///
    /// Ред без белег (записан преди белезите да ги има) е действие сам по себе
    /// си. Махнатите движения не влизат: поправя се това, което го има. Отложеният
    /// съсед влиза: сложен настрана, той пак е част от същото действие — иначе то
    /// се цепи на две с различни дати.
    /// </summary>
    public IReadOnlyList<ActionEntry> Group(ActionEntry entry) =>
        entry.Action.Length == 0
            ? [entry]
            : [.. List(entry.CompanyId, entry.Stagenum).Where(e => e.Action == entry.Action && e.Alive)];

    /// <summary>Какъв е бил прозорецът на действието. Празно значи, че не е пазен.</summary>
    public ActionWindow? Window(string action)
    {
        if (action.Length == 0) return null;

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT window FROM action_windows WHERE action = $action;";
        command.Parameters.AddWithValue("$action", action);

        return ActionWindow.Read(command.ExecuteScalar() as string);
    }

    /// <summary>Запомня прозореца. Поправеното действие получава новия си.</summary>
    public void Keep(string action, ActionWindow window)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO action_windows (action, window) VALUES ($action, $window)
            ON CONFLICT(action) DO UPDATE SET window = excluded.window;
            """;

        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$window", window.Write());
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Всички редове на действието — и в двата раздела, и махнатите.
    ///
    /// За разлика от <see cref="Group"/>, която събира какво да се ПОПРАВИ, тук
    /// се пита какво да се изтрие: редът, сложен настрана, и съседът му, останал
    /// във „Въведено“, са записани наведнъж и си отиват наведнъж.
    /// </summary>
    public IReadOnlyList<ActionEntry> Whole(ActionEntry entry) =>
        entry.Action.Length == 0
            ? [entry]
            : [.. List(entry.CompanyId, entry.Stagenum).Where(e => e.Action == entry.Action)];

    /// <summary>
    /// Отлага реда в раздела „Отложено“ — или го връща оттам. Движението не
    /// се пипа: то се трие едва оттам, изрично.
    /// </summary>
    public void Trash(long entryId, bool trashed = true)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE action_log SET trashed = $trashed WHERE id = $id;";
        command.Parameters.AddWithValue("$id", entryId);
        command.Parameters.AddWithValue("$trashed", trashed ? 1 : 0);
        command.ExecuteNonQuery();
    }

    /// <summary>Маха един ред от дневника. Движението не се пипа.</summary>
    public void Forget(long entryId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM action_log WHERE id = $id;";
        command.Parameters.AddWithValue("$id", entryId);
        command.ExecuteNonQuery();

        Sweep();
    }

    /// <summary>
    /// Изчиства работния раздел на дневника. Записаното в склада остава — това
    /// е тефтерче, не отчет. Отложеното остава: то още чака
    /// решение.
    /// </summary>
    public int Clear(long? companyId = null, string? stagenum = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            DELETE FROM action_log
            WHERE ($company IS NULL OR company_id = $company)
              AND ($stage IS NULL OR stagenum = $stage)
              AND trashed = 0;
            """;

        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value);
        command.Parameters.AddWithValue("$stage", (object?)stagenum ?? DBNull.Value);

        var cleared = command.ExecuteNonQuery();

        Sweep();

        return cleared;
    }

    // ------------------------------------------------------------- помощни

    /// <summary>
    /// Маха прозорците, от чиито действия не е останал ред. Дневникът е за
    /// изхвърляне и след него не бива да остава нищо.
    /// </summary>
    private void Sweep()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM action_windows WHERE action NOT IN (SELECT action FROM action_log);";
        command.ExecuteNonQuery();
    }

    private static MoveKind Kind(string text) => text switch
    {
        "received" => MoveKind.Received,
        "treated" => MoveKind.Treated,
        "generated" => MoveKind.Generated,
        "recycled" => MoveKind.Recycled,
        _ => MoveKind.Submitted
    };

    private static DateOnly ReadDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : default;

    private static decimal ReadAmount(string text) =>
        SplitMath.TryRead(text, out var amount) ? amount : 0m;

    private static DateTime ReadTime(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var when)
            ? when.ToLocalTime()
            : DateTime.MinValue;
}
