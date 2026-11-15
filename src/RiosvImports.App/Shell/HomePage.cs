using RiosvImports.App.Controls;
using RiosvImports.App.Updates;
using RiosvImports.Data;

using RiosvImports.Core.Localization;

namespace RiosvImports.App.Shell;

/// <summary>
/// Началният екран: какво ново има, какво е състоянието и с какво да се
/// продължи.
///
/// Умишлено е рядък. Отваря се при всяко стартиране и не бива да иска четене —
/// три неща, всяко от които се разбира от един поглед.
///
/// Известията стоят най-горе, под поздрава: те са единственото тук, което се
/// мени от само себе си. При пускане програмата пита GitHub за ново издание и
/// за новини, и отговорът идва тук — не в прозорец, който спира работата.
/// </summary>
internal sealed class HomePage : ContentPage
{
    private readonly Workspace _workspace;
    private readonly UpdateService _updates;
    private readonly ToolTip _tips = new();

    private readonly Label _greeting = new();
    private readonly Label _accountText = new();
    private readonly Label _accountGlyph = new();
    private readonly Card _accountCard = new();
    private readonly FlowLayoutPanel _notices = new();
    private readonly Label _noticesTitle = new();
    private readonly FlowLayoutPanel _recent = new();
    private readonly FlowLayoutPanel _stack = new();

    private AppButton _check = null!;

    /// <summary>Картата на обновяването, докато се сваля — пипа се на място, а не наново.</summary>
    private NoticeCard? _updateCard;

    private string? _updateCardKey;

    /// <summary>
    /// Новото в това пускане. Показаното се отбелязва като прочетено веднага, а
    /// „· ново“ трябва да преживее следващото пресъздаване на картите — иначе
    /// изчезва при първата смяна на състоянието, преди окото да го е хванало.
    /// </summary>
    private readonly HashSet<string> _fresh = new(StringComparer.Ordinal);

    /// <summary>Повече новини от това не изтласкват „Продължете оттам…“ под ръба.</summary>
    private const int NewsShown = 4;

    private bool _allNews;

    public HomePage(Workspace workspace, UpdateService? updates = null) : base(1100)
    {
        _workspace = workspace;
        _updates = updates ?? new UpdateService(prerelease => UpdateBackends.Create(prerelease, AppServices.Web));

        _stack.Dock = DockStyle.Fill;
        _stack.FlowDirection = FlowDirection.TopDown;
        _stack.WrapContents = false;
        _stack.AutoScroll = true;
        _stack.BackColor = Theme.Canvas;

        _stack.Controls.Add(BuildGreeting());
        _stack.Controls.Add(BuildNoticesHeader());
        _stack.Controls.Add(BuildNotices());
        _stack.Controls.Add(BuildAccountCard());
        _stack.Controls.Add(Section(Lang.T("Продължете оттам, докъдето сте стигнали", "Pick up where you left off")));
        _stack.Controls.Add(BuildRecent());

        Controls.Add(_stack);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Картите следват ширината на страницата, а не обратното.</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        // Ширината идва от самата подложка, а не от тавана. Отстоянията вляво
        // и вдясно се занулят: подредбата в поток ги брои към съдържанието и
        // при постоянните три пиксела по подразбиране се появява воден плъзгач,
        // без нищо да е излязло извън прозореца.
        var width = Math.Max(320, _stack.ClientSize.Width - 2);

        foreach (Control child in _stack.Controls)
        {
            if (child.Margin.Left != 0 || child.Margin.Right != 0)
                child.Margin = new Padding(0, child.Margin.Top, 0, child.Margin.Bottom);

            if (child.Width != width) child.Width = width;
        }

        foreach (var list in new[] { _recent, _notices })
            foreach (Control card in list.Controls)
            {
                if (card.Margin.Left != 0 || card.Margin.Right != 0)
                    card.Margin = new Padding(0, card.Margin.Top, 0, card.Margin.Bottom);

                if (card.Width != width) card.Width = width;
            }
    }

    public event EventHandler? GoToWork;
    public event EventHandler? NewReport;

    /// <summary>Щракнат отчет от „Продължете оттам…“ — отваря се направо, не списъкът.</summary>
    public event EventHandler<long>? OpenReport;

    /// <summary>„Провери сега“ — обвивката пита и за изданието, и за новините.</summary>
    public event EventHandler? CheckRequested;

    /// <summary>„Рестартирай и обнови“ — обвивката затваря програмата и пуска инсталатора.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Махнато или прочетено известие — броячът в лентата трябва да го научи.</summary>
    public event EventHandler? NoticesChanged;

    // ------------------------------------------------------------- устройство

    private Control BuildGreeting()
    {
        var host = new Panel
        {
            Width = ContentWidth,
            Height = Theme.Scaled(132),
            BackColor = Theme.Canvas,
            Margin = new Padding(0, 0, 0, Theme.GapSmall)
        };

        _greeting.Dock = DockStyle.Top;
        _greeting.Height = Theme.Scaled(44);
        _greeting.Font = Theme.Display;
        _greeting.ForeColor = Theme.Ink;

        var sub = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(28),
            Font = Theme.Big,
            ForeColor = Theme.InkMuted,
            Text = Lang.T("Отчетите към НИСО — попълване, проверка и подпис на едно място.", "NWIS reports — filling in, checking and signing in one place.")
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Scaled(52),
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Theme.Canvas
        };

        // Ширината се смята от текста: българските надписи са по-дълги от
        // английските и всяка постоянна стойност рано или късно реже дума.
        var start = Theme.PrimaryButton(Lang.T("Нов отчет", "New report"));
        start.Glyph = Theme.Glyph.Add;
        start.Click += (_, _) => NewReport?.Invoke(this, EventArgs.Empty);
        start.FitWidth(28);

        var open = Theme.SecondaryButton(Lang.T("Всички отчети", "All reports"));
        open.Glyph = Theme.Glyph.Documents;
        open.Height = Theme.Scaled(40);
        open.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        open.Click += (_, _) => GoToWork?.Invoke(this, EventArgs.Empty);
        open.FitWidth(28);

        actions.Controls.Add(start);
        actions.Controls.Add(open);

        host.Controls.Add(actions);
        host.Controls.Add(sub);
        host.Controls.Add(_greeting);

        return host;
    }

    /// <summary>
    /// „Известия“ с копче за проверка отдясно. Копчето е за онзи, който е
    /// изключил проверката при пускане или чака издание, за което е чул.
    /// </summary>
    private Control BuildNoticesHeader()
    {
        var height = Math.Max(Theme.Scaled(36), Theme.SmallBold.Height + Theme.Scaled(14));

        var header = new Panel
        {
            Width = ContentWidth,
            Height = height,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, Theme.Gap, 0, Theme.GapSmall)
        };

        _noticesTitle.Dock = DockStyle.Fill;
        _noticesTitle.Font = Theme.SmallBold;
        _noticesTitle.ForeColor = Theme.InkFaint;
        _noticesTitle.TextAlign = ContentAlignment.BottomLeft;
        _noticesTitle.Text = Lang.T("Известия", "Notifications");

        _check = Theme.GhostButton(Lang.T("Провери сега", "Check now"), Theme.Glyph.Sync);
        _check.Dock = DockStyle.Right;
        _check.Height = height;
        _check.Click += (_, _) => CheckRequested?.Invoke(this, EventArgs.Empty);
        _check.FitWidth(12);

        header.Controls.Add(_noticesTitle);
        header.Controls.Add(_check);

        return header;
    }

    private Control BuildNotices()
    {
        _notices.Width = ContentWidth;
        _notices.AutoSize = true;
        _notices.FlowDirection = FlowDirection.TopDown;
        _notices.WrapContents = false;
        _notices.BackColor = Theme.Canvas;
        _notices.Margin = new Padding(0, 0, 0, Theme.GapSmall);

        return _notices;
    }

    private Control BuildAccountCard()
    {
        _accountCard.Width = ContentWidth;
        _accountCard.Height = Theme.Scaled(70);
        _accountCard.Margin = new Padding(0, Theme.Gap, 0, 0);
        _accountCard.Padding = new Padding(Theme.Gap, 0, Theme.Gap, 0);

        _accountGlyph.Dock = DockStyle.Left;
        _accountGlyph.Width = Theme.Scaled(40);
        _accountGlyph.Font = Theme.IconLarge;
        _accountGlyph.TextAlign = ContentAlignment.MiddleLeft;

        _accountText.Dock = DockStyle.Fill;
        _accountText.TextAlign = ContentAlignment.MiddleLeft;
        _accountText.Font = Theme.Big;

        _accountCard.Controls.Add(_accountText);
        _accountCard.Controls.Add(_accountGlyph);

        return _accountCard;
    }

    private Control BuildRecent()
    {
        _recent.Width = ContentWidth;
        _recent.AutoSize = true;
        _recent.FlowDirection = FlowDirection.TopDown;
        _recent.WrapContents = false;
        _recent.BackColor = Theme.Canvas;
        _recent.Margin = new Padding(0, 0, 0, Theme.GapSmall);

        return _recent;
    }

    private Label Section(string text) => new()
    {
        Text = text,
        Font = Theme.SmallBold,
        ForeColor = Theme.InkFaint,
        Width = ContentWidth,
        Height = Theme.Scaled(32),
        Margin = new Padding(0, Theme.GapLarge, 0, Theme.GapSmall),
        TextAlign = ContentAlignment.BottomLeft
    };

    // ------------------------------------------------------------- пълнене

    public new void Refresh()
    {
        _greeting.Text = Greeting();

        var account = AppServices.Account;
        var ok = account.CanSign;

        _accountText.Text = account.Describe();
        _accountText.ForeColor = ok ? Theme.Ok : Theme.Warn;
        _accountGlyph.Text = ok ? Theme.Glyph.Shield : Theme.Glyph.Warning;
        _accountGlyph.ForeColor = ok ? Theme.Ok : Theme.Warn;
        _accountCard.Fill = ok ? Theme.OkSoft : Theme.WarnSoft;
        _accountCard.Outline = ok ? Theme.OkSoft : Theme.WarnSoft;
        _accountCard.Stripe = ok ? Theme.Ok : Theme.Warn;
        _accountCard.Invalidate();

        FillRecent();
        FillNotices();

        base.Refresh();
    }

    /// <summary>
    /// Състоянието на обновяването се е сменило. Докато се сваля, картата само
    /// сменя числото си — стотина пресъздавания за едно сваляне мигат.
    /// </summary>
    public void ShowUpdates()
    {
        var state = _updates.State;

        ShowChecking(state);

        if (_updateCard is { IsDisposed: false } card && state.Stage == UpdateStage.Downloading &&
            _updateCardKey == Key(state))
        {
            card.Say(DownloadTitle(state), Lang.T($"сега: {_updates.CurrentVersion}", $"now: {_updates.CurrentVersion}"),
                card.Message);
            card.Progress = state.Progress;
            return;
        }

        FillNotices();
    }

    private void ShowChecking(UpdateState state)
    {
        var busy = state.Stage == UpdateStage.Checking;

        _check.Text = busy ? Lang.T("Проверява се…", "Checking…") : Lang.T("Провери сега", "Check now");
        _check.Enabled = !busy;
        _check.FitWidth(12);
    }

    private static string Key(UpdateState state) => $"{state.Stage}|{state.Version}";

    private static string Greeting() => DateTime.Now.Hour switch
    {
        < 11 => Lang.T("Добро утро.", "Good morning."),
        < 18 => Lang.T("Добър ден.", "Good afternoon."),
        _ => Lang.T("Добър вечер.", "Good evening.")
    };

    private void FillRecent()
    {
        Clear(_recent);

        var documents = _workspace.Documents.List().Take(4).ToList();

        if (documents.Count == 0)
        {
            _recent.Controls.Add(Empty(Lang.T("Още няма започнати отчети. Започнете с бутона „Нов отчет“.", "No reports started yet. Begin with the “New report” button.")));
            return;
        }

        foreach (var document in documents) _recent.Controls.Add(RecentRow(document));
    }

    private Control RecentRow(DocumentSummary document)
    {
        var card = new ClickableCard
        {
            Width = ContentWidth,
            Height = Theme.Scaled(58),
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(Theme.Gap, 0, Theme.Gap, 0)
        };

        var status = new Label
        {
            Dock = DockStyle.Right,
            Width = Theme.Scaled(300),
            Text = $"{document.StatusLabel}   ·   {document.UpdatedUtc.ToLocalTime():dd.MM.yyyy, HH:mm}",
            ForeColor = document.Status == DocumentStatus.Signed ? Theme.Ok : Theme.InkFaint,
            Font = Theme.Small,
            TextAlign = ContentAlignment.MiddleRight
        };

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = document.Title,
            Font = Theme.Base,
            ForeColor = Theme.Ink,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };

        card.Controls.Add(title);
        card.Controls.Add(status);

        // Направо в отчета: от „Продължете оттам…“ човек иска да продължи, а не
        // да търси същия ред още веднъж в списъка.
        card.Click += (_, _) => OpenReport?.Invoke(this, document.Id);
        card.MakeChildrenTransparent();

        return card;
    }

    // ------------------------------------------------------------- известия

    /// <summary>Изданието, чиято покана човекът е скрил. По-новото пак се показва.</summary>
    private string? Skipped => _workspace.Settings.Get(SettingsStore.Keys.UpdateSkipped);

    private void FillNotices()
    {
        _notices.SuspendLayout();

        try
        {
            Clear(_notices);

            _updateCard = UpdateCard();
            _updateCardKey = _updateCard is null ? null : Key(_updates.State);

            if (_updateCard is not null) _notices.Controls.Add(_updateCard);

            var items = _workspace.News.List(20);
            var unseen = items.Where(i => !i.Seen).ToList();

            foreach (var item in unseen) _fresh.Add(item.Id);

            var shown = _allNews ? items : items.Take(NewsShown).ToList();

            foreach (var item in shown) _notices.Controls.Add(NewsCard(item));

            if (items.Count > shown.Count) _notices.Controls.Add(MoreNews(items.Count - shown.Count));

            if (_notices.Controls.Count == 0) _notices.Controls.Add(NothingNew());

            var fresh = items.Count(i => _fresh.Contains(i.Id));

            _noticesTitle.Text = fresh > 0
                ? Lang.T($"Известия · {fresh} нови", $"Notifications · {fresh} new")
                : Lang.T("Известия", "Notifications");

            // Видяното тук е прочетено — броячът в лентата слиза на нула.
            foreach (var item in unseen) _workspace.News.MarkSeen(item.Id);

            if (unseen.Count > 0) NoticesChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _notices.ResumeLayout(performLayout: true);
        }

        ShowChecking(_updates.State);
        PerformLayout();
    }

    /// <summary>
    /// Картата на обновяването — каквото и докъдето да е стигнало. Проверката
    /// без резултат не оставя карта: „няма ново“ се казва в настройките.
    /// </summary>
    private NoticeCard? UpdateCard()
    {
        var state = _updates.State;
        var release = state.Release;
        var now = Lang.T($"сега: {_updates.CurrentVersion}", $"now: {_updates.CurrentVersion}");

        if (release is null) return null;

        var notes = ReleaseNotes.Plain(release.Notes, 600);
        NoticeCard card;

        switch (state.Stage)
        {
            case UpdateStage.Available when Skipped == release.Version:
                return null;

            case UpdateStage.Available when _updates.CanApply:
                card = new NoticeCard(
                    Lang.T($"Налично е издание {release.Version}", $"Version {release.Version} is available"), now,
                    notes.Length > 0 ? notes : Lang.T(
                        "Сваля се само разликата спрямо това издание, а работата продължава, докато трае.",
                        "Only the difference from this version is downloaded, and work carries on meanwhile."));

                card.AddAction(Lang.T("Обнови сега", "Update now"), Theme.Glyph.Download, ButtonVariant.Primary,
                    () => _ = _updates.DownloadAsync());
                AddReleasePage(card, release);
                AllowSkip(card, release);
                break;

            case UpdateStage.Available:
                card = new NoticeCard(
                    Lang.T($"Има издание {release.Version}", $"Version {release.Version} is out"), now,
                    (notes.Length > 0 ? notes + Environment.NewLine + Environment.NewLine : string.Empty) +
                    Lang.T("Тази програма е пусната от папка и не се обновява сама — инсталаторът е в страницата на изданието.",
                           "This copy runs from a folder and does not update itself — the installer is on the release page."));

                card.AddAction(Lang.T("Изтегли от GitHub", "Download from GitHub"), Theme.Glyph.External,
                    ButtonVariant.Primary, () => Browser.Open(release.Page, FindForm()));
                AllowSkip(card, release);
                break;

            case UpdateStage.Downloading:
                card = new NoticeCard(DownloadTitle(state), now, Lang.T(
                    "Може да продължите работа. Когато свърши, остава само рестартът.",
                    "You can keep working. When it finishes, only the restart is left."))
                {
                    Progress = state.Progress
                };
                break;

            case UpdateStage.Ready:
                card = new NoticeCard(
                    Lang.T($"Издание {release.Version} е готово", $"Version {release.Version} is ready"), now,
                    Lang.T("Програмата ще се затвори и ще се отвори наново — записаното остава, както е.",
                           "The program will close and open again — everything saved stays as it is."))
                {
                    Stripe = Theme.Ok
                };

                card.AddAction(Lang.T("Рестартирай и обнови", "Restart and update"), Theme.Glyph.Upgrade,
                    ButtonVariant.Primary, () => RestartRequested?.Invoke(this, EventArgs.Empty));
                AddReleasePage(card, release);
                break;

            case UpdateStage.Failed:
                card = new NoticeCard(
                    Lang.T($"Обновяването до {release.Version} спря", $"The update to {release.Version} stopped"), now,
                    state.Problem ?? string.Empty)
                {
                    Stripe = Theme.Warn,
                    Fill = Theme.WarnSoft,
                    Outline = Theme.WarnSoft
                };

                if (_updates.CanApply)
                    card.AddAction(Lang.T("Опитай пак", "Try again"), Theme.Glyph.Refresh, ButtonVariant.Secondary,
                        () => _ = _updates.DownloadAsync());
                AddReleasePage(card, release);
                break;

            default:
                return null;
        }

        return card;
    }

    private static string DownloadTitle(UpdateState state) => Lang.T(
        $"Сваля се издание {state.Version} — {state.Progress} %",
        $"Downloading version {state.Version} — {state.Progress} %");

    private void AddReleasePage(NoticeCard card, ReleaseInfo release) =>
        card.AddAction(Lang.T("Какво е новото", "What's new"), Theme.Glyph.External, ButtonVariant.Ghost,
            () => Browser.Open(release.Page, FindForm()));

    /// <summary>„Не сега“ за това издание. Настройките продължават да го показват.</summary>
    private void AllowSkip(NoticeCard card, ReleaseInfo release) =>
        card.AllowDismiss(Lang.T("Скрий до следващото издание", "Hide until the next version"), _tips, () =>
        {
            _workspace.Settings.Set(SettingsStore.Keys.UpdateSkipped, release.Version);
            FillNotices();
            NoticesChanged?.Invoke(this, EventArgs.Empty);
        });

    /// <summary>„Още N известия…“ — по-старите са на едно щракване, а не под ръба.</summary>
    private Control MoreNews(int hidden)
    {
        var more = Theme.GhostButton(
            Lang.T($"Още {hidden} " + (hidden == 1 ? "известие…" : "известия…"),
                   $"{hidden} more " + (hidden == 1 ? "notification…" : "notifications…")),
            Theme.Glyph.ChevronDown);

        more.Height = Theme.Scaled(36);
        more.Margin = new Padding(0, 0, 0, Theme.Scaled(6));
        more.Click += (_, _) =>
        {
            _allNews = true;
            FillNotices();
        };

        return more;
    }

    private NoticeCard NewsCard(NewsItem item)
    {
        var meta = item.Published.ToString("dd.MM.yyyy") +
                   (_fresh.Contains(item.Id) ? Lang.T("  ·  ново", "  ·  new") : string.Empty);

        var card = new NoticeCard(item.Title, meta, item.Body)
        {
            Stripe = item.Id.StartsWith(NewsStore.LocalPrefix, StringComparison.Ordinal)
                ? Theme.Ok
                : item.Severity switch
                {
                    NewsSeverity.Critical => Theme.Error,
                    NewsSeverity.Important => Theme.Warn,
                    _ => Theme.Accent
                },
            Fill = item.Severity switch
            {
                NewsSeverity.Critical => Theme.ErrorSoft,
                NewsSeverity.Important => Theme.WarnSoft,
                _ => Theme.SurfaceAlt
            },
            Outline = item.Severity == NewsSeverity.Info ? Theme.BorderSoft : Theme.WarnSoft
        };

        if (item.Link.Length > 0)
            card.AddAction(Lang.T("Прочети още", "Read more"), Theme.Glyph.External, ButtonVariant.Ghost,
                () => Browser.Open(item.Link, FindForm()));

        card.AllowDismiss(Lang.T("Скрий това известие", "Hide this notification"), _tips, () =>
        {
            _workspace.News.Hide(item.Id);
            FillNotices();
            NoticesChanged?.Invoke(this, EventArgs.Empty);
        });

        return card;
    }

    /// <summary>
    /// Празно — с последната проверка, за да личи, че програмата е питала, а не
    /// че е забравила.
    /// </summary>
    private NoticeCard NothingNew()
    {
        var auto = _workspace.Settings.GetBool(SettingsStore.Keys.UpdateAuto, true);
        var state = _updates.State;

        var checkedAt = state.CheckedAt ??
                        (DateTime.TryParse(_workspace.Settings.Get(SettingsStore.Keys.UpdateChecked),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var saved) ? saved : null);

        var meta = checkedAt is { } at
            ? Lang.T($"проверено {at:dd.MM.yyyy, HH:mm}", $"checked {at:dd.MM.yyyy, HH:mm}")
            : string.Empty;

        var body = state.Stage == UpdateStage.Failed && state.Problem is { } problem
            ? problem
            : auto
                ? Lang.T($"Издание {_updates.CurrentVersion}. При всяко пускане програмата пита GitHub за ново издание и новини.",
                         $"Version {_updates.CurrentVersion}. On every start the program asks GitHub for a new version and news.")
                : Lang.T($"Издание {_updates.CurrentVersion}. Проверката при пускане е изключена — „Провери сега“ пита веднага.",
                         $"Version {_updates.CurrentVersion}. The check on start is off — “Check now” asks right away.");

        return new NoticeCard(Lang.T("Няма нови известия", "No new notifications"), meta, body)
        {
            Stripe = Color.Transparent,
            Fill = Theme.SurfaceAlt,
            Outline = Theme.BorderSoft
        };
    }

    // ------------------------------------------------------------- за тестовете

    /// <summary>Картите в известията, отгоре надолу — ползва се от тестовете.</summary>
    internal IReadOnlyList<NoticeCard> Notices => [.. _notices.Controls.OfType<NoticeCard>()];

    /// <summary>Заглавието на известията с броя на новите — ползва се от тестовете.</summary>
    internal string NoticesTitle => _noticesTitle.Text;

    /// <summary>Щраква отчета от „Продължете оттам…“, както човек — ползва се от тестовете.</summary>
    internal void ClickRecent(int index)
    {
        if (index >= 0 && index < _recent.Controls.Count && _recent.Controls[index] is ClickableCard card)
            card.Clicked();
    }

    // ------------------------------------------------------------- общо

    /// <summary>Маха картите и ги изхвърля — иначе всяко пресъздаване оставя надписи в паметта.</summary>
    private static void Clear(Control list)
    {
        var old = list.Controls.Cast<Control>().ToList();

        list.Controls.Clear();

        foreach (var control in old) control.Dispose();
    }

    private Control Empty(string text)
    {
        var card = new Card
        {
            Width = ContentWidth,
            Height = Theme.Scaled(68),
            Fill = Theme.SurfaceAlt,
            Outline = Theme.BorderSoft,
            Padding = new Padding(Theme.Gap, 0, Theme.Gap, 0)
        };

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Theme.InkFaint,
            TextAlign = ContentAlignment.MiddleLeft
        });

        return card;
    }
}
