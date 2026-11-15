using RiosvImports.Core.Localization;

namespace RiosvImports.App.Editor;

/// <summary>Какво да стане с група създадени редове.</summary>
internal enum GroupChoice
{
    Cancel,

    /// <summary>Отваря разпределението наново.</summary>
    Redistribute,

    /// <summary>Маха цялата група.</summary>
    DeleteAll
}

/// <summary>
/// Пита какво да стане с група редове, създадени наведнъж.
///
/// Един от три реда не се маха сам: петдесетте тона са разпределени между
/// трите и без единия остават четиридесет и седем, които вече не отговарят на
/// нищо. Затова изборът е между двете неща, които запазват сметката цяла.
/// </summary>
internal sealed class GroupDialog : Dialog
{
    private GroupDialog(int rows, string where)
    {
        Text = Lang.T("Свързани редове", "Linked rows");
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var width = 560;

        var text = new Label
        {
            Location = new Point(Theme.Gap, Theme.Gap),
            Size = new Size(width - Theme.Gap * 2, Theme.Base.Height * 4),
            Text = Lang.T(
                $"Този ред е част от {rows} реда в „{where}“, създадени наведнъж от един ред. " +
                "Количествата им се събират точно колкото е влязло.\r\n\r\n" +
                "Затова единият не се маха сам: или разпределете количеството наново, " +
                "или махнете всичките.",
                $"This row is one of {rows} rows in “{where}” created at once from a single row. " +
                "Their amounts add up to exactly what came in.\r\n\r\n" +
                "So one of them cannot go alone: either split the amount again, " +
                "or remove all of them.")
        };

        var buttons = new FlowLayoutPanel
        {
            Location = new Point(Theme.Gap, text.Bottom + Theme.Gap),
            Size = new Size(width - Theme.Gap * 2, Theme.Base.Height + 26),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Canvas
        };

        var again = Theme.PrimaryButton(Lang.T("Разпредели наново…", "Split again…"));
        again.Height = Theme.Base.Height + 16;
        again.FitWidth(14);
        again.Click += (_, _) => { Choice = GroupChoice.Redistribute; DialogResult = DialogResult.OK; };

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Height = again.Height;
        cancel.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        cancel.FitWidth(10);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        var all = Theme.DangerButton(Lang.T($"Изтрий всичките {rows}", $"Delete all {rows}"), null);
        all.Height = again.Height;
        all.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        all.FitWidth(10);
        all.Click += (_, _) => { Choice = GroupChoice.DeleteAll; DialogResult = DialogResult.OK; };

        buttons.Controls.Add(again);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(all);

        Controls.Add(text);
        Controls.Add(buttons);

        ClientSize = new Size(width, buttons.Bottom + Theme.Gap);

        AcceptButton = again;
        CancelButton = cancel;

        Theme.Apply(this);
    }

    private GroupChoice Choice { get; set; } = GroupChoice.Cancel;

    public static GroupChoice Ask(IWin32Window owner, int rows, string where)
    {
        using var dialog = new GroupDialog(rows, where);
        dialog.ShowDialog(owner);

        return dialog.Choice;
    }
}
