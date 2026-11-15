using Microsoft.Data.Sqlite;
using RiosvImports.Core.Localization;

namespace RiosvImports.Data;

/// <summary>Едно задължено лице.</summary>
public sealed record Company(long Id, string Eik, string Name)
{
    /// <summary>Как се показва в списък: име и ЕИК, защото имената се повтарят.</summary>
    public string Display => $"{Name} — {Eik}";
}

/// <summary>Защо една фирма не може да се запише.</summary>
/// <summary>
/// Какво е станало с ЕИК-а на внесен файл: коя фирма е разпозната, какво е
/// пишело във файла и дали ЕИК-ът е бил махнат като непознат.
/// </summary>
public sealed record ImportedEik(Company? Company, string FileEik, bool Cleared);

public enum CompanyProblem
{
    None,
    EmptyEik,
    EikNotNineOrThirteen,
    EikNotDigits,
    EmptyName,
    AlreadyExists,
    LimitReached
}

/// <summary>
/// Фирмите, за които се подават отчети.
///
/// Един счетоводител води по няколко задължени лица и до вчера трябваше да
/// помни кой ЕИК за кой отчет. Сега фирмата се избира от списък, а колко
/// фирми се допускат зависи от абонамента — проверката за това стои извън
/// хранилището, защото хранилището не знае нищо за абонаменти.
/// </summary>
public sealed class CompanyStore(SqliteConnection connection)
{
    private readonly SqliteConnection _connection = connection;

    public IReadOnlyList<Company> List()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, eik, name FROM companies ORDER BY name;";

        var result = new List<Company>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
            result.Add(new Company(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));

        return result;
    }

    public Company? Find(long id) => List().FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// Търси по ЕИК. Ползва се при внасяне от Excel: там ЕИК-ът идва като
    /// текст от чужд файл и трябва да се разпознае коя от заведените фирми е.
    /// </summary>
    public Company? FindByEik(string? eik)
    {
        var trimmed = eik?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? null
            : List().FirstOrDefault(c => c.Eik == trimmed);
    }

    /// <summary>
    /// Решава чия е внесената от Excel заглавна част и **чисти** ЕИК-а от нея,
    /// ако не е на заведена тук фирма.
    ///
    /// Файлът идва отвън и може да носи какъвто и да е ЕИК. Мълчаливо приетият
    /// чужд ЕИК е точно грешката, която се вижда чак когато НИСО върне вече
    /// подадения отчет — затова непознатият се маха, а фирмата се избира на
    /// ръка. Редовете си остават: те са свършената работа.
    ///
    /// Решението стои тук, а не в екрана, за да може да се провери с тест.
    /// </summary>
    public ImportedEik MatchImportedEik(IDictionary<string, string> header)
    {
        var fileEik = header.TryGetValue("eik", out var raw) ? raw.Trim() : string.Empty;
        var company = FindByEik(fileEik);

        if (company is null) header.Remove("eik");

        return new ImportedEik(company, fileEik, company is null && fileEik.Length > 0);
    }

    public int Count()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM companies;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Проверява дали фирмата може да се запише и защо не.
    ///
    /// Отделена от самото записване, за да може екранът да покаже причината,
    /// преди да е натиснат бутон, и за да се провери с тест без прозорци.
    ///
    /// ЕИК-ът е 9 цифри за търговци и 13 за клонове — това е форматът на
    /// Търговския регистър. Контролната цифра не се пресмята: сгрешен, но
    /// правдоподобен ЕИК ще бъде отхвърлен от НИСО, а програмата няма как да
    /// знае повече от регистъра.
    /// </summary>
    public CompanyProblem Validate(string? eik, string? name, int limit, long? excludeId = null)
    {
        var trimmed = eik?.Trim() ?? string.Empty;

        if (trimmed.Length == 0) return CompanyProblem.EmptyEik;
        if (!trimmed.All(char.IsAsciiDigit)) return CompanyProblem.EikNotDigits;
        if (trimmed.Length is not (9 or 13)) return CompanyProblem.EikNotNineOrThirteen;
        if (string.IsNullOrWhiteSpace(name)) return CompanyProblem.EmptyName;

        var existing = List();

        if (existing.Any(c => c.Eik == trimmed && c.Id != excludeId))
            return CompanyProblem.AlreadyExists;

        if (excludeId is null && existing.Count >= limit) return CompanyProblem.LimitReached;

        return CompanyProblem.None;
    }

    public long Add(string eik, string name)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO companies (eik, name, created_utc)
            VALUES ($eik, $name, $now)
            RETURNING id;
            """;

        command.Parameters.AddWithValue("$eik", eik.Trim());
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Update(long id, string eik, string name)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE companies SET eik = $eik, name = $name WHERE id = $id;";

        command.Parameters.AddWithValue("$eik", eik.Trim());
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$id", id);

        command.ExecuteNonQuery();
    }

    /// <summary>Колко отчета сочат към тази фирма — питат се преди изтриване.</summary>
    public int DocumentCount(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM documents WHERE company_id = $id;";
        command.Parameters.AddWithValue("$id", id);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Маха фирмата. Отчетите ѝ остават — само престават да сочат към нея.
    ///
    /// Изтриването на фирма не бива да носи със себе си работата по нея:
    /// подписаните отчети са документи и се пазят независимо от това дали
    /// фирмата още се води в програмата.
    /// </summary>
    public void Remove(long id)
    {
        using var transaction = _connection.BeginTransaction();

        using (var detach = _connection.CreateCommand())
        {
            detach.Transaction = transaction;
            detach.CommandText = "UPDATE documents SET company_id = NULL WHERE company_id = $id;";
            detach.Parameters.AddWithValue("$id", id);
            detach.ExecuteNonQuery();
        }

        using (var remove = _connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText = "DELETE FROM companies WHERE id = $id;";
            remove.Parameters.AddWithValue("$id", id);
            remove.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Обяснение на български за отказана фирма.</summary>
    public static string Explain(CompanyProblem problem, int limit) => problem switch
    {
        CompanyProblem.EmptyEik => Lang.T("Въведете ЕИК.", "Enter a UIC."),
        CompanyProblem.EikNotDigits => Lang.T("ЕИК се състои само от цифри.", "A UIC is digits only."),
        CompanyProblem.EikNotNineOrThirteen => Lang.T("ЕИК е 9 цифри, а за клон — 13.", "A UIC is 9 digits, or 13 for a branch."),
        CompanyProblem.EmptyName => Lang.T("Въведете наименование на фирмата.", "Enter the company name."),
        CompanyProblem.AlreadyExists => Lang.T("Такъв ЕИК вече е добавен.", "That UIC has already been added."),
        CompanyProblem.LimitReached =>
            Lang.T($"Абонаментът позволява {limit} фирми. Премахнете една или разширете абонамента.", $"Your subscription allows {limit} companies. Remove one, or move up a tier."),
        _ => string.Empty
    };
}
