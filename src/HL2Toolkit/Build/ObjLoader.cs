using System.Globalization;

namespace HL2Toolkit.Build;

/// <summary>Загрузчик Wavefront OBJ (+ .mtl для поиска текстуры map_Kd).</summary>
public sealed class ObjModel
{
    public List<float> Tris = new();     // [x y z nx ny nz u v] * 3, оси как в файле (Y вверх)
    public string TexturePath;
    public float[] Mins = new float[3], Maxs = new float[3];

    static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static ObjModel Load(string path)
    {
        var m = new ObjModel();
        var v = new List<float[]>(); var vt = new List<float[]>(); var vn = new List<float[]>();
        string dir = Path.GetDirectoryName(path);
        var mtlTextures = new Dictionary<string, string>();
        string currentMtl = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "v": v.Add(new[] { P(p[1]), P(p[2]), P(p[3]) }); break;
                case "vt": vt.Add(new[] { P(p[1]), p.Length > 2 ? P(p[2]) : 0 }); break;
                case "vn": vn.Add(new[] { P(p[1]), P(p[2]), P(p[3]) }); break;
                case "mtllib":
                    var mtl = Path.Combine(dir, line[7..].Trim());
                    if (File.Exists(mtl)) ReadMtl(mtl, mtlTextures);
                    break;
                case "usemtl":
                    currentMtl = p.Length > 1 ? p[1] : null;
                    if (m.TexturePath == null && currentMtl != null && mtlTextures.TryGetValue(currentMtl, out var tp)) m.TexturePath = tp;
                    break;
                case "f":
                    var face = new List<(int vi, int ti, int ni)>();
                    for (int i = 1; i < p.Length; i++)
                    {
                        var s = p[i].Split('/');
                        int Idx(string x, int count) { if (string.IsNullOrEmpty(x)) return -1; int k = int.Parse(x); return k < 0 ? count + k : k - 1; }
                        face.Add((Idx(s[0], v.Count), s.Length > 1 ? Idx(s[1], vt.Count) : -1, s.Length > 2 ? Idx(s[2], vn.Count) : -1));
                    }
                    for (int i = 1; i + 1 < face.Count; i++)
                    {
                        var tri = new[] { face[0], face[i], face[i + 1] };
                        // плоская нормаль на случай отсутствия vn
                        var a = v[tri[0].vi]; var b = v[tri[1].vi]; var c = v[tri[2].vi];
                        float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], wx = c[0] - a[0], wy = c[1] - a[1], wz = c[2] - a[2];
                        float nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
                        float nl = MathF.Max(1e-8f, MathF.Sqrt(nx * nx + ny * ny + nz * nz));
                        foreach (var cn in tri)
                        {
                            var pos = v[cn.vi];
                            m.Tris.AddRange(pos);
                            if (cn.ni >= 0 && cn.ni < vn.Count) m.Tris.AddRange(vn[cn.ni]);
                            else { m.Tris.Add(nx / nl); m.Tris.Add(ny / nl); m.Tris.Add(nz / nl); }
                            if (cn.ti >= 0 && cn.ti < vt.Count) { m.Tris.Add(vt[cn.ti][0]); m.Tris.Add(vt[cn.ti][1]); }
                            else { m.Tris.Add(0); m.Tris.Add(0); }
                        }
                    }
                    break;
            }
        }
        if (m.TexturePath == null && mtlTextures.Count > 0) m.TexturePath = mtlTextures.Values.First();
        m.Mins = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
        m.Maxs = new[] { float.MinValue, float.MinValue, float.MinValue };
        for (int i = 0; i < m.Tris.Count; i += 8)
            for (int k = 0; k < 3; k++) { m.Mins[k] = Math.Min(m.Mins[k], m.Tris[i + k]); m.Maxs[k] = Math.Max(m.Maxs[k], m.Tris[i + k]); }
        return m;
    }

    static void ReadMtl(string path, Dictionary<string, string> tex)
    {
        string cur = null, dir = Path.GetDirectoryName(path);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("newmtl ")) cur = line[7..].Trim();
            else if (cur != null && line.StartsWith("map_Kd ", StringComparison.OrdinalIgnoreCase))
            {
                var file = line[7..].Trim().Split(' ').Last();
                var full = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
                if (File.Exists(full)) tex.TryAdd(cur, full);
            }
        }
    }

    /// <summary>Переводит в координаты Source (Z вверх), масштабирует до нужной высоты и ставит на землю.
    /// UV: в OBJ v растёт вверх, в Source/SMD — тоже (studiomdl сам переворачивает).</summary>
    public List<float> ToSource(float targetHeight, bool zUp, float yawDeg)
    {
        var o = new List<float>(Tris.Count);
        float h = zUp ? Maxs[2] - Mins[2] : Maxs[1] - Mins[1];
        float s = h > 1e-6f ? targetHeight / h : 1;
        float cx = (Mins[0] + Maxs[0]) / 2;
        float cy = zUp ? (Mins[1] + Maxs[1]) / 2 : (Mins[2] + Maxs[2]) / 2;
        float bz = zUp ? Mins[2] : Mins[1];
        float yr = yawDeg * MathF.PI / 180, cs = MathF.Cos(yr), sn = MathF.Sin(yr);
        for (int i = 0; i < Tris.Count; i += 8)
        {
            float x, y, z, nx, ny, nz;
            if (zUp) { x = Tris[i] - cx; y = Tris[i + 1] - cy; z = Tris[i + 2] - bz; nx = Tris[i + 3]; ny = Tris[i + 4]; nz = Tris[i + 5]; }
            else { x = Tris[i] - cx; y = -(Tris[i + 2] - cy); z = Tris[i + 1] - bz; nx = Tris[i + 3]; ny = -Tris[i + 5]; nz = Tris[i + 4]; }
            float rx = x * cs - y * sn, ry = x * sn + y * cs, rnx = nx * cs - ny * sn, rny = nx * sn + ny * cs;
            o.Add(rx * s); o.Add(ry * s); o.Add(z * s); o.Add(rnx); o.Add(rny); o.Add(nz); o.Add(Tris[i + 6]); o.Add(Tris[i + 7]);
        }
        return o;
    }
}
