using System.Globalization;
using Microsoft.Data.Sqlite;
using RiosvImports.Core.Ledger;
using RiosvImports.Core.Values;

namespace RiosvImports.Data;

/// <summary>Какво трябва да се знае, за да се заведе партида.</summary>
public sealed record BatchDraft(
    string Eik,
    string PartnerName,
    string Ewc,
    DateOnly ReceivedOn,
    string Stagenum = "1",
    string Document = "",
    bool Opening = false,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>
/// Партидите: кой какво е донесъл и кога.
///
/// Хранилището не проверява наличност и не решава кое от коя партида се взима —
/// това е работа на <see cref="Allocation"/> и <see cref="StockMath"/> в
/// „Ядро“. Тук се пази и се чете (правило 4: правилото се отделя от прозореца,
/// и от базата също).
/// </summary>
public sealed class BatchStore(SqliteConnection connection)
{
    private readonly SqliteConnection _connection = connection;

    // ------------------------------------------------------------- четене

    /// <summary>
    /// Партидите на една фирма, по избор само за една площадка.
    ///
    /// Подредени по дата на приемане, после по номер — редът, по който
    /// разпределянето ще ги изчерпи, за да се чете списъкът както ще се
    /// похарчи.
    /// </summary>
    public IReadOnlyList<Batch> List(long companyId, string? stagenum = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, eik, partner_name, ewc, received_on, document, opening, stagenum
            FROM batches
            WHERE company_id = $company AND ($stage IS NULL OR stagenum = $stage)
            ORDER BY received_on, id;
            """;

        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$stage", (object?)stagenum ?? DBNull.Value);

        var batches = new List<Batch>();
        using var reader = command.ExecuteReader();

        while (reader.Read()) batches.Add(Read(reader));

        return batches;
    }

    public Batch? Find(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, eik, partner_name, ewc, received_on, document, opening, stagenum
            FROM batches WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Колоните, които пътуват до отчета, но не участват в сметка.</summary>
    public Dictionary<string, string> Fields(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT fields FROM batches WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        return LedgerText.ReadFields(command.ExecuteScalar() as string);
    }

    /// <summary>Клиентите, от които изобщо е получавано — за сито и за меню.</summary>
    public IReadOnlyList<(string Eik, string Name)> Partners(long companyId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT eik, partner_name FROM batches
            WHERE company_id = $company AND LENGTH(TRIM(eik)) > 0
            GROUP BY eik
            ORDER BY partner_name, eik;
            """;
        command.Parameters.AddWithValue("$company", companyId);

        var partners = new List<(string, string)>();
        using var reader = command.ExecuteReader();

        while (reader.Read()) partners.Add((reader.GetString(0), reader.GetString(1)));

        return partners;
    }

    // ------------------------------------------------------------- писане

    /// <summary>
    /// Завежда партида и връща номера ѝ.
    ///
    /// Видимият код се дава тук, а не отвън: той трябва да е пореден в рамките
    /// на фирмата и годината, а само базата знае кой е последният.
    /// </summary>
    public long Add(long companyId, BatchDraft draft)
    {
        var code = NextCode(companyId, draft.ReceivedOn.Year);

        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO batches
                (company_id, stagenum, code, eik, partner_name, ewc, received_on,
                 document, fields, opening, created_utc)
            VALUES ($company, $stage, $code, $eik, $name, $ewc, $on,
                    $document, $fields, $opening, $now)
            RETURNING id;
            """;

        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$stage", Stage(draft.Stagenum));
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$eik", draft.Eik.Trim());
        command.Parameters.AddWithValue("$name", draft.PartnerName.Trim());
        command.Parameters.AddWithValue("$ewc", ValueConverter.NormalizeCode(draft.Ewc));
        command.Parameters.AddWithValue("$on", LedgerText.Date(draft.ReceivedOn));
        command.Parameters.AddWithValue("$document", draft.Document.Trim());
        command.Parameters.AddWithValue("$fields", LedgerText.Fields(draft.Fields));
        command.Parameters.AddWithValue("$opening", draft.Opening ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>Поправя описателното по партидата. Кодът и номерът не се менят.</summary>
    public void Update(long id, BatchDraft draft)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE batches
            SET stagenum = $stage, eik = $eik, partner_name = $name, ewc = $ewc,
                received_on = $on, document = $document, fields = $fields
            WHERE id = $id;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$stage", Stage(draft.Stagenum));
        command.Parameters.AddWithValue("$eik", draft.Eik.Trim());
        command.Parameters.AddWithValue("$name", draft.PartnerName.Trim());
        command.Parameters.AddWithValue("$ewc", ValueConverter.NormalizeCode(draft.Ewc));
        command.Parameters.AddWithValue("$on", LedgerText.Date(draft.ReceivedOn));
        command.Parameters.AddWithValue("$document", draft.Document.Trim());
        command.Parameters.AddWithValue("$fields", LedgerText.Fields(draft.Fields));

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Маха партида.
    ///
    /// Връзките ѝ падат с нея, но движенията остават: тогава те стават
    /// неразпределени и се виждат в контролите. По-добре видимо неразпределено,
    /// отколкото тихо изтрито движение.
    /// </summary>
    public void Remove(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM batches WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    // ------------------------------------------------------------- помощни

    /// <summary>
    /// Следващият пореден код: „2026-0042“.
    ///
    /// Годината отпред, защото номерата тръгват отначало всяка година и човек
    /// сверява с папка, подредена по същия начин. Четири цифри — площадка с
    /// повече от девет хиляди приемания на година не сме виждали, а и петата
    /// цифра само ще удължи номера, без да го счупи.
    /// </summary>
    public string NextCode(long companyId, int year)
    {
        var prefix = year.ToString("D4", CultureInfo.InvariantCulture) + "-";

        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT code FROM batches
            WHERE company_id = $company AND code LIKE $prefix
            ORDER BY code DESC LIMIT 1;
            """;

        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$prefix", prefix + "%");

        var last = command.ExecuteScalar() as string;
        var next = 1;

        if (last is { Length: > 0 } &&
            int.TryParse(last[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var used))
            next = used + 1;

        return prefix + next.ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>Празна площадка значи първата — схемата иска число, не празно.</summary>
    internal static string Stage(string? stagenum) =>
        string.IsNullOrWhiteSpace(stagenum) ? "1" : stagenum.Trim();

    private static Batch Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        LedgerText.ReadDate(reader.GetString(5)) ?? default,
        reader.GetString(6),
        reader.GetInt32(7) == 1,
        reader.GetString(8));
}
