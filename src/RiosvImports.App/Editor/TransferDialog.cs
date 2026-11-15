using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;

namespace RiosvImports.App.Editor;

/// <summary>
/// Прозорчето, което пита за един ред.
///
/// Показва какво се пренася — кодът и количеството от изходния ред, които
/// човек не пипа — и над него полетата, които се различават. Тръгва от
/// стойностите в страничния панел, така че при петдесет еднакви реда остава
/// само Enter.
/// </summary>
internal sealed class TransferDialog : Dialog
{
    private readonly TransferFields _fields;
    private readonly CheckBox _always = new();

    public TransferDialog(
        TransferRule rule,
        SchemaDefinition schema,
        ReportNode target,
        IReadOnlyDictionary<string, string> defaults,
        string summary,
        string? sameCode = null,
        ValueLimits? limits = null)
    {
        Text = rule.Title(schema);
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        KeyPreview = true;

        var width = 420;

        _fields = new TransferFields(rule, target, width - Theme.GapLarge * 2, limits)
        {
            Location = new Point(Theme.GapLarge, Theme.GapSmall)
        };

        // Какво идва от самия ред: показва се, за да не се гадае кой ред е
        // натиснат, но не се пипа — количеството минава едно към едно.
        var source = new Label
        {
            Location = new Point(Theme.GapLarge, _fields.Bottom + Theme.Gap),
            Size = new Size(width - Theme.GapLarge * 2, Theme.Base.Height * 3),
            Font = Theme.Small,
            ForeColor = Theme.InkMuted,
            Text = summary
        };

        _always.Location = new Point(Theme.GapLarge, source.Bottom + Theme.GapSmall);
        _always.Size = new Size(width - Theme.GapLarge * 2, Theme.Base.Height * 2);
        _always.Text = Lang.T("Занапред без питане", "From now on, without asking");
        _always.BackColor = Color.Transparent;

        var buttons = new FlowLayoutPanel
        {
            Location = new Point(Theme.GapLarge, _always.Bottom + Theme.GapSmall),
            Size = new Size(width - Theme.GapLarge * 2, Theme.Base.Height + 24),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Canvas
        };

        var create = Theme.PrimaryButton(Lang.T("Създай реда", "Create the row"));
        create.Height = Theme.Base.Height + 16;
        create.FitWidth(16);
        create.Click += (_, _) => Accept();

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Height = create.Height;
        cancel.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        cancel.FitWidth(10);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(create);
        buttons.Controls.Add(cancel);

        Controls.Add(_fields);
        Controls.Add(source);
        Controls.Add(_always);
        Controls.Add(buttons);

        ClientSize = new Size(width, buttons.Bottom + Theme.Gap);

        AcceptButton = create;
        CancelButton = cancel;

        _fields.SetValues(defaults);

        // Заключеното поле се заключва СЛЕД стойностите: иначе запомненото от
        // предишния ред би застанало в него и би изглеждало като избор.
        if (sameCode is not null && rule.SameAsSource is { } locked)
            _fields.Lock(locked, sameCode);

        Theme.Apply(this);
    }

    /// <summary>Избраното — попълва се чак при „Създай реда“.</summary>
    public Dictionary<string, string> Values { get; private set; } = [];

    /// <summary>Дали занапред да се пренася без питане.</summary>
    public bool AlwaysFromNow => _always.Checked;

    private void Accept()
    {
        if (_fields.Missing() is { Count: > 0 } missing)
        {
            MessageBox.Show(this,
                Lang.T("Още не е попълнено: ", "Not filled in yet: ") + string.Join(", ", missing),
                Lang.T("Липсва стойност", "A value is missing"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Values = _fields.Values();
        DialogResult = DialogResult.OK;
    }
}
