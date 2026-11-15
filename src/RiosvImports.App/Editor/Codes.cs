using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;

namespace RiosvImports.App.Editor;

/// <summary>
/// Дългите списъци с кодове — веднъж построени, после раздавани.
///
/// Две неща се крият тук.
///
/// Първото е бъг на Windows Forms: с
/// <see cref="AutoCompleteSource.ListItems"/> върху списък от осемстотин реда
/// стрелката спира да работи с мишка — списъкът се отваря и веднага се
/// затваря, и остава само F4. Същият списък, подаден като собствен източник,
/// се държи както трябва.
///
/// Второто е цената. Кодовете по ЕКО са над осемстотин и всяко ново падащо
/// меню ги преписва наново. В прозорчето за разделяне менютата са толкова,
/// колкото са редовете, и всяко натискане на „+“ ги правеше отначало: първо
/// секунда, после две, после три. Списъкът обаче е един и същ за цялата
/// програма — схемата се чете веднъж и не се мени — затова се прави веднъж и
/// се дава наготово.
///
/// Пази се по самия списък, а не по колона: една и съща колона получава
/// различно меню за различна фирма, откакто има кратки списъци. Затова ключът
/// е списъкът — <see cref="ValueLimits"/> връща един и същ за един и същ
/// обхват, така че намирането пак е едно търсене в речник.
/// </summary>
internal static class Codes
{
    /// <summary>Всичко тук се пипа само от нишката на екрана.</summary>
    private static readonly Dictionary<IReadOnlyList<EnumOption>, AutoCompleteStringCollection> Completion =
        new(ReferenceEqualityComparer.Instance);

    private static readonly Dictionary<IReadOnlyList<EnumOption>, object[]> Listed =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Показваните стойности, с празна отпред — за ComboBox.Items.</summary>
    public static object[] Items(IReadOnlyList<EnumOption> options)
    {
        if (Listed.TryGetValue(options, out var items)) return items;

        items = [string.Empty, .. options.Select(o => (object)o.Display)];
        Listed[options] = items;

        return items;
    }

    /// <summary>
    /// Приготвя падащо меню за дълъг списък.
    ///
    /// Списъкът не се сипва веднага. Осемстотин реда, подадени на готово
    /// меню, струват четвърт секунда — веднъж е нищо, но в прозорчето за
    /// разделяне менютата са колкото редовете и всяко „+“ ги плащаше отново.
    /// Сипват се, когато някой отвори менюто; дотогава дописването по
    /// въведеното работи и без тях, защото чете от собствения източник.
    ///
    /// IntegralHeight изключено спестява по едно преоразмеряване на ред.
    /// </summary>
    public static void Fill(ComboBox combo, IReadOnlyList<EnumOption> options)
    {
        combo.IntegralHeight = false;
        Complete(combo, options);

        combo.DropDown += Load;

        void Load(object? sender, EventArgs e)
        {
            combo.DropDown -= Load;

            combo.BeginUpdate();
            combo.Items.AddRange(Items(options));
            combo.EndUpdate();
        }
    }

    /// <summary>Включва дописването по въведеното.</summary>
    public static void Complete(ComboBox combo, IReadOnlyList<EnumOption> options)
    {
        if (!Completion.TryGetValue(options, out var source))
        {
            source = new AutoCompleteStringCollection();
            source.AddRange([.. options.Select(o => o.Display)]);
            Completion[options] = source;
        }

        combo.AutoCompleteCustomSource = source;
        combo.AutoCompleteSource = AutoCompleteSource.CustomSource;
        combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
    }
}
