namespace RiosvImports.Core.Localization;

public enum Language
{
    Bulgarian,
    English
}

/// <summary>
/// Езикът на интерфейса.
///
/// Преводите стоят на място — <c>Lang.T("Записване", "Save")</c>, — а не в
/// отделен файл с ключове. Причината е практична: ключът се пише веднъж и
/// после живее далеч от мястото, където се ползва. Пропуснат превод се вижда
/// като празен надпис чак когато някой отвори точно този прозорец, а
/// преименуван ключ не се хваща при компилиране. Двойката текст не може да
/// липсва — тя е самият израз.
///
/// Какво НЕ се превежда:
///
///  • имената на отчетите, на таблиците и на полетата — те идват от XSD
///    файловете на НИСО и назовават нормативни формуляри. „Приложение 9“ си е
///    Приложение 9 и на английски; преведено име не отговаря на нищо, което
///    човек може да намери в наредбата или в сайта на НИСО;
///  • стойностите в падащите менюта — те влизат в подавания файл;
///  • самият изходен XML.
///
/// Тоест английският превежда програмата, а не формулярите. Това е нарочно и
/// е записано в дневника.
/// </summary>
public static class Lang
{
    public static Language Current { get; private set; } = Language.Bulgarian;

    public static bool IsEnglish => Current == Language.English;

    /// <summary>Обажда се, когато езикът се смени — главният прозорец слуша.</summary>
    public static event EventHandler? Changed;

    /// <summary>Ключът, който се пази в настройките. „bg“ е подразбиращият се.</summary>
    public static string Key => IsEnglish ? "en" : "bg";

    public static void Use(string? key)
    {
        var next = string.Equals(key, "en", StringComparison.OrdinalIgnoreCase)
            ? Language.English
            : Language.Bulgarian;

        if (next == Current) return;

        Current = next;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Българският текст или английският — според избрания език.</summary>
    public static string T(string bg, string en) => IsEnglish ? en : bg;

    /// <summary>
    /// Езиците за избор. Всеки се изписва на собствения си език: човек, който
    /// търси английския, търси думата „English“, а не „Английски“.
    /// </summary>
    public static readonly (string Key, string Name)[] All =
    [
        ("bg", "Български"),
        ("en", "English")
    ];
}
