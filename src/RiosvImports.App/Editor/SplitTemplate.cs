using System.Text.Json;
using RiosvImports.Data;

namespace RiosvImports.App.Editor;

/// <summary>Един дял от шаблона: код по ЕКО и колко процента от количеството отива в него.</summary>
/// <param name="Ewc">Кодът по ЕКО, както се пази — без описанието.</param>
/// <param name="Percent">Дял в проценти. Сборът на един шаблон е сто.</param>
internal sealed record SplitPart(string Ewc, decimal Percent);

/// <summary>
/// Наименован начин на разделяне: „Стар кабел“ = 70 % мед, 25 % пластмаса,
/// 5 % остатък.
///
/// Едно и също нещо се разкомплектова по един и същи начин всеки месец.
/// Кодовете и делът им не се променят — променя се само количеството. Затова
/// се записват веднъж и после се прилагат с един избор.
/// </summary>
internal sealed record SplitTemplate(string Name, IReadOnlyList<SplitPart> Parts)
{
    /// <summary>Сборът на дяловете. Сто значи, че шаблонът покрива всичко.</summary>
    public decimal Total => Parts.Sum(p => p.Percent);
}

/// <summary>
/// Къде живеят шаблоните.
///
/// В настройките, като текст: те са предпочитание на човека, а не част от
/// отчета, и важат за всички отчети, които той попълва. Отделен ключ за всеки
/// раздел — дяловете при образуване нямат нищо общо с тези другаде.
/// </summary>
internal static class SplitTemplates
{
    private static string KeyFor(string node) => "split.templates." + node;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static List<SplitTemplate> Load(SettingsStore settings, string node)
    {
        var raw = settings.Get(KeyFor(node));
        if (string.IsNullOrWhiteSpace(raw)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<SplitTemplate>>(raw, Options) ?? [];
        }
        catch (JsonException)
        {
            // Развален запис не е повод програмата да не се отвори; шаблоните
            // са удобство и се правят наново за половин минута.
            return [];
        }
    }

    public static void Save(SettingsStore settings, string node, IEnumerable<SplitTemplate> templates) =>
        settings.Set(KeyFor(node), JsonSerializer.Serialize(templates.ToList(), Options));

    /// <summary>Добавя или замества шаблон с това име и връща новия списък.</summary>
    public static List<SplitTemplate> Put(
        SettingsStore settings, string node, SplitTemplate template)
    {
        var all = Load(settings, node);
        all.RemoveAll(t => string.Equals(t.Name, template.Name, StringComparison.CurrentCultureIgnoreCase));
        all.Add(template);
        all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));

        Save(settings, node, all);
        return all;
    }

    public static List<SplitTemplate> Remove(SettingsStore settings, string node, string name)
    {
        var all = Load(settings, node);
        all.RemoveAll(t => string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase));

        Save(settings, node, all);
        return all;
    }
}
