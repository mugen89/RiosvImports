using Microsoft.Data.Sqlite;
using RiosvImports.Core.Ledger;

namespace RiosvImports.Data;

/// <summary>Затворен месец: кога е приет и с какво.</summary>
public sealed record ClosedPeriod(LedgerPeriod Period, string Stagenum, DateTime ClosedUtc, string Note);

/// <summary>
/// Отварянето и затварянето на месеца.
///
/// Затварянето не пази числа — салдата се смятат от движенията при всяко
/// питане и точно затова не могат да се разминат с тях. Пази се само
/// решението: този месец е прегледан и приет такъв, какъвто е.
///
/// Тук стои и месецът, от който изобщо се води складът. Той не е предпочитание,
/// а начало на отчетност: преди него наличността не е нула, а неизвестна, и
/// движение отпреди него значи, че някой е сбъркал годината.
/// </summary>
public sealed class PeriodStore(SqliteConnection connection, SettingsStore settings)
{
    private readonly SqliteConnection _connection = connection;
    private readonly SettingsStore _settings = settings;

    // ------------------------------------------ месецът, от който се води

    /// <summary>
    /// От кой месец се води складът на тази фирма.
    ///
    /// Null значи „още не е обявено“ — тогава нищо не се брои за твърде рано и
    /// страницата пита човека, вместо да отбелязва всичко в червено.
    /// </summary>
    public LedgerPeriod? Start(long companyId) =>
        LedgerPeriod.TryParse(_settings.Get(StartKey(companyId)), out var period) ? period : null;

    public void SetStart(long companyId, LedgerPeriod period) =>
        _settings.Set(StartKey(companyId), period.ToString());

    /// <summary>Настройка на фирма, а не обща: две дружества тръгват по различно време.</summary>
    private static string StartKey(long companyId) =>
        $"{SettingsStore.Keys.LedgerStart}.{companyId}";

    // ------------------------------------------------- работният период

    /// <summary>
    /// Между кои дати се пише сега — самоконтролът на човека.
    ///
    /// Стои тук, до затворените месеци, защото отговаря на същия въпрос: „може
    /// ли да се пише на тази дата“. Двата отговора обаче не са от един ред и
    /// това трябва да се знае: затвореният месец е заключалка на отчета, а
    /// работният период е обещание пред самия себе си и се оттегля с отметка.
    ///
    /// Изключен по подразбиране: програмата не връзва човек, който не е поискал
    /// да бъде вързан.
    /// </summary>
    public WorkPeriod Window(long companyId) =>
        WorkPeriod.Read(_settings.Get(WindowKey(companyId)));

    public void SetWindow(long companyId, WorkPeriod window) =>
        _settings.Set(WindowKey(companyId), window.Text);

    private static string WindowKey(long companyId) =>
        $"{SettingsStore.Keys.WorkPeriod}.{companyId}";

    // --------------------------------------------- какво се внася отвън

    /// <summary>
    /// Кои движения са отключени за внасяне при тази фирма.
    ///
    /// Стои тук по същата причина като останалите две: това е трети отговор на
    /// „може ли да се пише така“. Правилото кое е винаги отключено живее в
    /// <see cref="ImportPolicy"/> — то е за устройството на програмата, не за
    /// предпочитанието на фирмата.
    /// </summary>
    public IReadOnlySet<MoveKind> Unlocked(long companyId) =>
        ImportPolicy.Read(_settings.Get(UnlockKey(companyId)));

    public void SetUnlocked(long companyId, IReadOnlyCollection<MoveKind> unlocked) =>
        _settings.Set(UnlockKey(companyId), ImportPolicy.Text(unlocked));

    /// <summary>Дали този вид изобщо може да се внася при тази фирма.</summary>
    public bool CanImport(long companyId, MoveKind kind) =>
        ImportPolicy.Allows(Unlocked(companyId), kind);

    private static string UnlockKey(long companyId) =>
        $"{SettingsStore.Keys.ImportUnlock}.{companyId}";

    // ------------------------------------------------------------- четене

    /// <summary>Дали месецът е затворен — тогава движенията в него не се пипат.</summary>
    public bool IsClosed(long companyId, string? stagenum, LedgerPeriod period)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM ledger_periods
            WHERE company_id = $company AND stagenum = $stage AND period = $period;
            """;

        Bind(command, companyId, stagenum, period);

        return command.ExecuteScalar() is not null;
    }

    /// <summary>Затворените месеци — за да се видят заключените в списъка.</summary>
    public IReadOnlyList<ClosedPeriod> Closed(long companyId, string? stagenum = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT period, stagenum, closed_utc, note FROM ledger_periods
            WHERE company_id = $company AND ($stage IS NULL OR stagenum = $stage)
            ORDER BY period DESC;
            """;

        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$stage", (object?)stagenum ?? DBNull.Value);

        var closed = new List<ClosedPeriod>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (!LedgerPeriod.TryParse(reader.GetString(0), out var period)) continue;

            closed.Add(new ClosedPeriod(
                period,
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                reader.GetString(3)));
        }

        return closed;
    }

    /// <summary>
    /// Последният затворен месец — оттам тръгва предложението кой да е следващият.
    /// </summary>
    public LedgerPeriod? Latest(long companyId, string? stagenum = null) =>
        Closed(companyId, stagenum).Select(c => (LedgerPeriod?)c.Period).FirstOrDefault();

    // ------------------------------------------------------------- писане

    /// <summary>
    /// Затваря месеца.
    ///
    /// Хранилището не пита дали контролите минават — <see cref="Reconcile"/>
    /// решава кое спира, а човекът решава дали да приеме бележките. Тук се
    /// записва решението заедно с това, с което е взето: „приет с разлика в
    /// масата 0,300 т“ утре обяснява защо числата изглеждат така.
    /// </summary>
    public void Close(long companyId, string? stagenum, LedgerPeriod period, string note = "")
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ledger_periods (company_id, stagenum, period, closed_utc, note)
            VALUES ($company, $stage, $period, $now, $note)
            ON CONFLICT(company_id, stagenum, period)
            DO UPDATE SET closed_utc = excluded.closed_utc, note = excluded.note;
            """;

        Bind(command, companyId, stagenum, period);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$note", note.Trim());

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Отваря затворен месец наново.
    ///
    /// Съществува, защото грешка се намира и след затварянето, а единственият
    /// друг изход би бил да се поправя със записи в следващия месец — което
    /// разминава отчета с това, което наистина се е случило.
    /// </summary>
    public void Reopen(long companyId, string? stagenum, LedgerPeriod period)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            DELETE FROM ledger_periods
            WHERE company_id = $company AND stagenum = $stage AND period = $period;
            """;

        Bind(command, companyId, stagenum, period);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, long companyId, string? stagenum, LedgerPeriod period)
    {
        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$stage", BatchStore.Stage(stagenum));
        command.Parameters.AddWithValue("$period", period.ToString());
    }
}
