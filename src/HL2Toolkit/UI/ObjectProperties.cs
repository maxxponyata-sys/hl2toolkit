using System.ComponentModel;

namespace HL2Toolkit.UI;

/// <summary>Обёртки объектов сцены для PropertyGrid — русские названия, выпадающие списки, отмена.</summary>
public abstract class PropsBase
{
    [Browsable(false)] public SceneObject O { get; }
    protected readonly EditorState Ed;
    protected PropsBase(SceneObject o, EditorState ed) { O = o; Ed = ed; }
    protected void Set(Action a) { Ed.PushUndo(); a(); Ed.NotifyChanged(); }

    [Category("1. Общее"), DisplayName("Имя"), Description("Имя объекта (для списка объектов и логики карты).")]
    public string Name { get => O.Name; set => Set(() => O.Name = value); }

    [Category("2. Положение"), DisplayName("X"), Description("Позиция по оси X (красная стрелка).")]
    public float X { get => O.Pos[0]; set => Set(() => O.Pos[0] = value); }
    [Category("2. Положение"), DisplayName("Y"), Description("Позиция по оси Y (зелёная стрелка).")]
    public float Y { get => O.Pos[1]; set => Set(() => O.Pos[1] = value); }
    [Category("2. Положение"), DisplayName("Z (высота)"), Description("Высота (синяя стрелка). 1 метр ≈ 52 юнита, человек ≈ 72.")]
    public float Z { get => O.Pos[2]; set => Set(() => O.Pos[2] = value); }
    [Category("2. Положение"), DisplayName("Поворот (рыскание)"), Description("Поворот вокруг вертикальной оси, градусы.")]
    public float Yaw { get => O.Ang[1]; set => Set(() => O.Ang[1] = value); }
}

public sealed class ClassConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) =>
        new(new[] { "prop_static", "prop_physics", "prop_dynamic" });
}

public sealed class MaterialConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new(Catalog.BlockMaterials);
}

public sealed class PropProps : PropsBase
{
    readonly Func<string, bool> _physicsOnly;
    public PropProps(SceneObject o, EditorState ed, Func<string, bool> physicsOnly) : base(o, ed) { _physicsOnly = physicsOnly; }

    [Category("1. Общее"), DisplayName("Модель"), ReadOnly(true)]
    public string Model => O.Model;

    [Category("1. Общее"), DisplayName("Тип"), TypeConverter(typeof(ClassConverter)),
     Description("prop_static — неподвижная декорация (самая быстрая).\nprop_physics — физический объект, можно толкать и бросать грави-пушкой.\nprop_dynamic — неподвижный, но может анимироваться.")]
    public string Cls
    {
        get => O.Cls;
        set
        {
            if (value == "prop_static" && _physicsOnly(O.Model))
            {
                MessageBox.Show("Эта модель в Half-Life 2 бывает только физической (prop_physics) — движок не разрешает делать её статичной.",
                    "HL2 Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Set(() => O.Cls = value);
        }
    }

    [Category("2. Положение"), DisplayName("Наклон (тангаж)")]
    public float Pitch { get => O.Ang[0]; set => Set(() => O.Ang[0] = value); }
    [Category("2. Положение"), DisplayName("Крен")]
    public float Roll { get => O.Ang[2]; set => Set(() => O.Ang[2] = value); }
    [Category("2. Положение"), DisplayName("Масштаб"), Description("Работает для prop_static и prop_dynamic.")]
    public float Scale { get => O.Scale; set => Set(() => O.Scale = Math.Clamp(value, 0.05f, 20)); }
    [Category("1. Общее"), DisplayName("Скин"), Description("Номер варианта раскраски модели (если есть).")]
    public int Skin { get => O.Skin; set => Set(() => O.Skin = Math.Max(0, value)); }
}

public sealed class EntityProps : PropsBase
{
    public EntityProps(SceneObject o, EditorState ed) : base(o, ed) { }

    [Category("1. Общее"), DisplayName("Класс"), ReadOnly(true)]
    public string Cls => O.Cls;

    [Category("3. Параметры"), DisplayName("Оружие NPC"), Description("Например weapon_smg1, weapon_ar2, weapon_shotgun, weapon_pistol. Пусто — без оружия.")]
    public string Weapon
    {
        get => O.Keys.GetValueOrDefault("additionalequipment", "");
        set => Set(() => { if (string.IsNullOrWhiteSpace(value)) O.Keys.Remove("additionalequipment"); else O.Keys["additionalequipment"] = value.Trim(); });
    }

    [Category("3. Параметры"), DisplayName("Ключи (key=value; ...)"), Description("Дополнительные ключи сущности Source через точку с запятой. Для опытных.")]
    public string Keys
    {
        get => string.Join("; ", O.Keys.Select(k => k.Key + "=" + k.Value));
        set => Set(() =>
        {
            O.Keys.Clear();
            foreach (var part in (value ?? "").Split(';'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Trim().Length > 0) O.Keys[kv[0].Trim()] = kv[1].Trim();
            }
        });
    }
}

public sealed class BlockProps : PropsBase
{
    public BlockProps(SceneObject o, EditorState ed) : base(o, ed) { }

    [Category("1. Общее"), DisplayName("Материал"), TypeConverter(typeof(MaterialConverter)),
     Description("Материал Source (путь без materials/ и .vmt). Можно вписать любой.")]
    public string Material { get => O.Material; set => Set(() => O.Material = value.Trim().Replace('\\', '/')); }

    [Category("3. Размер"), DisplayName("Ширина (X)")]
    public float SX { get => O.Size[0]; set => Set(() => O.Size[0] = Math.Max(1, value)); }
    [Category("3. Размер"), DisplayName("Глубина (Y)")]
    public float SY { get => O.Size[1]; set => Set(() => O.Size[1] = Math.Max(1, value)); }
    [Category("3. Размер"), DisplayName("Высота (Z)")]
    public float SZ { get => O.Size[2]; set => Set(() => O.Size[2] = Math.Max(1, value)); }
}

public sealed class LightProps : PropsBase
{
    public LightProps(SceneObject o, EditorState ed) : base(o, ed) { }

    [Category("3. Свет"), DisplayName("Цвет")]
    public Color Color
    {
        get => Color.FromArgb(O.Color[0], O.Color[1], O.Color[2]);
        set => Set(() => O.Color = new int[] { value.R, value.G, value.B });
    }
    [Category("3. Свет"), DisplayName("Яркость"), Description("Обычно 100–800.")]
    public float Brightness { get => O.Brightness; set => Set(() => O.Brightness = Math.Max(0, value)); }
    [Category("3. Свет"), DisplayName("Радиус"), Description("Расстояние, на котором яркость падает вдвое.")]
    public float Range { get => O.Range; set => Set(() => O.Range = Math.Max(8, value)); }
    [Category("2. Положение"), DisplayName("Наклон (для прожектора)"), Description("-90 — светит вниз.")]
    public float Pitch { get => O.Ang[0]; set => Set(() => O.Ang[0] = value); }
}
