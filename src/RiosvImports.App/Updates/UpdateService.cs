using RiosvImports.Core.Localization;

namespace RiosvImports.App.Updates;

/// <summary>
/// Проверява за ново издание, сваля го и го слага — по искане на човека.
///
/// Изданията стоят в GitHub (виж <see cref="UpdateFeed"/>). Velopack сваля само
/// разликата спрямо текущото издание, затова обновяването отнема секунди и не
/// иска права на администратор: програмата се инсталира в профила на
/// потребителя.
///
/// Три правила:
/// <list type="bullet">
/// <item><b>Нищо не се слага без човека.</b> Проверката при пускане само казва;
/// свалянето е копче, рестартът е друго копче. Velopack по подразбиране слага
/// всяко свалено издание при следващото пускане — това е изключено в
/// Program.Main, за да не се смени програмата сутринта преди краен срок.</item>
/// <item><b>Нищо не хвърля навън.</b> Без мрежа човекът продължава да работи с
/// това, което има; неуспехът е състояние с обяснение, не грешка.</item>
/// <item><b>Едно състояние за всички екрани.</b> Началният екран и настройките
/// показват едно и също, защото четат едно и също.</item>
/// </list>
/// </summary>
internal sealed class UpdateService
{
    private readonly Func<bool, IUpdateBackend> _make;
    private readonly SynchronizationContext? _ui;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;

    private IUpdateBackend _backend;
    private bool _prerelease;
    private Task? _checking;
    private Task? _downloading;
    private int _lastProgress = -1;

    /// <summary>
    /// <paramref name="make"/> сглобява страната, която върши работата — с или
    /// без пробните издания. Сглобява се наново, когато отметката за тях се смени.
    /// </summary>
    public UpdateService(Func<bool, IUpdateBackend> make, bool prerelease = false)
    {
        _make = make;
        _prerelease = prerelease;
        _backend = make(prerelease);

        // Събитията стигат до екраните на нишката им. Напредъкът на свалянето
        // идва от нишка на Velopack и без това би пипнал контрола отвън.
        _ui = SynchronizationContext.Current;

        State = _backend.Pending is { } pending
            ? new UpdateState(UpdateStage.Ready, pending)
            : new UpdateState(UpdateStage.Idle);
    }

    /// <summary>Вдига се при всяка промяна на <see cref="State"/>, на нишката на екраните.</summary>
    public event EventHandler? Changed;

    public UpdateState State { get; private set; }

    public bool CanApply => _backend.CanApply;

    public InstallKind Kind => _backend.Kind;

    public string CurrentVersion => _backend.CurrentVersion;

    public bool Prerelease => _prerelease;

    public bool Busy => State.Stage is UpdateStage.Checking or UpdateStage.Downloading;

    /// <summary>
    /// Пробни издания или не. Смяната забравя намереното: то е било намерено по
    /// другото правило и може вече да не е за този човек.
    /// </summary>
    public void UsePrerelease(bool on)
    {
        if (on == _prerelease || Busy) return;

        _prerelease = on;
        _backend = _make(on);

        Set(_backend.Pending is { } pending
            ? new UpdateState(UpdateStage.Ready, pending, CheckedAt: State.CheckedAt)
            : new UpdateState(UpdateStage.Idle, CheckedAt: State.CheckedAt));
    }

    /// <summary>Пита за ново издание. Повторно натискане по време на проверка чака същата.</summary>
    public Task CheckAsync()
    {
        if (_checking is { IsCompleted: false } running) return running;
        if (State.Stage == UpdateStage.Downloading) return _downloading ?? Task.CompletedTask;

        return _checking = Check();
    }

    private async Task Check()
    {
        var before = State;
        Set(new UpdateState(UpdateStage.Checking, before.Release, CheckedAt: before.CheckedAt));

        try
        {
            var found = await _backend.CheckAsync(CancellationToken.None);
            var now = DateTime.Now;

            if (found is null)
            {
                Set(new UpdateState(UpdateStage.Current, CheckedAt: now));
                return;
            }

            // Вече сваленото същото издание не се сваля втори път.
            var ready = _backend.Pending is { } pending && pending.Version == found.Version;

            Set(new UpdateState(ready ? UpdateStage.Ready : UpdateStage.Available, found, CheckedAt: now));
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog(ex);

            Set(new UpdateState(UpdateStage.Failed, before.HasNewer ? before.Release : null,
                Problem: UpdateBackends.Explain(ex), CheckedAt: before.CheckedAt));
        }
    }

    /// <summary>
    /// Сваля намереното. Програмата продължава да работи; когато свърши,
    /// остава само рестартът.
    /// </summary>
    public Task DownloadAsync()
    {
        if (_downloading is { IsCompleted: false } running) return running;
        if (!CanApply || State.Release is null) return Task.CompletedTask;
        if (State.Stage is not (UpdateStage.Available or UpdateStage.Failed)) return Task.CompletedTask;

        return _downloading = Download(State.Release);
    }

    private async Task Download(ReleaseInfo release)
    {
        var checkedAt = State.CheckedAt;

        _lastProgress = -1;
        Set(new UpdateState(UpdateStage.Downloading, release, 0, CheckedAt: checkedAt));

        try
        {
            await _backend.DownloadAsync(Progress, CancellationToken.None);

            Set(new UpdateState(UpdateStage.Ready, release, 100, CheckedAt: checkedAt));
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog(ex);

            Set(new UpdateState(UpdateStage.Failed, release,
                Problem: Lang.T("Свалянето не стана: ", "The download failed: ") + UpdateBackends.Explain(ex),
                CheckedAt: checkedAt));
        }
    }

    /// <summary>
    /// Напредъкът на свалянето. Пристига от нишка на Velopack и може да
    /// закъснее: изостанал „99 %“ не бива да върне вече готовото назад.
    /// </summary>
    private void Progress(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        if (percent == _lastProgress) return;
        _lastProgress = percent;

        Post(() =>
        {
            if (State.Stage != UpdateStage.Downloading) return;

            State = State with { Progress = percent };
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Казва на инсталатора да смени файловете, щом програмата излезе. Вика се
    /// СЛЕД затварянето на базата. Връща дали има какво да се сложи.
    /// </summary>
    public bool ApplyOnExit()
    {
        if (!CanApply || State.Stage != UpdateStage.Ready) return false;

        try
        {
            _backend.ApplyOnExit();
            return true;
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog(ex);
            return false;
        }
    }

    /// <summary>
    /// Слага готово състояние наготово — за прегледа на екраните и за тестовете,
    /// които трябва да видят картата на началния екран без мрежа.
    /// </summary>
    internal void Show(UpdateState state) => Set(state);

    private void Set(UpdateState state)
    {
        Post(() =>
        {
            State = state;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// По нишката, а не по самия контекст: Windows Forms може да сложи нов
    /// контекст при Application.Run, а нишката си остава същата.
    /// </summary>
    private void Post(Action change)
    {
        if (_ui is null || Environment.CurrentManagedThreadId == _uiThread)
        {
            change();
            return;
        }

        _ui.Post(_ => change(), null);
    }
}
