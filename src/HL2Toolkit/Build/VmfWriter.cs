using System.Globalization;
using System.Text;

namespace HL2Toolkit.Build;

/// <summary>Собирает VMF из сцены. Карта запечатывается автоматически: коробка skybox вокруг уровня,
/// пол — сплошные брашы, на верхних гранях которых лежат дисплейсменты рельефа.</summary>
public static class VmfWriter
{
    public const float FloorBase = -512, Wall = 64;
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    static string N(double v)
    {
        v = Math.Round(v, 3);
        return v == Math.Floor(v) ? ((long)v).ToString(C) : v.ToString("0.###", C);
    }
    static string P((double x, double y, double z) p) => $"({N(p.x)} {N(p.y)} {N(p.z)})";

    sealed class Ctx
    {
        int _id;
        public int Next() => ++_id;
        public readonly StringBuilder World = new();
        public readonly StringBuilder Ents = new();
    }

    record Face((double x, double y, double z)[] Pts, (double x, double y, double z) U, (double x, double y, double z) V, string Name);

    static (double, double, double) RotZ((double x, double y, double z) p, double cx, double cy, double yaw)
    {
        double s = Math.Sin(yaw), c = Math.Cos(yaw), x = p.x - cx, y = p.y - cy;
        return (cx + x * c - y * s, cy + x * s + y * c, p.z);
    }

    static List<Face> BoxFaces(double[] mn, double[] mx, double yawDeg)
    {
        double x0 = mn[0], y0 = mn[1], z0 = mn[2], x1 = mx[0], y1 = mx[1], z1 = mx[2];
        var f = new List<Face>
        {
            new(new[] { (x0, y1, z1), (x1, y1, z1), (x1, y0, z1) }, (1, 0, 0), (0, -1, 0), "top"),
            new(new[] { (x0, y0, z0), (x1, y0, z0), (x1, y1, z0) }, (1, 0, 0), (0, -1, 0), "bottom"),
            new(new[] { (x0, y1, z1), (x0, y0, z1), (x0, y0, z0) }, (0, 1, 0), (0, 0, -1), "-x"),
            new(new[] { (x1, y1, z0), (x1, y0, z0), (x1, y0, z1) }, (0, 1, 0), (0, 0, -1), "+x"),
            new(new[] { (x1, y1, z1), (x0, y1, z1), (x0, y1, z0) }, (1, 0, 0), (0, 0, -1), "+y"),
            new(new[] { (x1, y0, z0), (x0, y0, z0), (x0, y0, z1) }, (1, 0, 0), (0, 0, -1), "-y"),
        };
        if (Math.Abs(yawDeg) < 1e-6) return f;
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, yaw = yawDeg * Math.PI / 180;
        return f.Select(fc => new Face(fc.Pts.Select(p => RotZ(p, cx, cy, yaw)).ToArray(),
            RotZ(fc.U, 0, 0, yaw), RotZ(fc.V, 0, 0, yaw), fc.Name)).ToList();
    }

    static void Solid(Ctx c, StringBuilder sb, double[] mn, double[] mx, Func<string, string> mat, double yaw = 0, string dispTop = null)
    {
        sb.Append("\tsolid\n\t{\n\t\t\"id\" \"").Append(c.Next()).Append("\"\n");
        foreach (var f in BoxFaces(mn, mx, yaw))
        {
            sb.Append("\t\tside\n\t\t{\n");
            sb.Append($"\t\t\t\"id\" \"{c.Next()}\"\n");
            sb.Append($"\t\t\t\"plane\" \"{P(f.Pts[0])} {P(f.Pts[1])} {P(f.Pts[2])}\"\n");
            sb.Append($"\t\t\t\"material\" \"{mat(f.Name).ToUpperInvariant()}\"\n");
            sb.Append($"\t\t\t\"uaxis\" \"[{N(f.U.x)} {N(f.U.y)} {N(f.U.z)} 0] 0.25\"\n");
            sb.Append($"\t\t\t\"vaxis\" \"[{N(f.V.x)} {N(f.V.y)} {N(f.V.z)} 0] 0.25\"\n");
            sb.Append("\t\t\t\"rotation\" \"0\"\n\t\t\t\"lightmapscale\" \"16\"\n\t\t\t\"smoothing_groups\" \"0\"\n");
            if (dispTop != null && f.Name == "top") sb.Append(dispTop);
            sb.Append("\t\t}\n");
        }
        sb.Append("\t}\n");
    }

    /// <summary>dispinfo: строки идут вдоль Y, столбцы вдоль X (как в Hammer, от startposition = min-угол).</summary>
    static string DispInfo(Terrain t, int tx, int ty, double x0, double y0)
    {
        int n = Terrain.Seg + 1, res = t.Res;
        var sb = new StringBuilder();
        sb.Append("\t\t\tdispinfo\n\t\t\t{\n");
        sb.Append($"\t\t\t\t\"power\" \"{Terrain.Power}\"\n\t\t\t\t\"startposition\" \"[{N(x0)} {N(y0)} 0]\"\n");
        sb.Append("\t\t\t\t\"flags\" \"0\"\n\t\t\t\t\"elevation\" \"0\"\n\t\t\t\t\"subdiv\" \"0\"\n");
        float H(int r, int col) => t.Heights[(ty * Terrain.Seg + r) * res + tx * Terrain.Seg + col];
        float A(int r, int col) => t.Alpha[(ty * Terrain.Seg + r) * res + tx * Terrain.Seg + col];
        void Block(string name, Func<int, int, string> cell)
        {
            sb.Append($"\t\t\t\t{name}\n\t\t\t\t{{\n");
            for (int r = 0; r < n; r++)
            {
                sb.Append($"\t\t\t\t\t\"row{r}\" \"");
                for (int col = 0; col < n; col++) { if (col > 0) sb.Append(' '); sb.Append(cell(r, col)); }
                sb.Append("\"\n");
            }
            sb.Append("\t\t\t\t}\n");
        }
        Block("normals", (r, col) => H(r, col) >= 0 ? "0 0 1" : "0 0 -1");
        Block("distances", (r, col) => N(Math.Abs(H(r, col))));
        Block("offsets", (r, col) => "0 0 0");
        Block("offset_normals", (r, col) => "0 0 1");
        Block("alphas", (r, col) => N(Math.Clamp(A(r, col), 0, 255)));
        sb.Append("\t\t\t\ttriangle_tags\n\t\t\t\t{\n");
        for (int r = 0; r < n - 1; r++)
            sb.Append($"\t\t\t\t\t\"row{r}\" \"").Append(string.Join(" ", Enumerable.Repeat("9", 2 * (n - 1)))).Append("\"\n");
        sb.Append("\t\t\t\t}\n\t\t\t\tallowed_verts\n\t\t\t\t{\n\t\t\t\t\t\"10\" \"-1 -1 -1 -1 -1 -1 -1 -1 -1 -1\"\n\t\t\t\t}\n");
        sb.Append("\t\t\t}\n");
        return sb.ToString();
    }

    static void Entity(Ctx c, string cls, float[] origin, IEnumerable<(string k, string v)> keys)
    {
        var sb = c.Ents;
        sb.Append("entity\n{\n").Append($"\t\"id\" \"{c.Next()}\"\n\t\"classname\" \"{cls}\"\n");
        if (origin != null) sb.Append($"\t\"origin\" \"{N(origin[0])} {N(origin[1])} {N(origin[2])}\"\n");
        foreach (var (k, v) in keys)
            if (!string.IsNullOrEmpty(v)) sb.Append($"\t\"{k}\" \"{v.Replace("\"", "'")}\"\n");
        sb.Append("}\n");
    }

    static string Ang(float[] a) => $"{N(a[0])} {N(a[1])} {N(a[2])}";
    static string Col(int[] c, double b) => $"{c[0]} {c[1]} {c[2]} {(int)b}";

    public static string Build(Scene scene)
    {
        var c = new Ctx();
        var t = scene.Terrain;
        t.EnsureValid();
        var env = scene.Env;
        double half = t.Half, tile = t.Size / t.Tiles;

        // --- рельеф
        for (int ty = 0; ty < t.Tiles; ty++)
            for (int tx = 0; tx < t.Tiles; tx++)
            {
                double x0 = -half + tx * tile, y0 = -half + ty * tile;
                Solid(c, c.World, new[] { x0, y0, FloorBase }, new[] { x0 + tile, y0 + tile, 0.0 },
                    f => f == "top" ? t.Material : "tools/toolsnodraw", 0, DispInfo(t, tx, ty, x0, y0));
            }

        // --- блоки
        float maxH = t.Heights.Max();
        foreach (var o in scene.Objects)
        {
            maxH = Math.Max(maxH, o.Pos[2] + (o.Kind == ObjKind.Block ? o.Size[2] : 600));
            if (o.Kind != ObjKind.Block) continue;
            double sx = o.Size[0], sy = o.Size[1], sz = o.Size[2];
            string m = o.Material;
            Solid(c, c.World, new double[] { o.Pos[0] - sx / 2, o.Pos[1] - sy / 2, o.Pos[2] },
                new double[] { o.Pos[0] + sx / 2, o.Pos[1] + sy / 2, o.Pos[2] + sz }, _ => m, o.Ang[1]);
        }

        // --- коробка неба
        double ceil = Math.Min(15000, Math.Max(2048, Math.Ceiling((maxH + 1024) / 256.0) * 256));
        double lo = FloorBase - Wall, W = Wall;
        Func<string, string> sky = _ => "tools/toolsskybox", nodraw = _ => "tools/toolsnodraw";
        Solid(c, c.World, new[] { -half - W, -half - W, ceil }, new[] { half + W, half + W, ceil + W }, sky);
        Solid(c, c.World, new[] { -half - W, -half - W, lo }, new[] { half + W, half + W, FloorBase }, nodraw);
        Solid(c, c.World, new[] { -half - W, -half - W, lo }, new[] { -half, half + W, ceil }, sky);
        Solid(c, c.World, new[] { half, -half - W, lo }, new[] { half + W, half + W, ceil }, sky);
        Solid(c, c.World, new[] { -half, -half - W, lo }, new[] { half, -half, ceil }, sky);
        Solid(c, c.World, new[] { -half, half, lo }, new[] { half, half + W, ceil }, sky);

        // --- вода (обычный world-браш с водным материалом сверху)
        if (env.Water)
        {
            string wm = env.WaterMaterial;
            Solid(c, c.World, new[] { -half, -half, FloorBase }, new[] { half, half, (double)env.WaterZ },
                f => f == "top" ? wm : "tools/toolsnodraw");
            Entity(c, "env_cubemap", new[] { 0f, 0f, env.WaterZ + 64 }, new[] { ("cubemapsize", "0") });
        }

        // --- окружение
        var (pitch, sun, sunB, amb, ambB) = env.Sun();
        float z = (float)ceil - 128;
        Entity(c, "light_environment", new[] { 0f, 0f, z }, new[]
        {
            ("angles", $"0 {N(env.SunYaw)} 0"), ("pitch", N(-pitch)), ("_light", Col(sun, sunB)),
            ("_ambient", Col(amb, ambB)), ("_lightHDR", "-1 -1 -1 1"), ("_ambientHDR", "-1 -1 -1 1"), ("SunSpreadAngle", "0"),
        });
        Entity(c, "shadow_control", new[] { 64f, 0f, z }, new[] { ("angles", $"{N(pitch)} {N(env.SunYaw)} 0"), ("color", "64 64 64"), ("distance", "75") });
        Entity(c, "env_sun", new[] { 128f, 0f, z }, new[] { ("angles", $"0 {N(env.SunYaw)} 0"), ("pitch", N(-pitch)), ("rendercolor", "255 240 200"), ("size", "16") });
        if (env.Fog)
        {
            string fc = $"{env.FogColor[0]} {env.FogColor[1]} {env.FogColor[2]}";
            Entity(c, "env_fog_controller", new[] { 192f, 0f, z }, new[]
            {
                ("fogenable", "1"), ("fogcolor", fc), ("fogcolor2", fc), ("fogstart", N(env.FogStart)), ("fogend", N(env.FogEnd)),
                ("fogmaxdensity", "0.9"), ("farz", "-1"), ("spawnflags", "1"), ("targetname", "fog_main"),
            });
        }
        Entity(c, "env_cubemap", new[] { 0f, 0f, t.HeightAt(0, 0) + 128 }, new[] { ("cubemapsize", "0") });

        // --- объекты
        bool player = false;
        foreach (var o in scene.Objects)
        {
            var keys = new List<(string, string)>();
            switch (o.Kind)
            {
                case ObjKind.Prop:
                {
                    // В HL2 масштаб понимает только игра (ключ modelscale у prop_physics / prop_dynamic).
                    // VBSP из HL2 не умеет масштабировать prop_static, поэтому масштабированный
                    // статичный предмет автоматически превращается в неподвижный prop_dynamic.
                    bool scaled = Math.Abs(o.Scale - 1) > 1e-3;
                    string cls = o.Cls == "prop_static" && scaled ? "prop_dynamic" : o.Cls;
                    keys.Add(("model", o.Model)); keys.Add(("angles", Ang(o.Ang))); keys.Add(("skin", o.Skin.ToString()));
                    if (cls != "prop_static" && !string.IsNullOrEmpty(o.Name)) keys.Add(("targetname", SafeTarget(o.Name)));
                    if (cls == "prop_physics" || cls == "prop_physics_multiplayer")
                    { keys.Add(("spawnflags", "256")); keys.Add(("fademindist", "-1")); }
                    else
                    {
                        // у масштабированного prop_dynamic столкновения — по габаритам (они масштабируются вместе с моделью)
                        keys.Add(("solid", cls == "prop_dynamic" && scaled ? "2" : "6"));
                        keys.Add(("fademindist", "-1")); keys.Add(("fadescale", "1"));
                        if (cls == "prop_dynamic") keys.Add(("DisableBoneFollowers", "1"));
                    }
                    if (scaled && cls != "prop_static") keys.Add(("modelscale", N(o.Scale)));
                    Entity(c, cls, o.Pos, keys);
                    break;
                }
                case ObjKind.Entity:
                    if (o.Cls == "info_player_start") player = true;
                    keys.Add(("angles", Ang(o.Ang)));
                    foreach (var kv in o.Keys) keys.Add((kv.Key, kv.Value));
                    Entity(c, o.Cls, o.Pos, keys);
                    break;
                case ObjKind.Light:
                    keys.Add(("_light", Col(o.Color, o.Brightness))); keys.Add(("_lightHDR", "-1 -1 -1 1"));
                    keys.Add(("_quadratic_attn", "1")); keys.Add(("_fifty_percent_distance", N(o.Range)));
                    keys.Add(("_zero_percent_distance", N(o.Range * 2.5)));
                    if (o.Cls == "light_spot")
                    {
                        keys.Add(("angles", Ang(o.Ang))); keys.Add(("pitch", N(o.Ang[0])));
                        keys.Add(("_inner_cone", "30")); keys.Add(("_cone", "45"));
                    }
                    Entity(c, o.Cls, o.Pos, keys);
                    break;
            }
        }
        if (!player) Entity(c, "info_player_start", new[] { 0f, 0f, t.HeightAt(0, 0) + 16 }, new[] { ("angles", "0 0 0") });
        if (scene.GiveWeapons)
            Entity(c, "game_player_equip", new[] { 0f, 64f, 64f }, new[]
            {
                ("item_suit", "1"), ("weapon_crowbar", "1"), ("weapon_physcannon", "1"), ("weapon_pistol", "1"),
                ("weapon_smg1", "1"), ("weapon_shotgun", "1"), ("ammo_pistol", "5"), ("ammo_smg1", "5"), ("ammo_buckshot", "3"),
            });

        var sbAll = new StringBuilder();
        sbAll.Append("versioninfo\n{\n\t\"editorversion\" \"400\"\n\t\"editorbuild\" \"8000\"\n\t\"mapversion\" \"1\"\n\t\"formatversion\" \"100\"\n\t\"prefab\" \"0\"\n}\n");
        sbAll.Append("visgroups\n{\n}\nviewsettings\n{\n\t\"bSnapToGrid\" \"1\"\n\t\"bShowGrid\" \"1\"\n\t\"nGridSpacing\" \"64\"\n}\n");
        sbAll.Append("world\n{\n\t\"id\" \"1\"\n\t\"mapversion\" \"1\"\n\t\"classname\" \"worldspawn\"\n");
        sbAll.Append($"\t\"skyname\" \"{env.Sky}\"\n\t\"maxpropscreenwidth\" \"-1\"\n\t\"detailvbsp\" \"detail.vbsp\"\n\t\"detailmaterial\" \"detail/detailsprites\"\n");
        sbAll.Append(c.World).Append("}\n").Append(c.Ents);
        sbAll.Append("cameras\n{\n\t\"activecamera\" \"-1\"\n}\ncordon\n{\n\t\"mins\" \"(-1024 -1024 -1024)\"\n\t\"maxs\" \"(1024 1024 1024)\"\n\t\"active\" \"0\"\n}\n");
        return sbAll.ToString();
    }

    static string SafeTarget(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) && ch < 128 ? ch : '_').ToArray());
}
