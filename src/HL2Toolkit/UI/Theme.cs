namespace HL2Toolkit.UI;

/// <summary>Тёмная тема в духе редакторов Dunia/CryEngine: угольный фон, оранжевый акцент.</summary>
public static class Theme
{
    public static readonly Color Back = Color.FromArgb(37, 38, 41);
    public static readonly Color Panel = Color.FromArgb(45, 46, 50);
    public static readonly Color Field = Color.FromArgb(28, 29, 31);
    public static readonly Color Border = Color.FromArgb(62, 64, 69);
    public static readonly Color Text = Color.FromArgb(222, 222, 222);
    public static readonly Color Dim = Color.FromArgb(150, 152, 158);
    public static readonly Color Accent = Color.FromArgb(242, 140, 40);
    public static readonly Color AccentDark = Color.FromArgb(120, 72, 25);
    public static readonly Color Hover = Color.FromArgb(62, 64, 70);
    public static readonly Font UiFont = new("Segoe UI", 9f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Header = new("Segoe UI Semibold", 10.5f);

    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls) Apply(c);
        switch (root)
        {
            case TextBox or NumericUpDown or ComboBox or ListBox:
                root.BackColor = Field; root.ForeColor = Text; break;
            case ListView lv:
                lv.BackColor = Field; lv.ForeColor = Text; break;
            case TreeView tv:
                tv.BackColor = Field; tv.ForeColor = Text; tv.LineColor = Border; break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat; b.BackColor = Panel; b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = Hover; break;
            case CheckBox cb:
                cb.ForeColor = Text; break;
            case PropertyGrid pg:
                pg.BackColor = Back; pg.ViewBackColor = Field; pg.ViewForeColor = Text; pg.LineColor = Panel;
                pg.CategoryForeColor = Accent; pg.HelpBackColor = Panel; pg.HelpForeColor = Dim; pg.CategorySplitterColor = Panel;
                pg.ViewBorderColor = Border; pg.HelpBorderColor = Border; pg.SelectedItemWithFocusBackColor = AccentDark;
                pg.CommandsBackColor = Panel; pg.CommandsForeColor = Text; break;
            default:
                if (root is not ToolStrip) { root.BackColor = root is Label ? Color.Transparent : Back; root.ForeColor = Text; }
                break;
        }
    }

    public sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Panel;
        public override Color ToolStripGradientMiddle => Panel;
        public override Color ToolStripGradientEnd => Panel;
        public override Color ToolStripBorder => Border;
        public override Color MenuStripGradientBegin => Back;
        public override Color MenuStripGradientEnd => Back;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Panel;
        public override Color MenuItemPressedGradientEnd => Panel;
        public override Color MenuBorder => Border;
        public override Color ToolStripDropDownBackground => Panel;
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color ButtonSelectedBorder => Accent;
        public override Color ButtonCheckedGradientBegin => AccentDark;
        public override Color ButtonCheckedGradientEnd => AccentDark;
        public override Color ButtonCheckedHighlight => AccentDark;
        public override Color ButtonPressedGradientBegin => AccentDark;
        public override Color ButtonPressedGradientEnd => AccentDark;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Panel;
        public override Color StatusStripGradientBegin => Back;
        public override Color StatusStripGradientEnd => Back;
    }

    public sealed class Renderer : ToolStripProfessionalRenderer
    {
        public Renderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? (e.Item is ToolStripButton { Checked: true } ? Color.White : Text) : Dim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Text; base.OnRenderArrow(e); }
    }

    /// <summary>Панель-вкладки: ряд кнопок сверху и сменяемое содержимое (у TabControl нет тёмной темы).</summary>
    public sealed class Tabs : Panel
    {
        readonly FlowLayoutPanel _bar = new() { Dock = DockStyle.Top, Height = 30, Padding = new Padding(2, 2, 2, 0), WrapContents = false };
        readonly Panel _body = new() { Dock = DockStyle.Fill };
        readonly List<(Button b, Control c)> _pages = new();
        public Tabs()
        {
            Controls.Add(_body); Controls.Add(_bar);
            _bar.BackColor = Back;
        }
        public Control Add(string title, Control content)
        {
            var b = new Button
            {
                Text = title, AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 2, 0),
                ForeColor = Theme.Text, BackColor = Panel, Font = Bold, Padding = new Padding(6, 0, 6, 0),
            };
            b.FlatAppearance.BorderSize = 0;
            content.Dock = DockStyle.Fill;
            content.Visible = false;
            _body.Controls.Add(content);
            int idx = _pages.Count;
            b.Click += (_, _) => Show(idx);
            _bar.Controls.Add(b);
            _pages.Add((b, content));
            if (_pages.Count == 1) Show(0);
            return content;
        }
        public void Show(int i)
        {
            for (int k = 0; k < _pages.Count; k++)
            {
                _pages[k].c.Visible = k == i;
                _pages[k].b.BackColor = k == i ? AccentDark : Panel;
                _pages[k].b.ForeColor = k == i ? Color.White : Theme.Text;
            }
        }
    }

    public static Label Caption(string text) => new()
    {
        Text = text, Dock = DockStyle.Top, Height = 24, Font = Header, ForeColor = Accent,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0),
    };
}
