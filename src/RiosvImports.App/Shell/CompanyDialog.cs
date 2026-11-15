using RiosvImports.App.Controls;
using RiosvImports.Data;

using RiosvImports.Core.Localization;

namespace RiosvImports.App.Shell;

/// <summary>
/// Добавяне или поправка на фирма.
///
/// Две полета и една проверка. Причината за отказ се показва още докато се
/// пише, а не чак при натискане на бутона: няма смисъл човек да попълни и
/// двете, за да научи, че ЕИК-ът е с една цифра по-малко.
/// </summary>
internal sealed class CompanyDialog : Dialog
{
    private readonly TextBox _eik = new();
    private readonly TextBox _name = new();
    private readonly Label _problem = new();
    private readonly AppButton _save;

    private readonly CompanyStore _store;
    private readonly int _limit;
    private readonly long? _editing;

    public CompanyDialog(CompanyStore store, int limit, Company? existing = null)
    {
        _store = store;
        _limit = limit;
        _editing = existing?.Id;

        Text = existing is null ? Lang.T("Нова фирма", "New company") : Lang.T("Фирма", "Company");
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(Theme.Scaled(520), Theme.Scaled(290));
        KeyPreview = true;

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.GapLarge) };

        _problem.Dock = DockStyle.Bottom;
        _problem.Height = Theme.Scaled(40);
        _problem.Font = Theme.Small;
        _problem.ForeColor = Theme.Error;

        host.Controls.Add(_problem);
        host.Controls.Add(Field(Lang.T("Наименование", "Name"), _name, existing?.Name));
        host.Controls.Add(Field(Lang.T("ЕИК", "UIC"), _eik, existing?.Eik));

        _save = Theme.PrimaryButton(existing is null ? Lang.T("Добави", "Add") : Lang.T("Запиши", "Save"));
        _save.Click += (_, _) => Accept();

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Height = Theme.Scaled(40);
        cancel.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = Theme.Scaled(60),
            Padding = new Padding(Theme.GapLarge, Theme.GapSmall, Theme.GapLarge, Theme.Gap)
        };

        buttons.Controls.Add(_save.FitWidth(40));
        buttons.Controls.Add(cancel.FitWidth(24));

        Controls.Add(host);
        Controls.Add(buttons);

        AcceptButton = _save;
        CancelButton = cancel;

        _eik.TextChanged += (_, _) => Recheck();
        _name.TextChanged += (_, _) => Recheck();

        Theme.Apply(this);
        Recheck();
    }

    // Не се казват Eik и Name: Control вече има и Name, и CompanyName, а
    // засенчването им се компилира мълчаливо и подава празен низ на проверката.
    public string EnteredEik => _eik.Text.Trim();
    public string EnteredName => _name.Text.Trim();

    private Control Field(string label, TextBox input, string? value)
    {
        var box = new Panel { Dock = DockStyle.Top, Height = Theme.Scaled(74) };

        input.Dock = DockStyle.Top;
        input.Font = Theme.Big;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Text = value ?? string.Empty;
        Theme.StyleInput(input);

        box.Controls.Add(input);
        box.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(24),
            Text = label,
            ForeColor = Theme.InkMuted
        });

        return box;
    }

    /// <summary>Проверката е същата, която пази и записа — само че показана рано.</summary>
    private void Recheck()
    {
        var problem = _store.Validate(EnteredEik, EnteredName, _limit, _editing);

        _problem.Text = CompanyStore.Explain(problem, _limit);
        _save.Enabled = problem == CompanyProblem.None;
        _save.Invalidate();
    }

    private void Accept()
    {
        if (_store.Validate(EnteredEik, EnteredName, _limit, _editing) != CompanyProblem.None) return;

        DialogResult = DialogResult.OK;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }

        return base.ProcessCmdKey(ref message, keyData);
    }
}
