using RiosvImports.Core.Localization;

namespace RiosvImports.App.Editor;

/// <summary>
/// Пита за едно име. Windows Forms няма такова прозорче, а да се вика
/// Visual Basic заради един ред въвеждане не е сделка.
/// </summary>
internal sealed class NameDialog : Dialog
{
    private readonly TextBox _input = new();

    private NameDialog(string caption, string current)
    {
        Text = caption;
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(Theme.Scaled(400), Theme.Base.Height + 96);

        _input.Location = new Point(Theme.Gap, Theme.Gap);
        _input.Size = new Size(ClientSize.Width - Theme.Gap * 2, Theme.Big.Height + 10);
        _input.Font = Theme.Big;
        _input.BorderStyle = BorderStyle.FixedSingle;
        _input.Text = current;
        Theme.StyleInput(_input);

        var ok = Theme.PrimaryButton(Lang.T("Готово", "Done"));
        ok.Height = Theme.Base.Height + 16;
        ok.FitWidth(12);
        ok.Location = new Point(ClientSize.Width - Theme.Gap - ok.Width, _input.Bottom + Theme.Gap);
        ok.Click += (_, _) => { Value = _input.Text.Trim(); DialogResult = DialogResult.OK; };

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Height = ok.Height;
        cancel.FitWidth(10);
        cancel.Location = new Point(ok.Left - Theme.GapSmall - cancel.Width, ok.Top);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        Controls.Add(_input);
        Controls.Add(ok);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        Theme.Apply(this);
    }

    private string Value { get; set; } = string.Empty;

    /// <summary>Връща въведеното или празно при отказ.</summary>
    public static string Ask(IWin32Window owner, string caption, string current)
    {
        using var dialog = new NameDialog(caption, current);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Value : string.Empty;
    }
}
