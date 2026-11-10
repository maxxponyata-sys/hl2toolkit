using System.Text.Json;

namespace HL2Toolkit;

public enum ObjKind { Prop, Entity, Block, Light }

/// <summary>Объект на карте. Координаты — Source (Z вверх).</summary>
public sealed class SceneObject
{
    public ObjKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Cls { get; set; } = "prop_static";
    public string Model { get; set; } = "";
    public float[] Pos { get; set; } = new float[3];
    public float[] Ang { get; set; } = new float[3];     // pitch yaw roll
    public float Scale { get; set; } = 1;
    public float[] Size { get; set; } = { 128, 128, 128 };  // для блоков
    public string Material { get; set; } = "brick/brickwall001a";
    public int[] Color { get; set; } = { 255, 220, 180 };
    public float Brightness { get; set; } = 300;
    public float Range { get; set; } = 256;
    public int Skin { get; set; }
    public Dictionary<string, string> Keys { get; set; } = new();

    public SceneObject Clone() => JsonSerializer.Deserialize<SceneObject>(JsonSerializer.Serialize(this));
}

public sealed class Terrain
{
    public float Size { get; set; } = 4096;
    public int Tiles { get; set; } = 8;
    public float[] Heights { get; set; }
    public float[] Alpha { get; set; }       // 0..255 — второй слой текстуры
    public string Material { get; set; } = "nature/blendgrassgravel001a";

    public const int Power = 3;
    public const int Seg = 1 << Power;
    public int Res => Tiles * Seg + 1;
    public float Step => Size / (Tiles * Seg);
    public float Half => Size / 2;

    public void Reset(float size)
    {
        Size = size;
        Tiles = Math.Clamp((int)(size / 512), 2, 16);
        Heights = new float[Res * Res];
        Alpha = new float[Res * Res];
    }

    public void EnsureValid()
    {
        if (Heights == null || Heights.Length != Res * Res) Heights = new float[Res * Res];
        if (Alpha == null || Alpha.Length != Res * Res) Alpha = new float[Res * Res];
    }

    /// <summary>Высота рельефа в мировой точке (билинейно).</summary>
    public float HeightAt(float x, float y)
    {
        float gx = (x + Half) / Step, gy = (y + Half) / Step;
        int r = Res;
        gx = Math.Clamp(gx, 0, r - 1.001f); gy = Math.Clamp(gy, 0, r - 1.001f);
        int ix = (int)gx, iy = (int)gy;
        float fx = gx - ix, fy = gy - iy;
        float h00 = Heights[iy * r + ix], h10 = Heights[iy * r + ix + 1];
        float h01 = Heights[(iy + 1) * r + ix], h11 = Heights[(iy + 1) * r + ix + 1];
        return (h00 * (1 - fx) + h10 * fx) * (1 - fy) + (h01 * (1 - fx) + h11 * fx) * fy;
    }
}

public sealed class Env
{
    public string Sky { get; set; } = "sky_day01_01";
    public float TimeOfDay { get; set; } = 14;     // часы 0..24
    public float SunYaw { get; set; } = 135;
    public bool Fog { get; set; }
    public int[] FogColor { get; set; } = { 180, 190, 205 };
    public float FogStart { get; set; } = 768;
    public float FogEnd { get; set; } = 6000;
    public bool Water { get; set; }
    public float WaterZ { get; set; } = -48;
    public string WaterMaterial { get; set; } = "nature/water_canals_water2";

    /// <summary>Солнце по времени суток: угол над горизонтом, цвет, яркость, окружающий свет.</summary>
    public (float pitch, int[] sun, float sunBright, int[] amb, float ambBright) Sun()
    {
        float t = TimeOfDay;
        float day = (float)Math.Sin(Math.Clamp((t - 6) / 14f, 0, 1) * Math.PI);    // 0 ночь .. 1 полдень
        float pitch = Math.Max(5, day * 75);
        float warm = 1 - day;
        int[] sun = { 255, (int)(245 - 90 * warm), (int)(225 - 150 * warm) };
        bool night = t < 5.5f || t > 20.5f;
        if (night) return (35, new[] { 120, 140, 200 }, 60, new[] { 40, 50, 80 }, 40);
        return (pitch, sun, 150 + 300 * day, new[] { (int)(120 + 20 * warm), 145, 185 }, 60 + 90 * day);
    }
}

public sealed class Scene
{
    public string Name { get; set; } = "mymap";
    public Terrain Terrain { get; set; } = new();
    public Env Env { get; set; } = new();
    public List<SceneObject> Objects { get; set; } = new();
    public bool GiveWeapons { get; set; } = true;

    public static Scene CreateDefault()
    {
        var s = new Scene();
        s.Terrain.Reset(4096);
        s.Objects.Add(new SceneObject { Kind = ObjKind.Entity, Cls = "info_player_start", Name = "Игрок", Pos = new float[] { 0, 0, 0 } });
        return s;
    }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };
    public string ToJson() => JsonSerializer.Serialize(this, Opts);
    public static Scene FromJson(string json)
    {
        var s = JsonSerializer.Deserialize<Scene>(json);
        s.Terrain.EnsureValid();
        return s;
    }
}
