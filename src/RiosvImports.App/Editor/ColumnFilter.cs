using RiosvImports.Core.Localization;

namespace RiosvImports.App.Editor;

/// <summary>
/// Кои стойности от една колона да се виждат.
///
/// Отчет с осемдесет реда се преглежда по части: всички редове с един и същи
/// код, или всички, които още не са пренесени. Досега това ставаше с
/// подредба и с пръст по екрана.
///
/// Списъкът е от това, което наистина стои в колоната — не от цялата
/// номенклатура. Осемстотин кода по ЕКО са възможни, но в един отчет се
/// срещат пет.
/// </summary>
internal sealed class ColumnFilter : Dialog
{
    private readonly TextBox _search = new();
    private readonly CheckedListBox _list = new();
    private readonly IReadOnlyList<string> _values;

    public ColumnFilter(string column, IReadOnlyList<string> values, IReadOnlySet<string>? allowed)
    {
        _values = values;

        Text = Lang.T($"Филтър — {column}", $"Filter — {column}");
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MaximizeBox = false;
        ClientSize = new Size(Theme.Scaled(420), Theme.Scaled(460));
        KeyPreview = true;

        var caption = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Base.Height + 10,
            Text = Lang.T(
                $"{values.Count} различни стойности в колоната.",
                $"{values.Count} distinct values in the column."),
            ForeColor = Theme.InkMuted
        };

        _search.Dock = DockStyle.Top;
        _search.Font = Theme.Big;
        _search.BorderStyle = BorderStyle.FixedSingle;
        Theme.StyleInput(_search);
        _search.TextChanged += (_, _) => Refill();

        _list.Dock = DockStyle.Fill;
        _list.BorderStyle = BorderStyle.None;
        _list.IntegralHeight = false;
        _list.CheckOnClick = true;
        Theme.StyleInput(_list);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Base.Height + 34,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Canvas
        };

        var ok = Theme.PrimaryButton(Lang.T("Покажи", "Show"));
        ok.Height = Theme.Base.Height + 16;
        ok.FitWidth(14);
        ok.Click += (_, _) => Accept();

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Height = ok.Height;
        cancel.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        cancel.FitWidth(10);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        var all = Theme.GhostButton(Lang.T("Всички", "All"), null);
        all.Height = ok.Height;
        all.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        all.FitWidth(6);
        all.Click += (_, _) => SetAll(true);

        var none = Theme.GhostButton(Lang.T("Нито една", "None"), null);
        none.Height = ok.Height;
        none.FitWidth(6);
        none.Click += (_, _) => SetAll(false);

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(all);
        buttons.Controls.Add(none);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Theme.Canvas };
        host.Controls.Add(_list);
        host.Controls.Add(_search);
        host.Controls.Add(caption);
        host.Controls.Add(buttons);

        Controls.Add(host);

        AcceptButton = ok;
        CancelButton = cancel;

        // Без предишен филтър всичко е отметнато: така „Нито една“ плюс две
        // отметки е най-краткият път до „само тези двете“.
        Chosen = allowed is null ? [.. values] : [.. allowed];

        Refill();
        Theme.Apply(this);
    }

    /// <summary>Избраните стойности. Всички отметнати значи „без филтър“.</summary>
    public HashSet<string> Chosen { get; private set; }

    /// <summary>Дали филтърът пропуска всичко — тогава просто се маха.</summary>
    public bool ShowsEverything => Chosen.Count == _values.Count;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _search.Focus();
    }

    private void Refill()
    {
        var query = _search.Text.Trim();

        var shown = query.Length == 0
            ? _values
            : [.. _values.Where(v => v.Contains(query, StringComparison.CurrentCultureIgnoreCase))];

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var value in shown) _list.Items.Add(value, Chosen.Contains(value));

        _list.EndUpdate();
    }

    /// <summary>Отмята или маха отметките само на показаното в момента.</summary>
    private void SetAll(bool on)
    {
        for (var i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, on);

        Harvest();
    }

    /// <summary>
    /// Прибира отметките от видимия списък, без да губи скритото от търсенето.
    /// </summary>
    private void Harvest()
    {
        for (var i = 0; i < _list.Items.Count; i++)
        {
            var value = _list.Items[i].ToString() ?? string.Empty;

            if (_list.GetItemChecked(i)) Chosen.Add(value);
            else Chosen.Remove(value);
        }
    }

    private void Accept()
    {
        Harvest();
        DialogResult = DialogResult.OK;
    }
}
