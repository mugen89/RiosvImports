using RiosvImports.Data;

namespace RiosvImports.App;

/// <summary>
/// Фирмата, в която се работи в момента.
///
/// Досега всеки екран си избираше фирма сам и на четири места стояха четири
/// падащи списъка с едно и също съдържание. Те ядяха по двеста пиксела от
/// лентата на всеки екран — а на лаптоп точно тези пиксели не достигат — и
/// оставяха отворен въпроса какво значи „Месец“ да е на едно дружество, докато
/// „Склад“ стои на друго. Отговорът е, че не значи нищо: човек работи по една
/// фирма и минава на следващата, когато свърши с нея.
///
/// Затова изборът е един, стои в лентата отляво и важи навсякъде. Помни се
/// между пусканията: счетоводител, който вчера е приключил с една фирма, утре
/// продължава от нея, а не от първата по азбучен ред.
///
/// Тук се пази само КОЯ е фирмата. Какво показва всеки екран за нея си остава
/// негова работа.
/// </summary>
internal sealed class CompanyScope
{
    private readonly Workspace _workspace;

    private List<Company> _companies = [];
    private long? _current;

    public CompanyScope(Workspace workspace)
    {
        _workspace = workspace;
        Reload();
    }

    /// <summary>Сменена е фирмата — екраните показват друго.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Фирмата, по която се работи.
    ///
    /// Null само докато няма нито една заведена: тогава екраните казват какво
    /// липсва, вместо да показват празни таблици.
    /// </summary>
    public long? Current => _current;

    /// <summary>Заведените фирми — за списъка в лентата.</summary>
    public IReadOnlyList<Company> Companies => _companies;

    /// <summary>Избраната фирма с името и ЕИК-а ѝ.</summary>
    public Company? Company => _companies.FirstOrDefault(c => c.Id == _current);

    /// <summary>
    /// Заглавие на екран с фирмата: „Склад — Метал ООД“.
    ///
    /// На едно място, защото четири екрана го пишат еднакво, а разминаване в
    /// тирето или в реда на думите личи веднага при превключване между тях.
    /// Без заведена фирма остава само името на екрана — „Склад — “ е обещание
    /// за дума, която няма да дойде.
    /// </summary>
    public string Titled(string page) => Company is { Name.Length: > 0 } company
        ? $"{page} — {company.Name}"
        : page;

    /// <summary>
    /// Чете фирмите наново.
    ///
    /// Вика се след пипане в „Настройки“: заведена, поправена или премахната
    /// фирма мени и списъка, и понякога самия избор. Избраната се пази, ако я
    /// има още; иначе се пада към запомнената, а после към първата.
    /// </summary>
    public void Reload()
    {
        _companies = [.. _workspace.Companies.List()];

        var wanted = _current ?? Remembered();

        var chosen = _companies.FirstOrDefault(c => c.Id == wanted)
                     ?? _companies.FirstOrDefault();

        Switch(chosen?.Id);
    }

    /// <summary>Минава на друга фирма. Същата не се брои за смяна.</summary>
    public void Use(long? companyId)
    {
        if (companyId == _current) return;
        if (companyId is { } id && _companies.All(c => c.Id != id)) return;

        Switch(companyId);
    }

    // ------------------------------------------------------------- помощни

    private void Switch(long? companyId)
    {
        if (companyId == _current) return;

        _current = companyId;

        // Записва се веднага, а не при затваряне: програмата се затваря и с
        // изключване на тока, а изборът е дребна настройка, не работа.
        _workspace.Settings.Set(SettingsStore.Keys.CurrentCompany,
            companyId?.ToString() ?? string.Empty);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private long? Remembered() =>
        long.TryParse(_workspace.Settings.Get(SettingsStore.Keys.CurrentCompany), out var id) ? id : null;
}
