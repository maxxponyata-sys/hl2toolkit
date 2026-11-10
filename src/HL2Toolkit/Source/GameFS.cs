using System.Text;
using System.Text.RegularExpressions;

namespace HL2Toolkit.Source;

/// <summary>Читает *_dir.vpk (версии 1 и 2).</summary>
public sealed class Vpk
{
    readonly string _dirPath, _base;
    readonly long _dataStart;
    public readonly Dictionary<string, (ushort arc, uint off, uint len, byte[] pre)> Entries = new();

    public Vpk(string dirPath)
    {
        _dirPath = dirPath;
        _base = dirPath[..^"_dir.vpk".Length];
        using var f = File.OpenRead(dirPath);
        using var r = new BinaryReader(f);
        uint sig = r.ReadUInt32(), ver = r.ReadUInt32(), treeSize = r.ReadUInt32();
        if (sig != 0x55AA1234) throw new InvalidDataException("не VPK: " + dirPath);
        int header = 12;
        if (ver == 2) { r.ReadBytes(16); header = 28; }
        byte[] t = r.ReadBytes((int)treeSize);
        _dataStart = header + treeSize;
        int p = 0;
        string Str()
        {
            int e = Array.IndexOf(t, (byte)0, p);
            string s = Encoding.Latin1.GetString(t, p, e - p);
            p = e + 1;
            return s;
        }
        while (true)
        {
            string ext = Str(); if (ext.Length == 0) break;
            while (true)
            {
                string path = Str(); if (path.Length == 0) break;
                while (true)
                {
                    string name = Str(); if (name.Length == 0) break;
                    ushort preload = BitConverter.ToUInt16(t, p + 4);
                    ushort arc = BitConverter.ToUInt16(t, p + 6);
                    uint off = BitConverter.ToUInt32(t, p + 8);
                    uint len = BitConverter.ToUInt32(t, p + 12);
                    p += 18;
                    byte[] pre = t[p..(p + preload)];
                    p += preload;
                    string full = path == " " ? $"{name}.{ext}" : $"{path}/{name}.{ext}";
                    Entries[full.ToLowerInvariant()] = (arc, off, len, pre);
                }
            }
        }
    }

    public byte[] Read(string path)
    {
        if (!Entries.TryGetValue(path, out var e)) return null;
        if (e.len == 0) return e.pre;
        string fn; long off = e.off;
        if (e.arc == 0x7FFF) { fn = _dirPath; off += _dataStart; }
        else fn = $"{_base}_{e.arc:D3}.vpk";
        using var f = File.OpenRead(fn);
        f.Seek(off, SeekOrigin.Begin);
        var buf = new byte[e.pre.Length + e.len];
        Buffer.BlockCopy(e.pre, 0, buf, 0, e.pre.Length);
        f.ReadExactly(buf, e.pre.Length, (int)e.len);
        return buf;
    }
}

/// <summary>Файловая система игры: сначала свободные файлы (custom/*, hl2/), потом VPK.</summary>
public sealed class GameFS
{
    public readonly string GameDir;
    readonly List<string> _loose = new();
    readonly List<Vpk> _vpks = new();

    public GameFS(string gameDir)
    {
        GameDir = gameDir;
        string custom = Path.Combine(gameDir, "custom");
        if (Directory.Exists(custom))
            foreach (var d in Directory.GetDirectories(custom).OrderBy(x => x)) _loose.Add(d);
        _loose.Add(gameDir);
        foreach (var n in new[] { "hl2_misc_dir.vpk", "hl2_pak_dir.vpk", "hl2_textures_dir.vpk" })
        {
            string p = Path.Combine(gameDir, n);
            if (File.Exists(p))
                try { _vpks.Add(new Vpk(p)); } catch { /* повреждённый VPK пропускаем */ }
        }
    }

    public static string Norm(string p) => p.Replace('\\', '/').ToLowerInvariant().TrimStart('/');

    public byte[] Read(string path)
    {
        path = Norm(path);
        foreach (var root in _loose)
        {
            string p = Path.Combine(root, path);
            if (File.Exists(p)) return File.ReadAllBytes(p);
        }
        lock (_vpks)
            foreach (var v in _vpks)
            {
                var d = v.Read(path);
                if (d != null) return d;
            }
        return null;
    }

    public bool Exists(string path)
    {
        path = Norm(path);
        foreach (var root in _loose) if (File.Exists(Path.Combine(root, path))) return true;
        foreach (var v in _vpks) if (v.Entries.ContainsKey(path)) return true;
        return false;
    }

    public List<string> List(string prefix, string ext)
    {
        var set = new HashSet<string>();
        foreach (var v in _vpks)
            foreach (var k in v.Entries.Keys)
                if (k.StartsWith(prefix) && k.EndsWith(ext)) set.Add(k);
        foreach (var root in _loose)
        {
            string b = Path.Combine(root, prefix);
            if (!Directory.Exists(b)) continue;
            foreach (var f in Directory.EnumerateFiles(b, "*" + ext, SearchOption.AllDirectories))
                set.Add(Norm(Path.GetRelativePath(root, f)));
        }
        return set.OrderBy(x => x).ToList();
    }

    /// <summary>Простой разбор VMT: шейдер + ключи $param (с поддержкой "patch"/include).</summary>
    public Dictionary<string, string> ReadVmt(string material)
    {
        var data = Read("materials/" + Norm(material) + ".vmt");
        if (data == null) return null;
        var kv = ParseVmt(Encoding.Latin1.GetString(data));
        if (kv["_shader"] == "patch")
        {
            var m = Regex.Match(Encoding.Latin1.GetString(data), "\"?include\"?\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var inc = Read(m.Groups[1].Value);
                if (inc != null)
                {
                    var baseKv = ParseVmt(Encoding.Latin1.GetString(inc));
                    foreach (var p in kv) if (p.Key != "_shader") baseKv[p.Key] = p.Value;
                    kv = baseKv;
                }
            }
        }
        return kv;
    }

    public static Dictionary<string, string> ParseVmt(string text)
    {
        var d = new Dictionary<string, string>();
        var sh = Regex.Match(text, "^\\s*\"?([\\w]+)\"?");
        d["_shader"] = sh.Success ? sh.Groups[1].Value.ToLowerInvariant() : "";
        foreach (Match m in Regex.Matches(text, "\"?(\\$[\\w]+)\"?[ \\t]+\"?([^\"\\r\\n]+?)\"?[ \\t]*[\\r\\n]"))
        {
            string k = m.Groups[1].Value.ToLowerInvariant();
            if (!d.ContainsKey(k)) d[k] = m.Groups[2].Value.Trim();
        }
        return d;
    }
}
