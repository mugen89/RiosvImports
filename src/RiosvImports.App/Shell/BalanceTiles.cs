using RiosvImports.Core.Localization;

namespace RiosvImports.App.Shell;

/// <summary>Плочките на таблото.</summary>
internal enum BalanceTile
{
    /// <summary>Началното, образуваното, предаденото и крайното — четири едри числа.</summary>
    Numbers,

    /// <summary>Салдото на площадката през годината.</summary>
    Trend,

    /// <summary>Образувано срещу предадено по месеци.</summary>
    Flow,

    /// <summary>Кодовете по налично количество.</summary>
    TopClosing,

    /// <summary>Дял на всеки код от наличното.</summary>
    Share,

    /// <summary>Кодовете по образувано количество за годината.</summary>
    TopGenerated,

    /// <summary>Топлинна карта: код срещу месец.</summary>
    Heat,

    /// <summary>Сборовете по раздели на книгата.</summary>
    Sheets,

    /// <summary>Какво иска внимание: отрицателни салда и залежал отпадък.</summary>
    Attention,

    /// <summary>Кои отчети са влезли в тези числа.</summary>
    Sources
}

/// <summary>
/// Какво знае таблото за плочките си: как се казват, колко място искат и с
/// какъв ключ се помнят.
///
/// Отделно от самите плочки, защото списъкът трябва да се покаже и в панела с
/// отметките, преди която и да е плочка да е сглобена. Заглавията са методи, а
/// не готов списък: статичното поле се пълни веднъж, с езика, който е бил в
/// сила тогава.
/// </summary>
internal static class BalanceTiles
{
    /// <summary>Плочките в реда, в който стоят на таблото.</summary>
    public static readonly IReadOnlyList<BalanceTile> All =
    [
        BalanceTile.Numbers,
        BalanceTile.Trend,
        BalanceTile.Flow,
        BalanceTile.TopClosing,
        BalanceTile.Share,
        BalanceTile.Heat,
        BalanceTile.TopGenerated,
        BalanceTile.Sheets,
        BalanceTile.Attention,
        BalanceTile.Sources
    ];

    /// <summary>Ключът в настройките. Не се превежда — той не се чете от човек.</summary>
    public static string Key(BalanceTile tile) => tile.ToString().ToLowerInvariant();

    public static BalanceTile? Of(string key) =>
        All.Select(t => (BalanceTile?)t).FirstOrDefault(t => Key(t!.Value) == key);

    public static string Title(BalanceTile tile) => tile switch
    {
        BalanceTile.Numbers => Lang.T("Накратко", "At a glance"),
        BalanceTile.Trend => Lang.T("Салдо през годината", "Balance through the year"),
        BalanceTile.Flow => Lang.T("Образувано и предадено по месеци", "Generated and submitted by month"),
        BalanceTile.TopClosing => Lang.T("Най-много налично", "Largest quantities on hand"),
        BalanceTile.Share => Lang.T("Дял на кодовете", "Share by code"),
        BalanceTile.TopGenerated => Lang.T("Най-много образувано", "Most generated"),
        BalanceTile.Heat => Lang.T("Кога по кой код", "When, and by which code"),
        BalanceTile.Sheets => Lang.T("По раздели на книгата", "By section of the book"),
        BalanceTile.Attention => Lang.T("Иска внимание", "Wants attention"),
        _ => Lang.T("Отчети в обхвата", "Reports in scope")
    };

    /// <summary>Едно изречение под заглавието — казва какъв въпрос отговаря плочката.</summary>
    public static string Hint(BalanceTile tile) => tile switch
    {
        BalanceTile.Numbers => Lang.T("началото, движението и краят", "the start, the movement and the end"),
        BalanceTile.Trend => Lang.T("колко стои на площадката в края на всеки месец",
                                    "how much is on site at each month end"),
        BalanceTile.Flow => Lang.T("нагоре е образувано, надолу — предадено",
                                   "up is generated, down is submitted"),
        BalanceTile.TopClosing => Lang.T("в края на годината", "at the end of the year"),
        BalanceTile.Share => Lang.T("от наличното в края на годината", "of what is on hand at year end"),
        BalanceTile.TopGenerated => Lang.T("за цялата година", "for the whole year"),
        BalanceTile.Heat => Lang.T("по-тъмното е по-голямо количество", "darker means a larger quantity"),
        BalanceTile.Sheets => Lang.T("количествата така, както са записани", "quantities exactly as recorded"),
        BalanceTile.Attention => Lang.T("отрицателни салда и залежал отпадък",
                                        "negative balances and waste that has not moved"),
        _ => Lang.T("числата идват оттук", "the numbers come from these")
    };

    /// <summary>Колко колони заема плочката, когато има място.</summary>
    public static int Span(BalanceTile tile) => tile switch
    {
        BalanceTile.Numbers => 3,
        BalanceTile.Trend or BalanceTile.Flow or BalanceTile.Heat => 2,
        _ => 1
    };

    /// <summary>
    /// Височината в редове текст.
    ///
    /// В редове, а не в пиксели: на мащаб 150 % пикселът е друг, а редът е
    /// същият. Иначе плочките режат съдържанието си точно при хората, които
    /// най-много се нуждаят от едър шрифт.
    /// </summary>
    public static int Rows(BalanceTile tile) => tile switch
    {
        BalanceTile.Numbers => 4,
        BalanceTile.Sources => 7,
        BalanceTile.Sheets => 7,
        BalanceTile.Attention => 8,
        _ => 10
    };
}
