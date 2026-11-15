namespace RiosvImports.App;

/// <summary>
/// Прозорче: работи ВЪРХУ програмата, а не до нея.
///
/// Общата основа съществува заради едно оплакване, което не приличаше на
/// техническо: „изглежда, че съм отворил програмата няколко пъти“. Всяко
/// прозорче си слагаше свое копче на лентата на задачите, а в Windows копче на
/// лентата значи ОТДЕЛНА програма. Отвориш предаването, отскочиш до Excel да
/// свериш нещо, върнеш се — и на лентата те чакат две-три еднакви иконки, от
/// които само едната приема щракване. Останалите примигват и не правят нищо,
/// защото прозорчето е модално и програмата зад него не приема.
///
/// Затова тук се пише ВЕДНЪЖ какво е прозорче:
///
/// <list type="bullet">
/// <item>едно копче на лентата — на програмата, не на всеки прозорец;</item>
/// <item>без смаляване — смалено прозорче без свое копче се губи безвъзвратно;</item>
/// <item>отваря се върху този, който го е отворил, а не в ъгъла на екрана.</item>
/// </list>
///
/// <b>Родителят не се скрива.</b> Първата мисъл беше точно това — да изчезва,
/// докато прозорчето стои — но скрит родител е по-лошо от два прозореца: между
/// скриването и показването се вижда работният плот, а ако нещо се спъне по
/// пътя на затварянето, програмата остава пусната и невидима. Прозорчетата и
/// без това се отварят почти на цял екран; отдолу не се вижда нищо, а на
/// лентата вече стои едно име.
///
/// Наследникът може да презапише всичко: базата се изпълнява преди неговия
/// конструктор. Досега нито едно не иска друго — двайсет и шестте прозорчета
/// пишеха същите три реда поотделно, а петте, които се сещаха за лентата, бяха
/// петте, на които някой се беше оплакал.
/// </summary>
internal class Dialog : Form
{
    public Dialog()
    {
        // Копчето на лентата е на ПРОГРАМАТА. Прозорчето се сочи от него,
        // защото се отваря с притежател (ShowDialog(this)) — Windows връща
        // напред целия ред, а най-отгоре стои онова, което чака отговор.
        ShowInTaskbar = false;

        // Без смаляване, и то следва от горното: прозорче без копче на лентата,
        // веднъж смалено, няма откъде да се върне. Цялата програма пак се
        // смалява — с копчето на лентата, което сваля и притежателя, и всичко
        // над него.
        MinimizeBox = false;

        // Върху този, който го е отворил. На два монитора „по средата на
        // екрана“ значи „на другия монитор“ достатъчно често, за да дразни.
        StartPosition = FormStartPosition.CenterParent;
    }

    /// <summary>
    /// Прозорчето никога не е по-голямо от екрана, на който се отваря.
    ///
    /// Размерите по прозорчетата са мерени при 100 % и минават през мащаба. При
    /// 150 % на лаптоп някои излизаха по-високи от работната площ — и долният
    /// ред, точно този с „Запиши“, оставаше под лентата на задачите, без начин
    /// да се стигне. Тук се пише ВЕДНЪЖ за всичките: прозорчето се смалява до
    /// екрана, а най-малкият му размер — също, иначе Windows не би го пуснал.
    /// Каквото не се побира вътре, се пренася или превърта — то е работа на
    /// самото прозорче.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        FitScreen();
        base.OnLoad(e);
    }

    /// <summary>
    /// Работната част на прозорчетата на склада запазва височината си на нисък
    /// екран.
    ///
    /// Трите прозорчета — третиране, образуване, предаване — са устроени еднакво:
    /// заглавие с обяснение, полета на книгата, работна част (кодове, партиди,
    /// дата) и копчета. На лаптоп при 150 % полетата и обяснението изяждаха
    /// височината, а работната част — заради която прозорчето изобщо се отваря —
    /// оставаше с ред-два: при предаването таблицата с кодовете не се виждаше
    /// изобщо.
    ///
    /// Затова при нисък прозорец се отстъпва по ред, от най-малко нужното:
    /// първо обяснението под заглавието (то се чете веднъж), после полетата се
    /// свиват до един ред и превъртат вътре в себе си. Работната част получава
    /// поне <paramref name="workMin"/>; ако и така не стига, тя превърта, вместо
    /// да реже — копчетата долу остават на мястото си. На висок екран нищо не се
    /// мени.
    /// </summary>
    protected void KeepWork(Control title, Control fields, Control work, int workMin)
    {
        var explain = title.Controls.OfType<Label>().FirstOrDefault(l => l.Dock == DockStyle.Bottom);

        var titleFull = title.Height;
        var titleShort = explain is null ? titleFull : titleFull - explain.Height;
        var fieldsFull = fields.Height;
        var fieldsLeast = Math.Min(fieldsFull, Theme.Scaled(84));

        if (work is ScrollableControl scroll)
        {
            scroll.AutoScroll = true;
            scroll.AutoScrollMinSize = new Size(0, workMin);
        }

        void Fit()
        {
            if (ClientSize.Height <= 0) return;

            // Всичко закачено отгоре и отдолу освен заглавието и полетата —
            // копчетата и каквото още има; те не отстъпват.
            var rest = Controls.Cast<Control>()
                .Where(c => c != title && c != fields && c.Visible && c.Dock is DockStyle.Top or DockStyle.Bottom)
                .Sum(c => c.Height);

            var room = ClientSize.Height - rest - workMin;
            var full = room >= titleFull + fieldsFull;

            if (explain is not null) explain.Visible = full;

            title.Height = full ? titleFull : titleShort;
            fields.Height = full ? fieldsFull : Math.Clamp(room - titleShort, fieldsLeast, fieldsFull);
        }

        Resize += (_, _) => Fit();
        Fit();
    }

    private void FitScreen()
    {
        var area = (Owner is { } owner ? Screen.FromControl(owner) : Screen.FromPoint(Cursor.Position)).WorkingArea;

        if (MinimumSize.Width > area.Width || MinimumSize.Height > area.Height)
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));

        if (WindowState != FormWindowState.Normal) return;
        if (Width <= area.Width && Height <= area.Height) return;

        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));

        // Смаленото се слага наново по средата на притежателя — но вътре в
        // екрана: центрирано по стария размер, то би стърчало навън.
        var middle = Owner is { } parent
            ? new Point(parent.Left + (parent.Width - Width) / 2, parent.Top + (parent.Height - Height) / 2)
            : new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);

        StartPosition = FormStartPosition.Manual;
        Location = new Point(
            Math.Clamp(middle.X, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(middle.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    /// <summary>
    /// Най-тясната лява рамка, която още върши работа.
    ///
    /// Вляво стоят пет стълба и последният е онзи, В КОЙТО СЕ ПИШЕ. Скрит зад
    /// воден плъзгач, той прави екрана безполезен — затова делът е желание, а
    /// това число е истина.
    ///
    /// Мерено при мащаб 100 %. На лаптоп с 125 % или 150 % буквите в стълбовете
    /// растат, а числото — не, и минимумът спира да пази това, за което е:
    /// затова минава през <see cref="Theme.Scaled"/>.
    /// </summary>
    private const int ChoiceMin = 600;

    /// <summary>
    /// Дели работното прозорче: една трета за избора, две трети за работата.
    ///
    /// Вляво се ЧЕТЕ — кодовете, по които се избира. Вдясно се работи:
    /// партидите, количествата, датата, кантарът. Дотук всяко прозорче си
    /// смяташе дела само и трите започваха почти по средата, с по-голямата
    /// половина при списъка, който я не искаше.
    ///
    /// <b>Разделителят следва прозореца, докато не бъде дръпнат.</b> Иначе
    /// сложен веднъж при отварянето, той оставаше на мястото за малкия
    /// прозорец: разтвориш на цял екран и цялата спечелена ширина отиваше на
    /// едната страна. Дръпне ли го човек веднъж, оттам нататък е негов.
    ///
    /// Редът на присвояване е капан: зададен пръв, минимумът се прилага върху
    /// контрола, която още е широка сто и петдесет пиксела, и WinForms хвърля
    /// „SplitterDistance must be between…“. Затова минимумите се свалят, слага
    /// се разстоянието и чак тогава се вдигат обратно.
    /// </summary>
    /// <param name="choiceMin">
    /// Колко иска лявата рамка, ако е повече от обичайното. Образуването държи
    /// вляво две таблици една под друга и иска ширината на по-широката — тогава
    /// тя се дава дори на лаптоп, стига вдясно да остане <paramref name="workMin"/>.
    /// </param>
    protected static void Split(SplitContainer split, int workMin, int choiceMin = ChoiceMin)
    {
        var dragged = false;

        // Дърпането с мишка вдига „мърда се“; присвояването отвън — не.
        //
        // Първата версия слушаше „мръдна се“ и се самоизключваше още при
        // първото подреждане: WinForms мести разделителя сам, докато нарежда
        // прозореца, и прозорчето решаваше, че човекът вече го е дръпнал.
        split.SplitterMoving += (_, _) => dragged = true;

        split.SizeChanged += (_, _) =>
        {
            if (!dragged) Apply();
        };

        Apply();

        void Apply()
        {
            var room = split.Width - split.SplitterWidth;

            // Докато прозорчето не е подредено, разделителят е широк колкото
            // WinForms го е направил — стотина пиксела. Дял от него не значи
            // нищо; истинската ширина идва със SizeChanged.
            if (room < Theme.Scaled(320)) return;

            // При тесен прозорец минимумите не се побират. Тогава се свиват до
            // половината всеки — по-добре стеснена таблица, отколкото никакъв
            // минимум и разделител, оставен където WinForms го е заварил.
            var choice = choiceMin == ChoiceMin
                ? Math.Min(Theme.Scaled(ChoiceMin), room / 2)
                : Math.Min(Theme.Scaled(choiceMin), Math.Max(room / 2, room - Theme.Scaled(workMin)));
            var work = Math.Min(Theme.Scaled(workMin), room - choice);

            split.Panel1MinSize = 1;
            split.Panel2MinSize = 1;

            split.SplitterDistance = Math.Clamp(
                (int)(split.Width * Theme.WorkSplit), choice, Math.Max(choice, room - work));

            split.Panel1MinSize = choice;
            split.Panel2MinSize = work;
        }
    }
}
