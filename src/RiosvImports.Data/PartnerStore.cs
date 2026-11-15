using System.Globalization;
using Microsoft.Data.Sqlite;
using RiosvImports.Core.Localization;

namespace RiosvImports.Data;

/// <summary>
/// Насрещната страна: от кого се приема и на кого се предава.
///
/// Текст, а не число, по същата причина като произхода на отчета: утре и
/// подателят ще поиска същото, а новият вид трябва да е нова дума, не нова
/// стъпка по базата.
/// </summary>
public static class PartnerRole
{
    /// <summary>На когото предаваме — оползотворителят.</summary>
    public const string Receiver = "receiver";

    /// <summary>
    /// От когото приемаме — доставчикът. Научава се от всяко записано
    /// приемане: на ръка, от таблицата и от внесен дневник.
    /// </summary>
    public const string Originator = "originator";

    /// <summary>
    /// Договорът, по който се предава. Не е контрагент, но работата по списъка е
    /// същата: предлага се, научава се при запис и се поправя от „Настройки“.
    /// ЕИК няма — полето там стои празно.
    /// </summary>
    public const string Contract = "contract";
}

/// <summary>Един запомнен получател: име, ЕИК и колко пъти е ползван.</summary>
public sealed record Partner(
    long Id,
    long CompanyId,
    string Role,
    string Eik,
    string Name,
    int Used,
    DateTime Updated)
{
    /// <summary>Как се показва и как се предлага: „Претопи ООД — 205566778“.</summary>
    public string Display => Eik.Length == 0 ? Name : $"{Name} — {Eik}";

    /// <summary>Ползван ли е изобщо, или е вписан на ръка и още чака.</summary>
    public bool Fresh => Used == 0;
}

/// <summary>
/// Кой на кого предава — списъкът, който се научава сам.
///
/// Името на получателя е СВОБОДЕН текст по схемата и ще си остане такъв: НИСО
/// не дава номенклатура и програмата не бива да измисля една. Но свободно поле,
/// писано на ръка по трийсет пъти месечно, ражда „Претопи ООД“, „Претопи OOD“ и
/// „претопи оод“ — три получателя вместо един, и нито едно търсене не ги
/// събира.
///
/// Затова написаното веднъж се помни и се предлага втория път. Списъкът е малък
/// по устройство: площадка предава на шепа оползотворители, не на стотици — и
/// точно затова предлагането върши работа.
///
/// <b>Предлага, не ограничава.</b> Ново име минава и се научава; програмата не
/// спира предаване на получател, когото не познава (правило 13 — кратките
/// списъци стесняват предлаганото, не разбираното).
/// </summary>
public sealed class PartnerStore(SqliteConnection connection)
{
    private readonly SqliteConnection _connection = connection;

    /// <summary>
    /// Запомнените, най-ползваните отгоре.
    ///
    /// По ползване, а не по азбука: човек предава на трима редовно и на
    /// петнайсет по веднъж, и първите трима трябва да са под ръка.
    /// </summary>
    public IReadOnlyList<Partner> List(long companyId, string role = PartnerRole.Receiver)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, company_id, role, eik, name, used, updated_utc
            FROM partners
            WHERE company_id = $company AND role = $role
            ORDER BY used DESC, name COLLATE NOCASE;
            """;

        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$role", role);

        var partners = new List<Partner>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
            partners.Add(new Partner(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                ReadTime(reader.GetString(6))));

        return partners;
    }

    /// <summary>
    /// Отбелязва ползван получател — вписва го, ако е нов.
    ///
    /// Тук е самото „научаване“: викa се при ЗАПИС на предаване, не при писане
    /// в полето. Недовършено име, изтрито след миг, не бива да остава в списъка
    /// завинаги — а записано предаване е решение.
    ///
    /// Празно име не се помни: получател без име е липсващо поле, а не нов
    /// контрагент.
    /// </summary>
    public void Remember(long companyId, string name, string eik, string role = PartnerRole.Receiver)
    {
        var who = name.Trim();
        if (who.Length == 0) return;

        var pin = eik.Trim();

        // Познат ли е вече — по ЕИК, а без него по името, без значение от
        // главните букви — той се брои, вместо да се впише втори път. Иначе
        // „Метал ООД“ и „МЕТАЛ ООД“ с един и същ ЕИК биха станали двама, а
        // целта на списъка е точно обратното.
        //
        // Познат по име, но без ЕИК, го получава сега: списъкът се допълва,
        // вместо да ражда двойник.
        var known = List(companyId, role);

        var same = pin.Length > 0
            ? known.FirstOrDefault(p => string.Equals(p.Eik, pin, StringComparison.OrdinalIgnoreCase))
            : null;

        same ??= known.FirstOrDefault(p =>
            string.Equals(p.Name, who, StringComparison.CurrentCultureIgnoreCase) &&
            (p.Eik.Length == 0 || pin.Length == 0 ||
             string.Equals(p.Eik, pin, StringComparison.OrdinalIgnoreCase)));

        if (same is not null)
        {
            using var used = _connection.CreateCommand();
            used.CommandText = """
                UPDATE partners
                SET used = used + 1, updated_utc = $now,
                    eik = CASE WHEN LENGTH(TRIM(eik)) = 0 THEN $eik ELSE eik END
                WHERE id = $id;
                """;

            used.Parameters.AddWithValue("$id", same.Id);
            used.Parameters.AddWithValue("$eik", pin);
            used.Parameters.AddWithValue("$now", Now);
            used.ExecuteNonQuery();

            return;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO partners (company_id, role, eik, name, used, updated_utc)
            VALUES ($company, $role, $eik, $name, 1, $now)
            ON CONFLICT(company_id, role, eik, name) DO UPDATE SET
                used = used + 1,
                updated_utc = excluded.updated_utc;
            """;

        Bind(command, companyId, role, eik, who);
        command.Parameters.AddWithValue("$now", Now);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Вписва получател на ръка, без да го брои за ползван.
    ///
    /// Отделно от <see cref="Remember"/>, защото двете значат различно:
    /// вписаният предварително още не е ползван и „най-ползваните отгоре“ не
    /// бива да го изведе пред истинските.
    /// </summary>
    public bool Add(long companyId, string name, string eik, string role = PartnerRole.Receiver)
    {
        var who = name.Trim();
        if (who.Length == 0) return false;

        // Без ЕИК името е единственото, което го различава, а ключът на таблицата
        // гледа главните букви. Списък с „Договор 12“ и „договор 12“ е точно
        // грешката, от която той трябва да пази.
        if (eik.Trim().Length == 0 &&
            List(companyId, role).Any(p => p.Eik.Length == 0 &&
                string.Equals(p.Name, who, StringComparison.CurrentCultureIgnoreCase)))
            return false;

        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO partners (company_id, role, eik, name, used, updated_utc)
            VALUES ($company, $role, $eik, $name, 0, $now)
            ON CONFLICT(company_id, role, eik, name) DO NOTHING;
            """;

        Bind(command, companyId, role, eik, who);
        command.Parameters.AddWithValue("$now", Now);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Поправя вписан получател — сгрешен ЕИК се среща често.</summary>
    public void Update(long partnerId, string name, string eik)
    {
        var who = name.Trim();
        if (who.Length == 0) return;

        using var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE partners SET name = $name, eik = $eik, updated_utc = $now
            WHERE id = $id;
            """;

        command.Parameters.AddWithValue("$id", partnerId);
        command.Parameters.AddWithValue("$name", who);
        command.Parameters.AddWithValue("$eik", eik.Trim());
        command.Parameters.AddWithValue("$now", Now);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Маха запомнен получател.
    ///
    /// Записаните предавания не се пипат: името им е в самото движение, както
    /// го иска НИСО. Оттук си отива само предложението.
    /// </summary>
    public void Remove(long partnerId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM partners WHERE id = $id;";
        command.Parameters.AddWithValue("$id", partnerId);
        command.ExecuteNonQuery();
    }

    /// <summary>ЕИК-ът на познат получател по име. Празно значи „не го знаем“.</summary>
    public string EikOf(long companyId, string name, string role = PartnerRole.Receiver)
    {
        var who = name.Trim();
        if (who.Length == 0) return string.Empty;

        return List(companyId, role)
            .FirstOrDefault(p => string.Equals(p.Name, who, StringComparison.CurrentCultureIgnoreCase))
            ?.Eik ?? string.Empty;
    }

    /// <summary>
    /// Допълва написаното от познатото — без да го подменя.
    ///
    /// Липсващ ЕИК се взима по името, липсващо име — по ЕИК-а. Име, написано
    /// само с други главни букви, става такова, каквото е в списъка: „метал
    /// оод“ и „Метал ООД“ са един доставчик и в книгата трябва да изглеждат
    /// като един.
    ///
    /// Различно написаното НЕ се пипа. Име, което не е в списъка, или ЕИК,
    /// който не съвпада с този на познатото име, е решение на човека —
    /// програмата не познава нито новия доставчик, нито сменения му ЕИК.
    /// </summary>
    public (string Name, string Eik) Complete(long companyId, string name, string eik, string role)
    {
        var who = name.Trim();
        var pin = eik.Trim();

        if (who.Length == 0 && pin.Length == 0) return (who, pin);

        var known = List(companyId, role);

        if (pin.Length > 0 &&
            known.FirstOrDefault(p => string.Equals(p.Eik, pin, StringComparison.OrdinalIgnoreCase)) is { } byPin)
        {
            if (who.Length == 0 || string.Equals(who, byPin.Name, StringComparison.CurrentCultureIgnoreCase))
                who = byPin.Name;

            return (who, pin);
        }

        if (who.Length > 0)
        {
            var byName = known
                .Where(p => string.Equals(p.Name, who, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            // Едно и също име с два ЕИК-а са две дружества: тогава не се
            // гадае кое е.
            if (byName.Count == 1)
            {
                who = byName[0].Name;

                if (pin.Length == 0) pin = byName[0].Eik;
            }
        }

        return (who, pin);
    }

    /// <summary>Името на познат контрагент по ЕИК. Празно значи „не го знаем“.</summary>
    public string NameOf(long companyId, string eik, string role)
    {
        var pin = eik.Trim();
        if (pin.Length == 0) return string.Empty;

        return List(companyId, role)
            .FirstOrDefault(p => string.Equals(p.Eik, pin, StringComparison.OrdinalIgnoreCase))
            ?.Name ?? string.Empty;
    }

    /// <summary>
    /// Вписва наведнъж вече известни контрагенти, без да ги брои за ползвани.
    ///
    /// За началото на списъка: доставчиците, които вече стоят по партидите,
    /// не бива да чакат следващото си приемане, за да бъдат предлагани.
    /// Познатият не се вписва втори път. Връща колко са вписани.
    /// </summary>
    public int Seed(long companyId, IEnumerable<(string Eik, string Name)> known, string role)
    {
        var have = List(companyId, role);
        var added = 0;

        foreach (var (eik, name) in known)
        {
            var who = name.Trim();
            var pin = eik.Trim();

            if (who.Length == 0) continue;

            if (have.Any(p =>
                    (pin.Length > 0 && string.Equals(p.Eik, pin, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(p.Name, who, StringComparison.CurrentCultureIgnoreCase))) continue;

            if (Add(companyId, who, pin, role)) added++;
        }

        return added;
    }

    /// <summary>Колко получателя познава тази фирма — числото до копчето.</summary>
    public int Count(long companyId, string role = PartnerRole.Receiver) =>
        List(companyId, role).Count;

    // ------------------------------------------------------------- помощни

    /// <summary>
    /// Написаното без излишни интервали — отпред, отзад и по средата.
    ///
    /// Двоен интервал не се вижда на екрана, а прави от един договор два.
    /// </summary>
    public static string Tidy(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>„Претопи ООД — 205566778“ обратно до самото име.</summary>
    public static string NameOf(string? display)
    {
        var text = display?.Trim() ?? string.Empty;
        var dash = text.LastIndexOf(" — ", StringComparison.Ordinal);

        return dash > 0 ? text[..dash].Trim() : text;
    }

    /// <summary>Как се чете броят — за реда под списъка.</summary>
    public static string Counted(int count, string role = PartnerRole.Receiver) => role switch
    {
        PartnerRole.Originator => count switch
        {
            0 => Lang.T("няма запомнени доставчици", "no suppliers remembered yet"),
            1 => Lang.T("1 запомнен доставчик", "1 supplier remembered"),
            _ => Lang.T($"{count} запомнени доставчици", $"{count} suppliers remembered")
        },

        PartnerRole.Contract => count switch
        {
            0 => Lang.T("няма запомнени договори", "no contracts remembered yet"),
            1 => Lang.T("1 запомнен договор", "1 contract remembered"),
            _ => Lang.T($"{count} запомнени договора", $"{count} contracts remembered")
        },

        _ => count switch
        {
            0 => Lang.T("няма запомнени получатели", "no receivers remembered yet"),
            1 => Lang.T("1 запомнен получател", "1 receiver remembered"),
            _ => Lang.T($"{count} запомнени получатели", $"{count} receivers remembered")
        }
    };

    private static string Now => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static void Bind(
        SqliteCommand command, long companyId, string role, string eik, string name)
    {
        command.Parameters.AddWithValue("$company", companyId);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$eik", eik.Trim());
        command.Parameters.AddWithValue("$name", name);
    }

    private static DateTime ReadTime(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var when)
            ? when.ToLocalTime()
            : DateTime.MinValue;
}
