using RiosvImports.Core.Schema;

using RiosvImports.Core.Localization;

namespace RiosvImports.App.Editor;

/// <summary>
/// Търсачка за дългите изброени типове.
///
/// Кодовете по ЕКО са над осемстотин. Падащо меню с толкова редове се превърта
/// по-бавно, отколкото се пише, затова тук се пише част от кода или от името и
/// списъкът се свива. Enter избира, Escape отказва — без нито едно движение с
/// мишка.
/// </summary>
internal sealed class OptionPicker : Dialog
{
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly IReadOnlyList<EnumOption> _options;

    public OptionPicker(string title, IReadOnlyList<EnumOption> options, string? current)
    {
        _options = options;

        Text = title;
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MaximizeBox = false;
        ClientSize = new Size(Theme.Scaled(620), Theme.Scaled(460));
        KeyPreview = true;

        var caption = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(34),
            Padding = new Padding(2, 8, 2, 2),
            Text = Lang.T($"Изберете от {options.Count} стойности — пишете, за да търсите.", $"Choose from {options.Count} values — type to search."),
            ForeColor = Theme.InkMuted
        };

        _search.Dock = DockStyle.Top;
        _search.Font = Theme.Big;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.Margin = new Padding(0, 0, 0, 8);
        Theme.StyleInput(_search);
        _search.TextChanged += (_, _) => Refill();

        // Стрелките местят из списъка, докато курсорът стои в полето за търсене:
        // иначе всяко търсене иска допълнително натискане на Tab.
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Down or Keys.Up)) return;

            var step = e.KeyCode == Keys.Down ? 1 : -1;
            var next = _list.SelectedIndex + step;

            if (next >= 0 && next < _list.Items.Count) _list.SelectedIndex = next;
            e.Handled = true;
        };

        _list.Dock = DockStyle.Fill;
        _list.BorderStyle = BorderStyle.None;
        _list.IntegralHeight = false;
        _list.ItemHeight = Theme.Scaled(22);
        Theme.StyleInput(_list);
        _list.DoubleClick += (_, _) => Accept();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = Theme.Scaled(52),
            Padding = new Padding(0, 10, 0, 0)
        };

        var ok = Theme.SecondaryButton(Lang.T("Избери", "Choose"));
        ok.Width = Theme.Scaled(110);
        ok.Click += (_, _) => Accept();

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Width = Theme.Scaled(110);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) };
        host.Controls.Add(_list);
        host.Controls.Add(_search);
        host.Controls.Add(caption);
        host.Controls.Add(buttons);

        Controls.Add(host);

        AcceptButton = ok;
        CancelButton = cancel;

        Refill();
        Preselect(current);

        Theme.Apply(this);
    }

    public EnumOption? Selected { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _search.Focus();
    }

    private void Refill()
    {
        var query = _search.Text.Trim();

        var matches = query.Length == 0
            ? _options
            : _options.Where(o =>
                o.Value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (o.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false))
              .ToList();

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var option in matches) _list.Items.Add(option.Display);

        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.EndUpdate();
    }

    private void Preselect(string? current)
    {
        if (string.IsNullOrWhiteSpace(current)) return;

        var match = _options.FirstOrDefault(o =>
            string.Equals(o.Value, current, StringComparison.OrdinalIgnoreCase));

        if (match is not null) _list.SelectedItem = match.Display;
    }

    private void Accept()
    {
        if (_list.SelectedItem is not string display) return;

        Selected = _options.FirstOrDefault(o => o.Display == display);
        DialogResult = DialogResult.OK;
    }
}
