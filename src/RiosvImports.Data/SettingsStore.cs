using Microsoft.Data.Sqlite;

namespace RiosvImports.Data;

/// <summary>
/// Дребните предпочитания: ЕИК по подразбиране, последна папка, избран профил
/// на подписа.
///
/// Всичко е текст. Настройка се добавя, като просто се напише ново име — без
/// стъпка по базата и без версия.
/// </summary>
public sealed class SettingsStore(SqliteConnection connection)
{
    /// <summary>Имената стоят на едно място, за да не се разминават при писане.</summary>
    public static class Keys
    {
        public const string DefaultEik = "default.eik";
        public const string DefaultCompany = "default.company";
        public const string LastFolder = "last.folder";
        public const string SignatureDigest = "signature.digest";
        public const string SignatureForm = "signature.form";
        public const string AutoCheck = "editor.autocheck";
        public const string ConfirmOnDelete = "editor.confirmdelete";

        /// <summary>Заключва истинското изтриване от раздела „Отложено“ в дневника.</summary>
        public const string LogDeleteLocked = "log.delete.locked";

        /// <summary>Изгледите на склада — като икони, а не с надписи.</summary>
        public const string StoreViewIcons = "ui.store.icons";
        public const string UiTheme = "ui.theme";
        public const string UiLanguage = "ui.language";

        // Балансът помни как е бил нагласен: човек, който е скрил половината
        // плочки или е сгънал панела, не бива да ги намери обратно утре.
        public const string BalanceMarks = "balance.marks";
        public const string BalanceExportMarks = "balance.marks.export";
        public const string BalanceOpening = "balance.opening";
        public const string BalanceTiles = "balance.tiles";
        public const string BalancePanel = "balance.panel";

        /// <summary>Свита ли е лентата отляво.</summary>
        public const string RailCollapsed = "ui.rail";

        /// <summary>
        /// Фирмата, по която се работи.
        ///
        /// Избира се веднъж, от лентата отляво, и важи за всички екрани. Помни
        /// се, защото работата по една фирма трае дни: следващото пускане
        /// продължава оттам, а не от първата по ред.
        /// </summary>
        public const string CurrentCompany = "current.company";

        /// <summary>
        /// От кой месец се води складът — по един ключ на фирма
        /// („ledger.start.7“ = „2026-09“).
        ///
        /// Настройка, а не колона: две дружества тръгват по различно време, а
        /// нова фирма не бива да иска стъпка по базата. Ключът се сглобява в
        /// <see cref="PeriodStore"/>, за да стои на едно място.
        /// </summary>
        public const string LedgerStart = "ledger.start";

        /// <summary>
        /// Работният период — по един ключ на фирма
        /// („work.window.7“ = „2026-09-01|2026-09-30|1“).
        ///
        /// Настройка, а не колона, по същата причина като началния месец: това е
        /// предпочитание на човека, не данни за склада. Ключът се сглобява в
        /// <see cref="PeriodStore"/>, за да стои на едно място с останалите
        /// правила за „кога може да се пише“.
        /// </summary>
        public const string WorkPeriod = "work.window";

        /// <summary>
        /// Кои движения са отключени за внасяне — по един ключ на фирма
        /// („import.unlock.7“ = „treated,generated“).
        ///
        /// Приемането не се записва: то е винаги отключено и списък, който го
        /// съдържа, би позволил някой ден да бъде и извадено.
        /// </summary>
        public const string ImportUnlock = "import.unlock";

        /// <summary>Попълва ли се само полето, стеснено до една стойност.</summary>
        public const string ListAutoFill = "lists.autofill";

        /// <summary>
        /// Пита ли програмата GitHub при пускане — за ново издание и за новини.
        ///
        /// Една отметка за двете: човекът решава дали програмата говори с
        /// мрежата сама, а не кое по отделно. По подразбиране — да.
        /// </summary>
        public const string UpdateAuto = "update.auto";

        /// <summary>Предлагат ли се и пробните издания — за този, който тества.</summary>
        public const string UpdatePrerelease = "update.prerelease";

        /// <summary>Кога е свършила последната проверка — местно време, ISO.</summary>
        public const string UpdateChecked = "update.checked";

        /// <summary>
        /// Изданието, с което програмата е пускана последно. По разликата с
        /// днешното се познава току-що обновената — и се казва какво е новото.
        /// </summary>
        public const string UpdateLastRun = "update.lastrun";

        /// <summary>
        /// Бележките на изданието, което тъкмо се слага: „версия“, нов ред,
        /// текстът. Пишат се преди рестарта, четат се след него.
        /// </summary>
        public const string UpdateNotes = "update.notes";

        /// <summary>Изданието, чиято покана е скрита от началния екран. По-новото пак се показва.</summary>
        public const string UpdateSkipped = "update.skip";
    }

    private readonly SqliteConnection _connection = connection;

    public string? Get(string key)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string;
    }

    public string Get(string key, string fallback) => Get(key) ?? fallback;

    public bool GetBool(string key, bool fallback) =>
        Get(key) is { } value ? value == "1" : fallback;

    public void Set(string key, string value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);

        command.ExecuteNonQuery();
    }

    public void Set(string key, bool value) => Set(key, value ? "1" : "0");
}
