using HL2Toolkit.Build;

namespace HL2Toolkit.UI;

/// <summary>Мастер импорта своей модели: OBJ → SMD/QC/VTF → studiomdl.</summary>
public sealed class ImportDialog : Form
{
    public Toolchain.ImportRequest Request { get; private set; }

    readonly TextBox _file = new() { ReadOnly = true }, _name = new(), _tex = new() { ReadOnly = true };
    readonly NumericUpDown _height = new() { Minimum = 1, Maximum = 4000, DecimalPlaces = 0, Value = 64 };
    readonly NumericUpDown _yaw = new() { Minimum = -180, Maximum = 180, Increment = 90 };
    readonly NumericUpDown _mass = new() { Minimum = 1, Maximum = 5000, Value = 30 };
    readonly ComboBox _up = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _surface = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _info = new() { AutoSize = false, Height = 54, ForeColor = Theme.Dim };
    ObjModel _obj;

    public ImportDialog(string path)
    {
        Text = "Импорт своей модели";
        Size = new Size(560, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.UiFont;

        _up.Items.AddRange(new object[] { "Y вверх (Blender OBJ, Maya, большинство)", "Z вверх (3ds Max, SketchUp)" });
        _up.SelectedIndex = 0;
        _type.Items.AddRange(new object[] { "Статичная декорация (prop_static)", "Физический объект (prop_physics)" });
        _type.SelectedIndex = 0;
        _surface.Items.AddRange(new object[] { "default", "wood", "metal", "concrete", "plastic", "glass", "rubber", "dirt", "flesh" });
        _surface.SelectedIndex = 0;

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        void Row(string label, Control c, Control extra = null)
        {
            t.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Height = 30 });
            c.Dock = DockStyle.Fill; c.Margin = new Padding(2, 4, 2, 4);
            t.Controls.Add(c);
            if (extra != null) { extra.Dock = DockStyle.Fill; t.Controls.Add(extra); } else t.Controls.Add(new Label());
        }
        var browse = new Button { Text = "Обзор…" };
        browse.Click += (_, _) => PickObj();
        var texBrowse = new Button { Text = "Выбрать…" };
        texBrowse.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "Картинки|*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.gif|Все файлы|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) _tex.Text = d.FileName;
        };
        Row("Файл OBJ", _file, browse);
        Row("Имя модели", _name);
        Row("Текстура", _tex, texBrowse);
        Row("Ось «вверх»", _up);
        Row("Высота (юниты)", _height);
        var hint = new Label { Text = "Подсказки: человек ≈ 72, дверь ≈ 108, ящик ≈ 40, 1 метр ≈ 52 юнита.", ForeColor = Theme.Dim, Dock = DockStyle.Fill, Height = 24 };
        t.Controls.Add(new Label()); t.Controls.Add(hint); t.SetColumnSpan(hint, 2);
        Row("Поворот (°)", _yaw);
        Row("Тип", _type);
        Row("Масса (кг)", _mass);
        Row("Поверхность", _surface);
        t.Controls.Add(new Label()); t.Controls.Add(_info); t.SetColumnSpan(_info, 2);

        var ok = new Button { Text = "Скомпилировать модель", DialogResult = DialogResult.None, Width = 190, Height = 32, BackColor = Theme.AccentDark };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        ok.Click += (_, _) => Accept();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(8) };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        Controls.Add(t); Controls.Add(buttons);
        AcceptButton = ok; CancelButton = cancel;
        _type.SelectedIndexChanged += (_, _) => _mass.Enabled = _type.SelectedIndex == 1;
        _mass.Enabled = false;
        _up.SelectedIndexChanged += (_, _) => SuggestHeight();

        Shown += (_, _) => { if (path != null) Load(path); else PickObj(); };
    }

    void PickObj()
    {
        using var d = new OpenFileDialog { Filter = "Wavefront OBJ (*.obj)|*.obj" };
        if (d.ShowDialog(this) == DialogResult.OK) Load(d.FileName);
        else if (_obj == null) { DialogResult = DialogResult.Cancel; Close(); }
    }

    new void Load(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _obj = ObjModel.Load(path);
            _file.Text = path;
            _name.Text = Toolchain.SafeName(Path.GetFileNameWithoutExtension(path), "custom_model");
            if (_obj.TexturePath != null) _tex.Text = _obj.TexturePath;
            SuggestHeight();
        }
        catch (Exception ex) { MessageBox.Show(this, "Не удалось прочитать OBJ:\n" + ex.Message, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Cursor = Cursors.Default; }
    }

    void SuggestHeight()
    {
        if (_obj == null) return;
        bool zUp = _up.SelectedIndex == 1;
        float h = zUp ? _obj.Maxs[2] - _obj.Mins[2] : _obj.Maxs[1] - _obj.Mins[1];
        // маленькие числа — скорее всего метры (Blender), большие — сантиметры
        float units = h < 20 ? h * 52.49f : h > 500 ? h * 0.5249f : h;
        _height.Value = (decimal)Math.Clamp(MathF.Round(units), 1, 4000);
        _info.Text = $"Треугольников: {_obj.Tris.Count / 24}.  Высота в файле: {h:0.###}.\n" +
                     (h < 20 ? "Похоже на метры — пересчитано в юниты Source." : h > 500 ? "Похоже на сантиметры — пересчитано." : "Размер оставлен как есть.") +
                     (_obj.TexturePath == null ? "\nТекстура в .mtl не найдена — выберите картинку или модель будет серой." : "");
    }

    void Accept()
    {
        if (_obj == null) { PickObj(); return; }
        if (_obj.Tris.Count / 24 > 200000)
        {
            MessageBox.Show(this, "Слишком много треугольников для одной модели Source (больше 200 000). Упростите модель (Decimate в Blender).", "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Bitmap tex = null;
        if (_tex.Text.Length > 0 && File.Exists(_tex.Text))
            try { using var src = Image.FromFile(_tex.Text); tex = new Bitmap(src); } catch { tex = null; }
        Request = new Toolchain.ImportRequest
        {
            Name = Toolchain.SafeName(_name.Text, "custom_model"),
            Tris = _obj.ToSource((float)_height.Value, _up.SelectedIndex == 1, (float)_yaw.Value),
            Texture = tex,
            Physics = _type.SelectedIndex == 1,
            Mass = (float)_mass.Value,
            Surface = (string)_surface.SelectedItem,
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
