using RiosvImports.Core.Localization;

namespace RiosvImports.Core.Ledger;

/// <summary>
/// Кои движения изобщо може да се внасят отвън.
///
/// <b>Приемането — винаги. Останалите три — не.</b> И това не е предпазливост, а
/// устройство на програмата: приемането е единственото, което идва ОТВЪН.
/// Третирането, образуването и предаването се РАЖДАТ тук — от избрани купчини,
/// с посочени дялове по партиди, с произход, който минава през цялата верига.
///
/// Внесено отвън третиране носи количество и код, но не носи нищо от това: то
/// не изчерпва партида, не наследява произход и не се вижда в пътя ѝ. Оттам
/// нататък двете сметки — тази на програмата и тази на файла — вървят една до
/// друга и се разминават тихо, а разликата излиза чак в отчета.
///
/// Затова останалите три се отключват ПООТДЕЛНО и нарочно. Има случаи, в които
/// се налага: пренасяне на стара година, площадка, която е водена другаде до
/// вчера. Но това е изключение, което човек включва сам, а не начин на работа.
/// </summary>
public static class ImportPolicy
{
    /// <summary>
    /// Приемането не се заключва.
    ///
    /// То е вратата на площадката: няма как да бъде „сметнато“ отвътре, защото
    /// идва отвън по устройство.
    /// </summary>
    public static bool Always(MoveKind kind) => kind == MoveKind.Received;

    /// <summary>Какво се внася по подразбиране — само приемането.</summary>
    public static IReadOnlySet<MoveKind> Default => new HashSet<MoveKind> { MoveKind.Received };

    /// <summary>Дали този вид може да се внася при тези отключени.</summary>
    public static bool Allows(IReadOnlySet<MoveKind>? unlocked, MoveKind kind) =>
        Always(kind) || (unlocked?.Contains(kind) ?? false);

    /// <summary>
    /// Защо е заключено — казва се на човека, а не се премълчава.
    ///
    /// Заключено копче без обяснение кара човек да търси какво му липсва, за да
    /// го включи. Тук липсва решение, не право.
    /// </summary>
    public static string Why(MoveKind kind) => Lang.T(
        $"„{Moves.Title(kind)}“ се ражда в програмата — от избрани купчини и с произход по " +
        "партиди. Внесено отвън, то носи количество, но не изчерпва нищо и не се вижда в пътя " +
        "на партидата. Отключва се от „Настройки → Внасяне на движения“, ако наистина трябва.",
        $"“{Moves.Title(kind)}” is born inside the program — from picked piles, with an origin " +
        "traced through batches. Imported from outside it carries a quantity but draws down " +
        "nothing and never shows in a batch's trail. Unlock it in “Settings → Importing " +
        "movements” if it really is needed.");

    /// <summary>Записва се като „treated,generated“ — текст, а не битове.</summary>
    public static string Text(IReadOnlyCollection<MoveKind> unlocked) =>
        string.Join(",", unlocked
            .Where(kind => !Always(kind))
            .Select(kind => kind.ToString().ToLowerInvariant()));

    /// <summary>Разчита записаното. Всичко неразбрано значи „само приемането“.</summary>
    public static IReadOnlySet<MoveKind> Read(string? text)
    {
        var unlocked = new HashSet<MoveKind> { MoveKind.Received };

        foreach (var word in text?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [])
            foreach (var kind in Moves.All)
                if (string.Equals(kind.ToString(), word.Trim(), StringComparison.OrdinalIgnoreCase))
                    unlocked.Add(kind);

        return unlocked;
    }
}
