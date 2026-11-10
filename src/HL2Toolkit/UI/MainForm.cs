using System.Drawing.Imaging;
using HL2Toolkit.Build;
using HL2Toolkit.Render;
using HL2Toolkit.Source;

namespace HL2Toolkit.UI;

public sealed class MainForm : Form
{
    readonly EditorState _ed = new();
    readonly Assets _assets = new();
    GameInfo _game;
    readonly Viewport _vp;

    // библиотека
    readonly ImageList _icons = new() { ImageSize = new Size(80, 80), ColorDepth = ColorDepth.Depth32Bit };
    readonly ListView _models = MakeList(), _entities = MakeList(), _blocks = MakeList(), _mine = MakeList();
    readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Поиск модели… (например: crate, barrel, chair)" };
    readonly ComboBox _folder = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    List<(string path, string folder, string name)> _allModels = new();
    readonly HashSet<string> _thumbRequested = new();
    readonly System.Windows.Forms.Timer _thumbTimer = new() { Interval = 250 };

    // правая колонка
    readonly PropertyGrid _props = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.Categorized };
    readonly TreeView _outliner = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowLines = false };
    readonly Panel _envPanel = new() { Dock = DockStyle.Fill, AutoScroll = true };

    // низ
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5f), WordWrap = false };
    readonly ProgressBar _progress = new() { Dock = DockStyle.Right, Width = 260, Style = ProgressBarStyle.Continuous };
    readonly Label _stageLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel _gameStatus = new();

    // тулбар
    readonly Dictionary<Tool, ToolStripButton> _toolButtons = new();
    ToolStripButton _rtxBtn, _snapBtn, _finalBtn;
    ToolStripButton _playBtn, _buildBtn;
    CancellationTokenSource _buildCts;

    const string Hint = "ПКМ + WASD/QE — полёт  •  ЛКМ — выбрать и тащить по земле  •  W/E/R — двигать/вращать/масштаб  •  F — к объекту  •  End — на землю  •  Ctrl+D — копия  •  Del — удалить";

    public MainForm()
    {
        Text = "HL2 Toolkit — редактор карт Half-Life 2";
        Size = new Size(1600, 950);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.UiFont;
        BackColor = Theme.Back; ForeColor = Theme.Text;
        KeyPreview = true;
        AllowDrop = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        _vp = new Viewport(_ed, _assets) { Dock = DockStyle.Fill };
        _vp.Status += s => _status.Text = s;
        _vp.FilesDropped += f => ImportFile(f);

        BuildLayout();
        Theme.Apply(this);
        _log.BackColor = Theme.Field; _log.ForeColor = Color.FromArgb(190, 200, 190);

        _ed.SceneChanged += RefreshOutliner;
        _ed.SelectionChanged += () => { ShowProps(); SyncOutlinerSelection(); };
        _ed.ToolChanged += SyncTools;
        _thumbTimer.Tick += (_, _) => RequestVisibleThumbs();
        _thumbTimer.Start();
        DragEnter += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] fs) foreach (var f in fs) ImportFile(f); };
        Shown += (_, _) => InitGame();
        FormClosing += OnClosing;
    }

    static ListView MakeList()
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false, BorderStyle = BorderStyle.None,
            ShowItemToolTips = true, HideSelection = false,
        };
        lv.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(lv, true);
        return lv;
    }

    // ================================================================== компоновка
    void BuildLayout()
    {
        var menu = BuildMenu();
        var tools = BuildToolbar();
        var statusBar = new StatusStrip { Renderer = new Theme.Renderer(), BackColor = Theme.Back };
        statusBar.Items.Add(_status); statusBar.Items.Add(_gameStatus);
        _status.Text = Hint; _status.ForeColor = Theme.Dim; _gameStatus.ForeColor = Theme.Dim;

        // левая колонка: библиотека
        var lib = new Theme.Tabs { Dock = DockStyle.Fill };
        var modelsPage = new Panel();
        modelsPage.Controls.Add(_models); modelsPage.Controls.Add(_folder); modelsPage.Controls.Add(_search);
        lib.Add("Модели", modelsPage);
        lib.Add("Сущности", _entities);
        lib.Add("Блоки", _blocks);
        var minePage = new Panel();
        var importBtn = new Button { Text = "＋ Импорт своей модели (OBJ)…", Dock = DockStyle.Top, Height = 34, Font = Theme.Bold };
        importBtn.Click += (_, _) => ImportFile(null);
        var mineHint = new Label { Text = "Перетащите .obj прямо в окно редактора — откроется мастер импорта. Модель скомпилируется через studiomdl и появится здесь.", Dock = DockStyle.Top, Height = 48, ForeColor = Theme.Dim, Padding = new Padding(4) };
        minePage.Controls.Add(_mine); minePage.Controls.Add(mineHint); minePage.Controls.Add(importBtn);
        lib.Add("Мои модели", minePage);
        foreach (var lv in new[] { _models, _entities, _blocks, _mine })
        {
            lv.LargeImageList = _icons;
            lv.ItemDrag += LibraryItemDrag;
            lv.ItemActivate += (_, _) => PlaceInFront(lv);
        }
        _search.TextChanged += (_, _) => FilterModels();
        _folder.SelectedIndexChanged += (_, _) => FilterModels();
        var libHeader = Theme.Caption("Библиотека — перетащите в мир");
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(lib); left.Controls.Add(libHeader);

        // правая колонка
        var rightTabs = new Theme.Tabs { Dock = DockStyle.Fill };
        rightTabs.Add("Свойства", _props);
        rightTabs.Add("Мир и погода", _envPanel);
        BuildEnvPanel();
        var outl = new Panel { Dock = DockStyle.Fill };
        outl.Controls.Add(_outliner); outl.Controls.Add(Theme.Caption("Объекты на карте"));
        _outliner.AfterSelect += (_, e) => { if (e.Node?.Tag is SceneObject o && _ed.Selected != o) _ed.Select(o); };
        _outliner.NodeMouseDoubleClick += (_, _) => _vp.FocusSelected();
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 4 };
        rightSplit.Panel1.Controls.Add(rightTabs);
        rightSplit.Panel2.Controls.Add(outl);

        // центр: вьюпорт + лог
        var bottom = new Panel { Dock = DockStyle.Fill };
        var progRow = new Panel { Dock = DockStyle.Top, Height = 22 };
        progRow.Controls.Add(_stageLabel); progRow.Controls.Add(_progress);
        _stageLabel.Text = "  Лог сборки"; _stageLabel.Font = Theme.Bold; _stageLabel.ForeColor = Theme.Accent;
        bottom.Controls.Add(_log); bottom.Controls.Add(progRow);
        var centerSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 4, FixedPanel = FixedPanel.Panel2 };
        centerSplit.Panel1.Controls.Add(_vp);
        centerSplit.Panel2.Controls.Add(bottom);

        var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 4, FixedPanel = FixedPanel.Panel1 };
        var innerSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 4, FixedPanel = FixedPanel.Panel2 };
        mainSplit.Panel1.Controls.Add(left);
        mainSplit.Panel2.Controls.Add(innerSplit);
        innerSplit.Panel1.Controls.Add(centerSplit);
        innerSplit.Panel2.Controls.Add(rightSplit);

        Controls.Add(mainSplit);
        Controls.Add(tools);
        Controls.Add(menu);
        Controls.Add(statusBar);
        MainMenuStrip = menu;

        Load += (_, _) =>
        {
            mainSplit.SplitterDistance = 330;
            innerSplit.SplitterDistance = Math.Max(300, innerSplit.Width - 340);
            centerSplit.SplitterDistance = Math.Max(200, centerSplit.Height - 150);
            rightSplit.SplitterDistance = (int)(rightSplit.Height * 0.62);
        };
        foreach (var sc in new[] { mainSplit, innerSplit, centerSplit, rightSplit }) sc.BackColor = Theme.Border;
    }

    MenuStrip BuildMenu()
    {
        var m = new MenuStrip { Renderer = new Theme.Renderer(), BackColor = Theme.Back, ForeColor = Theme.Text };
        ToolStripMenuItem Item(string t, Keys k, Action a) { var i = new ToolStripMenuItem(t, null, (_, _) => a()) { ShortcutKeys = k, ForeColor = Theme.Text }; return i; }
        var file = new ToolStripMenuItem("Файл") { ForeColor = Theme.Text };
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Новая карта", Keys.Control | Keys.N, NewMap),
            Item("Открыть проект…", Keys.Control | Keys.O, OpenProject),
            Item("Сохранить", Keys.Control | Keys.S, () => SaveProject(false)),
            Item("Сохранить как…", Keys.Control | Keys.Shift | Keys.S, () => SaveProject(true)),
            new ToolStripSeparator(),
            Item("Экспорт VMF (для Hammer)…", Keys.None, ExportVmf),
            new ToolStripSeparator(),
            Item("Выход", Keys.Alt | Keys.F4, Close),
        });
        var edit = new ToolStripMenuItem("Правка") { ForeColor = Theme.Text };
        edit.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Отменить", Keys.Control | Keys.Z, () => _ed.Undo()),
            Item("Повторить", Keys.Control | Keys.Y, () => _ed.Redo()),
            new ToolStripSeparator(),
            Item("Дублировать", Keys.Control | Keys.D, () => _ed.DuplicateSelected()),
            Item("Удалить", Keys.None, () => _ed.DeleteSelected()),
            Item("Поставить на землю", Keys.None, () => _vp.DropToGround()),
        });
        var view = new ToolStripMenuItem("Вид") { ForeColor = Theme.Text };
        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Камеру к выделенному (F)", Keys.None, () => _vp.FocusSelected()),
            Item("Камеру к центру карты (Home)", Keys.None, () => _vp.LookAtCenter()),
        });
        var build = new ToolStripMenuItem("Сборка") { ForeColor = Theme.Text };
        build.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Собрать и играть", Keys.F5, () => StartBuild(true)),
            Item("Только собрать", Keys.F7, () => StartBuild(false)),
            Item("Запустить последнюю карту", Keys.F6, () => Toolchain.Launch(_ed.Scene.Name, _rtxBtn.Checked, Log)),
            Item("Остановить сборку", Keys.None, () => _buildCts?.Cancel()),
            new ToolStripSeparator(),
            Item("Открыть папку work", Keys.None, () => OpenFolder(Toolchain.WorkDir)),
        });
        var settings = new ToolStripMenuItem("Настройки") { ForeColor = Theme.Text };
        settings.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Папка Half-Life 2…", Keys.None, () => PickGameFolder(false)),
            Item("Папка Half-Life 2 RTX…", Keys.None, () => PickGameFolder(true)),
        });
        var help = new ToolStripMenuItem("Справка") { ForeColor = Theme.Text };
        help.DropDownItems.Add(Item("Управление и советы", Keys.F1, ShowHelp));
        help.DropDownItems.Add(Item("Про RTX", Keys.None, ShowRtxHelp));
        m.Items.AddRange(new ToolStripItem[] { file, edit, view, build, settings, help });
        return m;
    }

    ToolStrip BuildToolbar()
    {
        var ts = new ToolStrip { Renderer = new Theme.Renderer(), GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 3, 4, 3), ImageScalingSize = new Size(1, 1), BackColor = Theme.Panel };
        ToolStripButton Btn(string text, string tip, Action a, bool check = false)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, ForeColor = Theme.Text, CheckOnClick = check, Margin = new Padding(1, 0, 1, 0), Padding = new Padding(4, 2, 4, 2) };
            b.Click += (_, _) => a();
            ts.Items.Add(b);
            return b;
        }
        void ToolBtn(Tool t, string text, string tip) => _toolButtons[t] = Btn(text, tip, () => _ed.SetTool(t));
        Btn("Новая", "Новая карта (Ctrl+N)", NewMap);
        Btn("Открыть", "Открыть проект (Ctrl+O)", OpenProject);
        Btn("Сохранить", "Сохранить проект (Ctrl+S)", () => SaveProject(false));
        ts.Items.Add(new ToolStripSeparator());
        Btn("↶", "Отменить (Ctrl+Z)", () => _ed.Undo());
        Btn("↷", "Повторить (Ctrl+Y)", () => _ed.Redo());
        ts.Items.Add(new ToolStripSeparator());
        ToolBtn(Tool.Select, "Выбор", "Выбор объектов (Q)");
        ToolBtn(Tool.Move, "Двигать", "Перемещение (W): тащите объект по земле или за стрелки X/Y/Z");
        ToolBtn(Tool.Rotate, "Вращать", "Вращение (E): тяните за синее кольцо");
        ToolBtn(Tool.Scale, "Масштаб", "Масштаб (R): размеры блоков или масштаб модели");
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripLabel("Рельеф:") { ForeColor = Theme.Dim });
        ToolBtn(Tool.TerrainRaise, "Поднять", "Кисть: поднять землю (Ctrl — опустить)");
        ToolBtn(Tool.TerrainLower, "Опустить", "Кисть: опустить землю (Ctrl — поднять)");
        ToolBtn(Tool.TerrainSmooth, "Сгладить", "Кисть: сгладить неровности");
        ToolBtn(Tool.TerrainFlatten, "Выровнять", "Кисть: выровнять до высоты в точке клика");
        ToolBtn(Tool.TerrainPaint, "Краска", "Кисть: рисовать второй текстурой рельефа (Ctrl — стереть)");
        ts.Items.Add(new ToolStripLabel("Радиус") { ForeColor = Theme.Dim });
        var radius = new TrackBar { Minimum = 64, Maximum = 2048, Value = (int)_ed.BrushRadius, TickStyle = TickStyle.None, Width = 110, BackColor = Theme.Panel };
        radius.ValueChanged += (_, _) => _ed.BrushRadius = radius.Value;
        ts.Items.Add(new ToolStripControlHost(radius) { AutoSize = false, Width = 110 });
        ts.Items.Add(new ToolStripLabel("Сила") { ForeColor = Theme.Dim });
        var strength = new TrackBar { Minimum = 5, Maximum = 100, Value = (int)(_ed.BrushStrength * 100), TickStyle = TickStyle.None, Width = 90, BackColor = Theme.Panel };
        strength.ValueChanged += (_, _) => _ed.BrushStrength = strength.Value / 100f;
        ts.Items.Add(new ToolStripControlHost(strength) { AutoSize = false, Width = 90 });
        ts.Items.Add(new ToolStripSeparator());
        _snapBtn = Btn("Сетка 16", "Привязка к сетке 16 юнитов и к 15° при вращении", () => _ed.Snap = _snapBtn.Checked, true);
        _snapBtn.Checked = true;

        // справа — сборка
        var spacer = new ToolStripLabel("") { Alignment = ToolStripItemAlignment.Right };
        _playBtn = new ToolStripButton("▶  Собрать и играть") { Alignment = ToolStripItemAlignment.Right, ForeColor = Color.White, BackColor = Theme.AccentDark, Font = Theme.Bold, ToolTipText = "F5: компилирует карту (VBSP → VVIS → VRAD) и запускает Half-Life 2", Padding = new Padding(8, 2, 8, 2) };
        _playBtn.Click += (_, _) => StartBuild(true);
        _buildBtn = new ToolStripButton("Собрать") { Alignment = ToolStripItemAlignment.Right, ForeColor = Theme.Text, ToolTipText = "F7: только скомпилировать карту" };
        _buildBtn.Click += (_, _) => StartBuild(false);
        _rtxBtn = new ToolStripButton("RTX") { Alignment = ToolStripItemAlignment.Right, CheckOnClick = true, ForeColor = Color.FromArgb(118, 185, 0), Font = Theme.Bold, ToolTipText = "Режим RTX: карта копируется в Half-Life 2 RTX и запускается с трассировкой лучей (RTX Remix сам компилирует шейдеры при запуске)" };
        _rtxBtn.CheckedChanged += (_, _) => { if (_rtxBtn.Checked && _game != null && !_game.RtxAvailable) { ShowRtxHelp(); } };
        _finalBtn = new ToolStripButton("Финальный свет") { Alignment = ToolStripItemAlignment.Right, CheckOnClick = true, ForeColor = Theme.Text, ToolTipText = "Качественное освещение (-final): дольше, но красивее. Без галочки — быстрая сборка для проверки." };
        ts.Items.Add(_playBtn); ts.Items.Add(_buildBtn); ts.Items.Add(_rtxBtn); ts.Items.Add(_finalBtn); ts.Items.Add(spacer);
        SyncTools();
        return ts;
    }

    void SyncTools()
    {
        foreach (var kv in _toolButtons) kv.Value.Checked = kv.Key == _ed.Tool;
        _status.Text = _ed.IsTerrainTool
            ? "Кисть рельефа: зажмите ЛКМ и водите по земле  •  Ctrl — обратное действие  •  радиус и сила — на панели  •  ПКМ + WASD — полёт"
            : Hint;
    }

    // ================================================================== панель «Мир и погода»
    void BuildEnvPanel()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(6) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Dim, Height = 28 });
            c.Dock = DockStyle.Fill; c.Margin = new Padding(2, 3, 2, 3);
            t.Controls.Add(c);
        }
        void Section(string s)
        {
            var l = new Label { Text = s, Font = Theme.Header, ForeColor = Theme.Accent, AutoSize = false, Height = 28, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };
            t.Controls.Add(l); t.SetColumnSpan(l, 2);
        }

        Section("Карта");
        var name = new TextBox { Text = _ed.Scene.Name };
        name.TextChanged += (_, _) => _ed.Scene.Name = name.Text;
        Row("Имя карты", name);
        var weapons = new CheckBox { Text = "Выдать игроку костюм и оружие", Checked = true };
        weapons.CheckedChanged += (_, _) => _ed.Scene.GiveWeapons = weapons.Checked;
        Row("", weapons);

        Section("Время суток и небо");
        var timeLbl = new Label { ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft };
        var time = new TrackBar { Minimum = 0, Maximum = 48, TickStyle = TickStyle.None, BackColor = Theme.Back };
        time.ValueChanged += (_, _) => { _ed.Scene.Env.TimeOfDay = time.Value / 2f; timeLbl.Text = $"{(int)_ed.Scene.Env.TimeOfDay:00}:{(time.Value % 2 == 1 ? "30" : "00")}"; _ed.Dirty = true; };
        Row("Время", time); Row("", timeLbl);
        var yaw = new TrackBar { Minimum = 0, Maximum = 359, TickStyle = TickStyle.None, BackColor = Theme.Back };
        yaw.ValueChanged += (_, _) => { _ed.Scene.Env.SunYaw = yaw.Value; _ed.Dirty = true; };
        Row("Сторона солнца", yaw);
        var sky = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        sky.Items.AddRange(Catalog.Skies);
        sky.SelectedIndexChanged += (_, _) => { _ed.Scene.Env.Sky = (string)sky.SelectedItem; _ed.Dirty = true; };
        Row("Небо (skybox)", sky);

        Section("Туман");
        var fog = new CheckBox { Text = "Включить туман" };
        fog.CheckedChanged += (_, _) => { _ed.Scene.Env.Fog = fog.Checked; _ed.Dirty = true; };
        Row("", fog);
        var fogColor = new Button { Text = "Цвет тумана…" };
        fogColor.Click += (_, _) =>
        {
            var c = _ed.Scene.Env.FogColor;
            using var dlg = new ColorDialog { Color = Color.FromArgb(c[0], c[1], c[2]), FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) { _ed.Scene.Env.FogColor = new int[] { dlg.Color.R, dlg.Color.G, dlg.Color.B }; _ed.Dirty = true; }
        };
        Row("", fogColor);
        var fogStart = new NumericUpDown { Minimum = 0, Maximum = 30000, Increment = 128 };
        fogStart.ValueChanged += (_, _) => _ed.Scene.Env.FogStart = (float)fogStart.Value;
        Row("Начало", fogStart);
        var fogEnd = new NumericUpDown { Minimum = 64, Maximum = 60000, Increment = 256 };
        fogEnd.ValueChanged += (_, _) => _ed.Scene.Env.FogEnd = (float)fogEnd.Value;
        Row("Конец", fogEnd);

        Section("Вода");
        var water = new CheckBox { Text = "Вода по всей карте" };
        water.CheckedChanged += (_, _) => { _ed.Scene.Env.Water = water.Checked; _ed.Dirty = true; };
        Row("", water);
        var wz = new NumericUpDown { Minimum = -500, Maximum = 4000, Increment = 16 };
        wz.ValueChanged += (_, _) => _ed.Scene.Env.WaterZ = (float)wz.Value;
        Row("Уровень", wz);
        var wmat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        wmat.Items.AddRange(Catalog.Waters);
        wmat.SelectedIndexChanged += (_, _) => _ed.Scene.Env.WaterMaterial = (string)wmat.SelectedItem;
        Row("Материал", wmat);

        Section("Рельеф");
        var tmat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Name = "terrainMaterial" };
        tmat.SelectedIndexChanged += (_, _) => { if (tmat.SelectedItem is string s) { _ed.Scene.Terrain.Material = s; _ed.Dirty = true; } };
        Row("Текстуры", tmat);
        var size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        size.Items.AddRange(new object[] { "2048 (маленькая)", "4096 (средняя)", "8192 (большая)" });
        Row("Размер", size);
        var reset = new Button { Text = "Применить размер (сбросит рельеф)" };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Рельеф будет сброшен в ровную землю. Продолжить?", "HL2 Toolkit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _ed.PushUndo();
            _ed.Scene.Terrain.Reset(size.SelectedIndex switch { 0 => 2048, 2 => 8192, _ => 4096 });
            _ed.NotifyTerrain(true);
        };
        Row("", reset);
        var noise = new Button { Text = "Сгенерировать холмы" };
        noise.Click += (_, _) => { _ed.PushUndo(); GenerateHills(); _ed.NotifyTerrain(false); };
        Row("", noise);

        _envPanel.Controls.Add(t);
        // синхронизация с текущей сценой
        void Sync()
        {
            var e = _ed.Scene.Env;
            name.Text = _ed.Scene.Name; weapons.Checked = _ed.Scene.GiveWeapons;
            time.Value = Math.Clamp((int)(e.TimeOfDay * 2), 0, 48); yaw.Value = Math.Clamp((int)e.SunYaw, 0, 359);
            sky.SelectedItem = Catalog.Skies.Contains(e.Sky) ? e.Sky : Catalog.Skies[0];
            fog.Checked = e.Fog; fogStart.Value = (decimal)Math.Clamp(e.FogStart, 0, 30000); fogEnd.Value = (decimal)Math.Clamp(e.FogEnd, 64, 60000);
            water.Checked = e.Water; wz.Value = (decimal)Math.Clamp(e.WaterZ, -500, 4000);
            wmat.SelectedItem = Catalog.Waters.Contains(e.WaterMaterial) ? e.WaterMaterial : Catalog.Waters[0];
            if (!tmat.Items.Contains(_ed.Scene.Terrain.Material)) tmat.Items.Add(_ed.Scene.Terrain.Material);
            tmat.SelectedItem = _ed.Scene.Terrain.Material;
            size.SelectedIndex = _ed.Scene.Terrain.Size <= 2048 ? 0 : _ed.Scene.Terrain.Size >= 8192 ? 2 : 1;
        }
        _syncEnv = Sync;
        _terrainMaterials = tmat;
        Sync();
    }
    Action _syncEnv;
    ComboBox _terrainMaterials;

    void GenerateHills()
    {
        var t = _ed.Scene.Terrain; var rnd = new Random();
        int r = t.Res;
        var hills = Enumerable.Range(0, 7).Select(_ => (x: rnd.NextSingle() * t.Size - t.Half, y: rnd.NextSingle() * t.Size - t.Half,
            rad: 300 + rnd.NextSingle() * t.Size * 0.18f, h: (rnd.NextSingle() - 0.25f) * 450)).ToList();
        for (int gy = 0; gy < r; gy++)
            for (int gx = 0; gx < r; gx++)
            {
                float x = -t.Half + gx * t.Step, y = -t.Half + gy * t.Step, h = 0;
                foreach (var hl in hills) { float d2 = ((x - hl.x) * (x - hl.x) + (y - hl.y) * (y - hl.y)) / (hl.rad * hl.rad); h += hl.h * MathF.Exp(-d2 * 2); }
                // ровная площадка в центре — там стартует игрок
                float c = MathF.Min(1, MathF.Sqrt(x * x + y * y) / 600);
                t.Heights[gy * r + gx] = h * c;
                t.Alpha[gy * r + gx] = Math.Clamp(Math.Abs(h * c) * 0.8f, 0, 255);
            }
    }

    // ================================================================== игра и библиотека
    void InitGame()
    {
        _game = Toolchain.Detect();
        if (!_game.Ok)
        {
            MessageBox.Show(this, "Half-Life 2 не найдена автоматически. Укажите папку игры (где лежит hl2.exe).", "HL2 Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            PickGameFolder(false);
            return;
        }
        _assets.SetGame(_game.GameDir);
        _gameStatus.Text = "HL2: " + _game.Hl2Root + (_game.RtxAvailable ? "   •   RTX: найдено" : "   •   RTX: не установлено");
        Log("Half-Life 2: " + _game.Hl2Root);
        Log(_game.RtxAvailable ? "RTX: " + (_game.RtxExe ?? "RTX Remix в папке HL2") : "RTX не найден (Half-Life 2 RTX или RTX Remix). Можно собирать обычные карты.");
        _ed.Scene.Terrain.EnsureValid();
        GenerateHills();
        _ed.NotifyTerrain(true);
        _vp.LookAtCenter();
        _outlinerSig = null;
        RefreshOutliner();
        LoadLibrary();
    }

    void LoadLibrary()
    {
        _models.Items.Clear(); _entities.Items.Clear(); _blocks.Items.Clear(); _mine.Items.Clear();
        _icons.Images.Clear(); _thumbRequested.Clear();
        _icons.Images.Add("_none", MakeIcon("?", Theme.Panel));
        var fs = _assets.FS;
        if (fs == null) return;
        Task.Run(() =>
        {
            var all = fs.List("models/", ".mdl");
            var list = new List<(string, string, string)>();
            var skip = new System.Text.RegularExpressions.Regex(@"(gib|chunk|shard|_anim|_gesture|_posture|_shared|_ragdoll|/humans/group\d+/|_lod\d)");
            foreach (var p in all)
            {
                if (skip.IsMatch(p)) continue;
                string rest = p["models/".Length..];
                string folder = rest.Contains('/') ? rest[..rest.LastIndexOf('/')] : "(корень)";
                bool ok = folder.StartsWith("props") || folder.StartsWith("items") || folder.StartsWith("hl2toolkit") || folder.StartsWith("vehicle") ||
                          (folder.StartsWith("weapons") && rest.Contains("/w_"));
                if (ok) list.Add((p, folder, rest[(rest.LastIndexOf('/') + 1)..^4]));
            }
            var terrMats = fs.List("materials/nature/", ".vmt").Where(x => x.Contains("/blend")).Select(x => x["materials/".Length..^4]).ToList();
            terrMats.AddRange(fs.List("materials/", ".vmt").Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^materials/(nature|concrete|stone)/blend")).Select(x => x["materials/".Length..^4]));
            BeginInvoke(() =>
            {
                _allModels = list;
                _folder.Items.Clear();
                _folder.Items.Add("Все папки");
                foreach (var f in list.Select(x => x.Item2).Distinct().OrderBy(x => x)) _folder.Items.Add(f);
                _folder.SelectedIndex = Math.Max(0, _folder.Items.IndexOf("props_junk"));
                FillMine();
                _terrainMaterials.Items.Clear();
                foreach (var m in terrMats.Distinct().OrderBy(x => x)) _terrainMaterials.Items.Add(m);
                _syncEnv();
                Log($"Библиотека: {list.Count} моделей, {terrMats.Count} материалов рельефа.");
            });
        });

        // сущности
        foreach (var def in Catalog.Entities)
        {
            var g = _entities.Groups.Cast<ListViewGroup>().FirstOrDefault(x => x.Header == def.Category) ?? _entities.Groups.Add(def.Category, def.Category);
            var key = string.IsNullOrEmpty(def.Model) ? "_ent_" + def.Cls : def.Model;
            if (string.IsNullOrEmpty(def.Model) && !_icons.Images.ContainsKey(key))
                _icons.Images.Add(key, MakeIcon(def.Kind == ObjKind.Light ? "☀" : "▲", def.Kind == ObjKind.Light ? Color.FromArgb(120, 100, 30) : Color.FromArgb(40, 110, 50)));
            _entities.Items.Add(new ListViewItem(def.Title, key, g) { Tag = def, ToolTipText = def.Cls });
        }
        // блоки
        foreach (var mat in Catalog.BlockMaterials)
        {
            if (!fs.Exists("materials/" + mat + ".vmt")) continue;
            string key = "_mat_" + mat;
            _blocks.Items.Add(new ListViewItem(mat[(mat.LastIndexOf('/') + 1)..], "_none") { Tag = mat, ToolTipText = "Блок (стена, пол, здание): " + mat, ImageKey = "_none", Name = key });
            string m = mat;
            Task.Run(() =>
            {
                var vmt = fs.ReadVmt(m);
                var tex = vmt?.GetValueOrDefault("$basetexture", m) ?? m;
                var img = Vtf.ReadPreview(fs.Read("materials/" + GameFS.Norm(tex) + ".vtf"), 128);
                if (img == null) return;
                var bmp = RgbaToBitmap(img);
                BeginInvoke(() =>
                {
                    _icons.Images.Add(key, new Bitmap(bmp, 80, 80));
                    foreach (ListViewItem it in _blocks.Items) if (it.Name == key) it.ImageKey = key;
                });
            });
        }
    }

    void FillMine()
    {
        _mine.Items.Clear();
        foreach (var m in _allModels.Where(x => x.folder.StartsWith("hl2toolkit")))
            _mine.Items.Add(new ListViewItem(m.name, "_none") { Tag = m.path, ToolTipText = m.path, Name = m.path });
    }

    void FilterModels()
    {
        string q = _search.Text.Trim().ToLowerInvariant();
        string folder = _folder.SelectedIndex > 0 ? (string)_folder.SelectedItem : null;
        _models.BeginUpdate();
        _models.Items.Clear();
        var items = _allModels.Where(m => (folder == null || m.folder == folder || q.Length > 0) && (q.Length == 0 || m.path.Contains(q))).Take(600);
        foreach (var m in items)
            _models.Items.Add(new ListViewItem(m.name, _icons.Images.ContainsKey(m.path) ? m.path : "_none") { Tag = m.path, ToolTipText = m.path, Name = m.path });
        _models.EndUpdate();
    }

    void RequestVisibleThumbs()
    {
        foreach (var lv in new[] { _models, _entities, _mine })
        {
            if (!lv.Visible || lv.Items.Count == 0) continue;
            var rc = lv.ClientRectangle;
            int budget = 12;
            foreach (ListViewItem it in lv.Items)
            {
                string model = it.Tag as string ?? (it.Tag as EntityDef)?.Model;
                if (string.IsNullOrEmpty(model)) continue;
                if (_icons.Images.ContainsKey(model)) { if (it.ImageKey != model) it.ImageKey = model; continue; }
                if (_thumbRequested.Contains(model)) continue;
                if (!it.Bounds.IntersectsWith(rc)) continue;
                _thumbRequested.Add(model);
                string mm = model;
                _vp.RequestThumbnail(mm, bmp =>
                {
                    _icons.Images.Add(mm, new Bitmap(bmp, 80, 80));
                    foreach (var l in new[] { _models, _entities, _mine })
                        foreach (ListViewItem x in l.Items)
                            if ((x.Tag as string ?? (x.Tag as EntityDef)?.Model) == mm) x.ImageKey = mm;
                });
                if (--budget == 0) break;
            }
        }
    }

    static Bitmap RgbaToBitmap(RgbaImage img)
    {
        var bmp = new Bitmap(img.W, img.H, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, img.W, img.H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new byte[img.W * 4];
        for (int y = 0; y < img.H; y++)
        {
            for (int x = 0; x < img.W; x++)
            {
                int s = (y * img.W + x) * 4;
                row[x * 4] = img.Pixels[s + 2]; row[x * 4 + 1] = img.Pixels[s + 1]; row[x * 4 + 2] = img.Pixels[s]; row[x * 4 + 3] = 255;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, row.Length);
        }
        bmp.UnlockBits(bd);
        return bmp;
    }

    static Bitmap MakeIcon(string sym, Color bg)
    {
        var b = new Bitmap(80, 80);
        using var g = Graphics.FromImage(b);
        g.Clear(bg);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new Font("Segoe UI Symbol", 26);
        var sz = g.MeasureString(sym, f);
        g.DrawString(sym, f, Brushes.WhiteSmoke, (80 - sz.Width) / 2, (80 - sz.Height) / 2);
        return b;
    }

    /// <summary>Создаёт объект сцены из элемента библиотеки.</summary>
    SceneObject TemplateFrom(ListViewItem it)
    {
        switch (it.Tag)
        {
            case EntityDef d:
                var o = new SceneObject { Kind = d.Kind, Cls = d.Cls, Model = d.Model, Name = d.Title, Keys = new Dictionary<string, string>(d.Keys) };
                if (d.Kind == ObjKind.Light) { o.Brightness = d.Cls == "light_spot" ? 600 : 300; o.Ang = new float[] { d.Cls == "light_spot" ? -90 : 0, 0, 0 }; }
                if (!string.IsNullOrEmpty(d.Model) && d.Kind == ObjKind.Entity && !d.Keys.ContainsKey("model") && (d.Cls.StartsWith("npc_") || d.Cls.StartsWith("prop_vehicle")))
                    if (d.Cls.StartsWith("prop_vehicle")) o.Keys["model"] = d.Model;
                return o;
            case string s when it.ListView == _blocks:
                return new SceneObject { Kind = ObjKind.Block, Material = s, Name = "Блок", Size = new float[] { 256, 256, 128 } };
            case string model:
                var mesh = _assets.GetMesh(model);
                string cls = mesh?.SuggestedClass ?? "prop_static";
                if (cls == "prop_dynamic" && mesh != null && !mesh.HasPhysics) cls = "prop_dynamic";
                return new SceneObject { Kind = ObjKind.Prop, Model = model, Cls = cls, Name = it.Text };
        }
        return null;
    }

    void LibraryItemDrag(object sender, ItemDragEventArgs e)
    {
        if (e.Item is not ListViewItem it) return;
        var tpl = TemplateFrom(it);
        if (tpl == null) return;
        if (tpl.Kind == ObjKind.Prop) _assets.GetMesh(tpl.Model);
        DragPayload.Current = tpl;
        var data = new DataObject();
        data.SetData(DragPayload.Marker, "1");
        ((Control)sender).DoDragDrop(data, DragDropEffects.Copy);
        DragPayload.Current = null;
        // класс мог уточниться, пока модель грузилась
        var last = _ed.Selected;
        if (last != null && last.Kind == ObjKind.Prop)
        {
            var mesh = _assets.GetMesh(last.Model);
            if (mesh != null && mesh.PhysicsOnly && last.Cls == "prop_static") last.Cls = "prop_physics";
        }
    }

    /// <summary>Двойной клик по элементу библиотеки — поставить перед камерой.</summary>
    void PlaceInFront(ListView lv)
    {
        if (lv.SelectedItems.Count == 0) return;
        var tpl = TemplateFrom(lv.SelectedItems[0]);
        if (tpl == null) return;
        var hit = _vp.PickTerrain(new Point(_vp.Width / 2, _vp.Height / 2));
        var p = hit ?? new System.Numerics.Vector3(0, 0, 0);
        tpl.Pos = new[] { _ed.SnapV(p.X), _ed.SnapV(p.Y), _ed.Scene.Terrain.HeightAt(p.X, p.Y) + (tpl.Kind == ObjKind.Light ? 96 : 0) };
        var mesh = tpl.Kind == ObjKind.Prop ? _assets.GetMesh(tpl.Model) : null;
        if (mesh != null && mesh.Mins[2] < 0) tpl.Pos[2] -= mesh.Mins[2];
        _ed.Add(tpl);
    }

    // ================================================================== свойства и список объектов
    void ShowProps()
    {
        var o = _ed.Selected;
        _props.SelectedObject = o == null ? null : o.Kind switch
        {
            ObjKind.Prop => new PropProps(o, _ed, m => _assets.GetMesh(m)?.PhysicsOnly ?? false),
            ObjKind.Block => new BlockProps(o, _ed),
            ObjKind.Light => new LightProps(o, _ed),
            _ => (object)new EntityProps(o, _ed),
        };
    }

    void RefreshOutliner()
    {
        _props.Refresh();
        var groups = new[] { (ObjKind.Prop, "Модели"), (ObjKind.Entity, "Сущности и NPC"), (ObjKind.Block, "Блоки"), (ObjKind.Light, "Свет") };
        // перестраиваем только если изменился состав
        string sig = string.Join("|", _ed.Scene.Objects.Select(o => o.GetHashCode() + o.Name));
        if (sig == _outlinerSig) return;
        _outlinerSig = sig;
        _outliner.BeginUpdate();
        _outliner.Nodes.Clear();
        foreach (var (kind, title) in groups)
        {
            var items = _ed.Scene.Objects.Where(o => o.Kind == kind).ToList();
            if (items.Count == 0) continue;
            var n = _outliner.Nodes.Add($"{title} ({items.Count})");
            n.ForeColor = Theme.Accent;
            foreach (var o in items)
                n.Nodes.Add(new TreeNode(string.IsNullOrEmpty(o.Name) ? o.Cls : $"{o.Name}  ·  {(o.Kind == ObjKind.Prop ? o.Cls : o.Kind == ObjKind.Block ? o.Material : o.Cls)}") { Tag = o });
            n.Expand();
        }
        _outliner.EndUpdate();
        SyncOutlinerSelection();
    }
    string _outlinerSig;

    void SyncOutlinerSelection()
    {
        foreach (TreeNode g in _outliner.Nodes)
            foreach (TreeNode n in g.Nodes)
                if (n.Tag == _ed.Selected) { if (_outliner.SelectedNode != n) _outliner.SelectedNode = n; return; }
        if (_ed.Selected == null) _outliner.SelectedNode = null;
    }

    // ================================================================== файлы проекта
    bool ConfirmDiscard()
    {
        if (!_ed.Dirty) return true;
        var r = MessageBox.Show(this, "Сохранить изменения в текущей карте?", "HL2 Toolkit", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) return SaveProject(false);
        return true;
    }

    void NewMap()
    {
        if (!ConfirmDiscard()) return;
        var s = Scene.CreateDefault();
        _ed.LoadScene(s, null);
        _syncEnv();
        _vp.LookAtCenter();
    }

    void OpenProject()
    {
        if (!ConfirmDiscard()) return;
        Directory.CreateDirectory(Toolchain.ProjectsDir);
        using var dlg = new OpenFileDialog { Filter = "Проект HL2 Toolkit (*.hl2tk)|*.hl2tk|Все файлы|*.*", InitialDirectory = Toolchain.ProjectsDir };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _ed.LoadScene(Scene.FromJson(File.ReadAllText(dlg.FileName)), dlg.FileName);
            _syncEnv();
            Log("Открыт проект: " + dlg.FileName);
        }
        catch (Exception ex) { MessageBox.Show(this, "Не удалось открыть проект:\n" + ex.Message, "HL2 Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    bool SaveProject(bool saveAs)
    {
        string path = _ed.ProjectPath;
        if (saveAs || path == null)
        {
            Directory.CreateDirectory(Toolchain.ProjectsDir);
            using var dlg = new SaveFileDialog { Filter = "Проект HL2 Toolkit (*.hl2tk)|*.hl2tk", InitialDirectory = Toolchain.ProjectsDir, FileName = Toolchain.SafeName(_ed.Scene.Name) + ".hl2tk" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            path = dlg.FileName;
        }
        File.WriteAllText(path, _ed.Scene.ToJson());
        _ed.ProjectPath = path; _ed.Dirty = false;
        Log("Сохранено: " + path);
        return true;
    }

    void ExportVmf()
    {
        using var dlg = new SaveFileDialog { Filter = "Valve Map File (*.vmf)|*.vmf", FileName = Toolchain.SafeName(_ed.Scene.Name) + ".vmf" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, VmfWriter.Build(_ed.Scene));
        Log("VMF экспортирован: " + dlg.FileName + " — его можно открыть в Hammer.");
    }

    void OnClosing(object s, FormClosingEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
        else _buildCts?.Cancel();
    }

    void PickGameFolder(bool rtx)
    {
        using var dlg = new FolderBrowserDialog { Description = rtx ? "Папка Half-Life 2 RTX" : "Папка Half-Life 2 (где лежит hl2.exe)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var cfg = Toolchain.LoadConfig();
        if (rtx) cfg.Hl2RtxRoot = dlg.SelectedPath; else cfg.Hl2Root = dlg.SelectedPath;
        Toolchain.SaveConfig(cfg);
        InitGame();
    }

    static void OpenFolder(string p)
    {
        Directory.CreateDirectory(p);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{p}\"") { UseShellExecute = true });
    }

    // ================================================================== сборка
    void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(s)); return; }
        _log.AppendText(s + Environment.NewLine);
    }

    void StartBuild(bool launch)
    {
        if (_buildCts != null) { Log("Сборка уже идёт."); return; }
        if (_game == null || !_game.Ok) { PickGameFolder(false); return; }
        bool rtx = _rtxBtn.Checked;
        if (rtx && !_game.RtxAvailable)
        {
            ShowRtxHelp();
            rtx = false;
        }
        if (_ed.ProjectPath != null) SaveProject(false);
        var scene = Scene.FromJson(_ed.Scene.ToJson());     // копия — можно редактировать дальше во время сборки
        var opt = new Toolchain.BuildOptions { Final = _finalBtn.Checked, Rtx = rtx, Launch = launch };
        _log.Clear();
        _buildCts = new CancellationTokenSource();
        var ct = _buildCts.Token;
        _playBtn.Enabled = _buildBtn.Enabled = false;
        _progress.Value = 0;
        Task.Run(() =>
        {
            try
            {
                var name = Toolchain.BuildMap(scene, opt, Log, (stage, p) => BeginInvoke(() =>
                {
                    if (stage != null) _stageLabel.Text = "  " + stage;
                    _progress.Value = Math.Clamp((int)(p * 100), 0, 100);
                }), ct);
                BeginInvoke(() => { _stageLabel.Text = $"  Готово: maps/{name}.bsp" + (launch ? " — запускаю игру" : ""); _stageLabel.ForeColor = Color.FromArgb(120, 220, 120); });
            }
            catch (OperationCanceledException) { Log("Сборка остановлена."); BeginInvoke(() => _stageLabel.Text = "  Остановлено"); }
            catch (Exception ex)
            {
                Log("ОШИБКА: " + ex.Message);
                BeginInvoke(() => { _stageLabel.Text = "  Ошибка: " + ex.Message; _stageLabel.ForeColor = Color.FromArgb(240, 100, 90); });
            }
            finally
            {
                BeginInvoke(() =>
                {
                    _buildCts = null; _playBtn.Enabled = _buildBtn.Enabled = true;
                    Task.Delay(4000).ContinueWith(_ => BeginInvoke(() => _stageLabel.ForeColor = Theme.Accent));
                });
            }
        });
    }

    // ================================================================== импорт моделей
    void ImportFile(string path)
    {
        if (path != null && !path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            if (path.EndsWith(".hl2tk", StringComparison.OrdinalIgnoreCase) && ConfirmDiscard())
            { _ed.LoadScene(Scene.FromJson(File.ReadAllText(path)), path); _syncEnv(); return; }
            MessageBox.Show(this, "Поддерживаются модели в формате .obj (экспорт есть в Blender, 3ds Max, SketchUp и др.).\nFBX/GLB сначала экспортируйте в OBJ.", "Импорт модели", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_game == null || !_game.Ok) { PickGameFolder(false); return; }
        using var dlg = new ImportDialog(path);
        Theme.Apply(dlg);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var req = dlg.Request;
        _log.Clear();
        _stageLabel.Text = "  Импорт модели: " + req.Name;
        _progress.Style = ProgressBarStyle.Marquee;
        Task.Run(() =>
        {
            try
            {
                var model = Toolchain.ImportModel(req, Log, CancellationToken.None);
                BeginInvoke(() =>
                {
                    _assets.Invalidate(model);
                    _icons.Images.RemoveByKey(GameFS.Norm(model));
                    _thumbRequested.Remove(GameFS.Norm(model));
                    var norm = GameFS.Norm(model);
                    if (!_allModels.Any(m => m.path == norm)) _allModels.Add((norm, "hl2toolkit", Path.GetFileNameWithoutExtension(norm)));
                    FillMine();
                    _stageLabel.Text = "  Модель готова — перетащите её из вкладки «Мои модели»";
                    var o = new SceneObject { Kind = ObjKind.Prop, Model = norm, Cls = req.Physics ? "prop_physics" : "prop_static", Name = req.Name };
                    var hit = _vp.PickTerrain(new Point(_vp.Width / 2, _vp.Height / 2)) ?? System.Numerics.Vector3.Zero;
                    o.Pos = new[] { hit.X, hit.Y, _ed.Scene.Terrain.HeightAt(hit.X, hit.Y) };
                    _ed.Add(o);
                });
            }
            catch (Exception ex) { Log("ОШИБКА: " + ex.Message); BeginInvoke(() => _stageLabel.Text = "  Ошибка импорта: " + ex.Message); }
            finally { BeginInvoke(() => _progress.Style = ProgressBarStyle.Continuous); }
        });
    }

    // ================================================================== справка
    void ShowHelp() => MessageBox.Show(this,
        "КАМЕРА\n" +
        "  Зажать ПКМ + мышь — осмотреться;  ПКМ + W/A/S/D — лететь;  Q/E — вниз/вверх\n" +
        "  Shift — быстрее;  колесо — вперёд/назад;  ПКМ + колесо — скорость полёта;  СКМ — сдвиг\n" +
        "  F — к выделенному объекту;  Home — к центру карты\n\n" +
        "ОБЪЕКТЫ\n" +
        "  Перетащите модель, NPC, оружие или блок из библиотеки слева прямо в мир.\n" +
        "  Двойной клик по элементу библиотеки — поставить в центр экрана.\n" +
        "  ЛКМ по объекту — выбрать; тащить — двигать по земле.\n" +
        "  W — стрелки X/Y/Z;  E — кольцо вращения;  R — масштаб / размер блока.\n" +
        "  Ctrl+D — копия;  Del — удалить;  End — поставить на землю;  Ctrl+Z / Ctrl+Y — отмена/повтор.\n\n" +
        "РЕЛЬЕФ\n" +
        "  Кнопки «Поднять / Опустить / Сгладить / Выровнять / Краска» — кисти. Водите с зажатой ЛКМ.\n" +
        "  Ctrl — обратное действие. Радиус и сила — на панели инструментов.\n\n" +
        "СБОРКА\n" +
        "  F5 «Собрать и играть» — VBSP → VVIS → VRAD, копирование в hl2/maps и запуск игры.\n" +
        "  «Финальный свет» — красивее, но дольше. «RTX» — запуск в Half-Life 2 RTX.\n" +
        "  Карта автоматически закрывается небом — утечек (leak) не бывает.",
        "Управление", MessageBoxButtons.OK, MessageBoxIcon.Information);

    void ShowRtxHelp() => MessageBox.Show(this,
        "Режим RTX запускает вашу карту с трассировкой лучей через NVIDIA RTX Remix.\n\n" +
        (_game?.RtxAvailable == true ? "RTX найден: " + (_game.RtxExe ?? "RTX Remix в папке HL2") + "\n\n" :
        "Сейчас RTX не найден. Варианты:\n" +
        "  1) Установите «Half-Life 2 RTX» из Steam (бесплатно для владельцев HL2) — редактор найдёт его сам\n" +
        "     или укажите папку: Настройки → Папка Half-Life 2 RTX.\n" +
        "  2) Или положите RTX Remix (d3d9.dll + папка .trex) в папку Half-Life 2\\bin.\n\n") +
        "Как это работает:\n" +
        "  • Освещение VRAD запекается в быстром режиме — свет и тени считает RTX в реальном времени.\n" +
        "  • Шейдеры RTX Remix компилирует сам при первом запуске игры (это занимает несколько минут,\n" +
        "    потом они берутся из кэша). Отдельно компилировать шейдеры Source для этого не нужно.\n" +
        "  • Нужна видеокарта NVIDIA RTX (или другая с поддержкой Vulkan ray tracing).",
        "RTX", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
