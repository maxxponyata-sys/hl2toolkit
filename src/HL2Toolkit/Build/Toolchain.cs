using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HL2Toolkit.Source;

namespace HL2Toolkit.Build;

public sealed class GameInfo
{
    public string Hl2Root, GameDir, Bin;
    public string RtxRoot, RtxExe, RtxGame;
    public bool RemixInHl2;
    public bool Ok => Hl2Root != null && File.Exists(Path.Combine(Hl2Root, "hl2.exe"));
    public bool RtxAvailable => RtxExe != null || RemixInHl2;
}

public sealed class AppConfig
{
    public string Hl2Root { get; set; }
    public string Hl2RtxRoot { get; set; }
}

/// <summary>Поиск игры, компиляция карт и моделей, запуск.</summary>
public static class Toolchain
{
    public static readonly string AppDir = AppContext.BaseDirectory;
    public static string WorkDir => Path.Combine(AppDir, "work");
    public static string ProjectsDir => Path.Combine(AppDir, "projects");
    static string ConfigPath => Path.Combine(AppDir, "config.json");

    public static AppConfig LoadConfig()
    {
        try { if (File.Exists(ConfigPath)) return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)); } catch { }
        return new AppConfig();
    }
    public static void SaveConfig(AppConfig c) => File.WriteAllText(ConfigPath, JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));

    static IEnumerable<string> SteamLibraries()
    {
        var bases = new List<string>();
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string sp) bases.Add(sp.Replace('/', '\\'));
        }
        catch { }
        bases.AddRange(new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam", @"D:\Steam", @"D:\SteamLibrary", @"E:\SteamLibrary" });
        var libs = new List<string>();
        foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(b)) continue;
            libs.Add(b);
            var vdf = Path.Combine(b, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(p)) libs.Add(p);
            }
        }
        return libs.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static GameInfo Detect()
    {
        var cfg = LoadConfig();
        var gi = new GameInfo { Hl2Root = cfg.Hl2Root, RtxRoot = cfg.Hl2RtxRoot };
        foreach (var lib in SteamLibraries())
        {
            var common = Path.Combine(lib, "steamapps", "common");
            if (gi.Hl2Root == null && File.Exists(Path.Combine(common, "Half-Life 2", "hl2.exe")))
                gi.Hl2Root = Path.Combine(common, "Half-Life 2");
            if (gi.RtxRoot == null)
                foreach (var n in new[] { "Half-Life 2 RTX", "Half-Life 2 RTX Demo", "HL2RTX" })
                    if (Directory.Exists(Path.Combine(common, n))) gi.RtxRoot = Path.Combine(common, n);
        }
        if (gi.Hl2Root != null)
        {
            gi.GameDir = Path.Combine(gi.Hl2Root, "hl2");
            gi.Bin = Path.Combine(gi.Hl2Root, "bin");
            gi.RemixInHl2 = Directory.Exists(Path.Combine(gi.Hl2Root, "bin", ".trex")) || Directory.Exists(Path.Combine(gi.Hl2Root, ".trex"));
        }
        if (gi.RtxRoot != null && Directory.Exists(gi.RtxRoot))
        {
            foreach (var exe in new[] { "hl2.exe", "hl2rtx.exe", "hl2_win64.exe" })
            {
                var hit = Directory.EnumerateFiles(gi.RtxRoot, exe, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 }).FirstOrDefault();
                if (hit != null) { gi.RtxExe = hit; break; }
            }
            foreach (var g in new[] { "hl2rtx", "hl2" })
                if (File.Exists(Path.Combine(gi.RtxRoot, g, "gameinfo.txt"))) { gi.RtxGame = g; break; }
        }
        return gi;
    }

    public static string SafeName(string s, string def = "mymap")
    {
        s = Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9_]", "_").Trim('_');
        return s.Length == 0 ? def : s;
    }

    // ------------------------------------------------------------------ запуск процессов

    /// <summary>Запускает инструмент, построчно отдаёт вывод; прогресс vvis/vrad «0...1...2...» → 0..1.</summary>
    static int Run(string exe, string args, string cwd, Action<string> log, Action<double> progress, CancellationToken ct)
    {
        log($"> \"{exe}\" {args}");
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Latin1, StandardErrorEncoding = Encoding.Latin1,
        };
        using var p = Process.Start(psi);
        var errTask = Task.Run(() => { string l; while ((l = p.StandardError.ReadLine()) != null) if (l.Trim().Length > 0) log(l); });
        var sb = new StringBuilder();
        int ch;
        while ((ch = p.StandardOutput.Read()) >= 0)
        {
            if (ct.IsCancellationRequested) { try { p.Kill(true); } catch { } return -1; }
            if (ch == '\n' || ch == '\r')
            {
                if (sb.ToString().Trim().Length > 0) log(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append((char)ch);
                if (ch == '.' && sb.Length >= 4 && sb.ToString().EndsWith("..."))
                {
                    var ms = Regex.Matches(sb.ToString(), @"(\d+)\.\.\.");
                    if (ms.Count > 0) progress?.Invoke(Math.Min(1, int.Parse(ms[^1].Groups[1].Value) / 10.0));
                }
            }
        }
        if (sb.ToString().Trim().Length > 0) log(sb.ToString());
        p.WaitForExit();
        errTask.Wait(1000);
        return p.ExitCode;
    }

    public sealed class BuildOptions
    {
        public bool Final, Rtx, Launch;
    }

    /// <summary>Полная сборка: VMF → VBSP → VVIS → VRAD → копирование в maps. Возвращает имя карты.</summary>
    public static string BuildMap(Scene scene, BuildOptions opt, Action<string> log, Action<string, double> stage, CancellationToken ct)
    {
        var gi = Detect();
        if (!gi.Ok) throw new Exception("Half-Life 2 не найдена. Укажите папку игры в меню «Настройки».");
        string name = SafeName(scene.Name);
        Directory.CreateDirectory(WorkDir);
        string vmf = Path.Combine(WorkDir, name + ".vmf"), bsp = Path.Combine(WorkDir, name + ".bsp"), noExt = vmf[..^4];
        stage("Генерация VMF", 0.02);
        File.WriteAllText(vmf, VmfWriter.Build(scene));
        if (File.Exists(bsp)) File.Delete(bsp);
        log("VMF записан: " + vmf);
        bool leak = false;
        void LogLeak(string l) { if (l.Contains("LEAKED")) leak = true; log(l); }
        string game = $"-game \"{gi.GameDir}\"";

        stage("VBSP — геометрия", 0.05);
        int code = Run(Path.Combine(gi.Bin, "vbsp.exe"), $"{game} \"{vmf}\"", gi.Bin, LogLeak, p => stage(null, 0.05 + 0.2 * p), ct);
        if (ct.IsCancellationRequested) throw new OperationCanceledException();
        if (code != 0 || !File.Exists(bsp) || leak) throw new Exception(leak ? "Карта не запечатана (leak)." : "VBSP завершился с ошибкой — смотрите лог.");

        stage("VVIS — видимость", 0.25);
        string fast = opt.Final && !opt.Rtx ? "" : "-fast ";
        code = Run(Path.Combine(gi.Bin, "vvis.exe"), $"{game} {fast}\"{noExt}\"", gi.Bin, log, p => stage(null, 0.25 + 0.25 * p), ct);
        if (ct.IsCancellationRequested) throw new OperationCanceledException();
        if (code != 0) throw new Exception("VVIS завершился с ошибкой.");

        stage("VRAD — освещение", 0.5);
        // С RTX Remix освещение считается трассировкой лучей в реальном времени, поэтому запекаем быстро.
        string vradArgs = opt.Rtx ? "-fast -ldr" : opt.Final ? "-both -final -StaticPropLighting -StaticPropPolys" : "-both -fast";
        code = Run(Path.Combine(gi.Bin, "vrad.exe"), $"{game} {vradArgs} \"{noExt}\"", gi.Bin, log, p => stage(null, 0.5 + 0.45 * p), ct);
        if (ct.IsCancellationRequested) throw new OperationCanceledException();
        if (code != 0) throw new Exception("VRAD завершился с ошибкой.");

        stage("Копирование карты", 0.97);
        var targets = new List<string> { Path.Combine(gi.GameDir, "maps") };
        if (opt.Rtx && gi.RtxRoot != null && gi.RtxGame != null) targets.Add(Path.Combine(gi.RtxRoot, gi.RtxGame, "maps"));
        foreach (var tdir in targets)
        {
            Directory.CreateDirectory(tdir);
            File.Copy(bsp, Path.Combine(tdir, name + ".bsp"), true);
            log("Карта скопирована: " + Path.Combine(tdir, name + ".bsp"));
        }
        stage("Готово", 1);
        if (opt.Launch) Launch(name, opt.Rtx, log);
        return name;
    }

    public static void Launch(string map, bool rtx, Action<string> log)
    {
        var gi = Detect();
        map = SafeName(map);
        ProcessStartInfo psi;
        if (rtx && gi.RtxExe != null)
        {
            psi = new ProcessStartInfo(gi.RtxExe, $"-game {gi.RtxGame ?? "hl2"} -novid +map {map}") { WorkingDirectory = Path.GetDirectoryName(gi.RtxExe) };
            log?.Invoke("Запуск Half-Life 2 RTX. При первом запуске RTX Remix сам компилирует шейдеры — это может занять несколько минут.");
        }
        else
        {
            psi = new ProcessStartInfo(Path.Combine(gi.Hl2Root, "hl2.exe"), $"-game hl2 -novid -steam +sv_cheats 1 +map {map}") { WorkingDirectory = gi.Hl2Root };
            if (rtx && gi.RemixInHl2) log?.Invoke("Запуск HL2 с RTX Remix. Шейдеры Remix компилируются при первом запуске.");
            else log?.Invoke("Запуск Half-Life 2...");
        }
        psi.UseShellExecute = false;
        Process.Start(psi);
    }

    // ------------------------------------------------------------------ импорт моделей

    public sealed class ImportRequest
    {
        public string Name;
        public List<float> Tris = new();   // [x y z nx ny nz u v] * 3 на треугольник, координаты Source
        public Bitmap Texture;
        public Color FallbackColor = Color.LightGray;
        public bool Physics;
        public float Mass = 30;
        public string Surface = "default";
    }

    static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    static void WriteSmd(string path, List<float> tris, string mat)
    {
        var sb = new StringBuilder("version 1\nnodes\n0 \"root\" -1\nend\nskeleton\ntime 0\n0 0 0 0 0 0 0\nend\ntriangles\n");
        for (int i = 0; i + 23 < tris.Count; i += 24)
        {
            sb.Append(mat).Append('\n');
            for (int k = 0; k < 3; k++)
            {
                int o = i + k * 8;
                sb.Append("0 ");
                for (int j = 0; j < 8; j++) { sb.Append(F(tris[o + j])); sb.Append(j < 7 ? ' ' : '\n'); }
            }
        }
        sb.Append("end\n");
        File.WriteAllText(path, sb.ToString());
    }

    static List<float> BoxTris(float[] mn, float[] mx)
    {
        float[][] c =
        {
            new[] { mn[0], mn[1], mn[2] }, new[] { mx[0], mn[1], mn[2] }, new[] { mx[0], mx[1], mn[2] }, new[] { mn[0], mx[1], mn[2] },
            new[] { mn[0], mn[1], mx[2] }, new[] { mx[0], mn[1], mx[2] }, new[] { mx[0], mx[1], mx[2] }, new[] { mn[0], mx[1], mx[2] },
        };
        int[][] q = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 1, 2, 6, 5 }, new[] { 3, 0, 4, 7 } };
        float[][] n = { new[] { 0f, 0, -1 }, new[] { 0f, 0, 1 }, new[] { 0f, -1, 0 }, new[] { 0f, 1, 0 }, new[] { 1f, 0, 0 }, new[] { -1f, 0, 0 } };
        var t = new List<float>();
        for (int f = 0; f < 6; f++)
            foreach (var tri in new[] { new[] { q[f][0], q[f][1], q[f][2] }, new[] { q[f][0], q[f][2], q[f][3] } })
                foreach (var vi in tri) { t.AddRange(c[vi]); t.AddRange(n[f]); t.Add(0); t.Add(0); }
        return t;
    }

    /// <summary>Пишет VTF/VMT/SMD/QC и компилирует модель studiomdl. Возвращает путь models/... .mdl</summary>
    public static string ImportModel(ImportRequest r, Action<string> log, CancellationToken ct)
    {
        var gi = Detect();
        if (!gi.Ok) throw new Exception("Half-Life 2 не найдена.");
        if (r.Tris.Count < 24) throw new Exception("В модели нет треугольников.");
        string name = SafeName(r.Name, "custom_model");
        string folder = Path.Combine(WorkDir, "models", name);
        Directory.CreateDirectory(folder);
        string matDir = Path.Combine(gi.GameDir, "materials", "models", "hl2toolkit");
        Directory.CreateDirectory(matDir);
        using (var tex = r.Texture != null ? new Bitmap(r.Texture) : new Bitmap(16, 16))
        {
            if (r.Texture == null) using (var g = Graphics.FromImage(tex)) g.Clear(r.FallbackColor);
            Vtf.Write(Path.Combine(matDir, name + ".vtf"), tex);
        }
        File.WriteAllText(Path.Combine(matDir, name + ".vmt"),
            $"\"VertexLitGeneric\"\n{{\n\t\"$basetexture\" \"models/hl2toolkit/{name}\"\n\t\"$surfaceprop\" \"{r.Surface}\"\n}}\n");
        log($"Материал: materials/models/hl2toolkit/{name}.vmt");

        float[] mn = { float.MaxValue, float.MaxValue, float.MaxValue }, mx = { float.MinValue, float.MinValue, float.MinValue };
        for (int i = 0; i < r.Tris.Count; i += 8)
            for (int k = 0; k < 3; k++) { mn[k] = Math.Min(mn[k], r.Tris[i + k]); mx[k] = Math.Max(mx[k], r.Tris[i + k]); }
        WriteSmd(Path.Combine(folder, "ref.smd"), r.Tris, name);
        WriteSmd(Path.Combine(folder, "phys.smd"), BoxTris(mn, mx), "phys");
        log($"Треугольников: {r.Tris.Count / 24}, высота {mx[2] - mn[2]:0} юнитов");

        var qc = new StringBuilder();
        qc.Append($"$modelname \"hl2toolkit/{name}.mdl\"\n$cdmaterials \"models/hl2toolkit\"\n$body body \"ref.smd\"\n$surfaceprop \"{r.Surface}\"\n");
        if (!r.Physics) qc.Append("$staticprop\n");
        qc.Append("$sequence idle \"ref.smd\" loop fps 1\n");
        qc.Append($"$collisionmodel \"phys.smd\"\n{{\n\t$mass {F(r.Mass)}\n}}\n");
        string qcPath = Path.Combine(folder, name + ".qc");
        File.WriteAllText(qcPath, qc.ToString());
        int code = Run(Path.Combine(gi.Bin, "studiomdl.exe"), $"-game \"{gi.GameDir}\" -nop4 \"{qcPath}\"", folder, log, null, ct);
        string mdl = Path.Combine(gi.GameDir, "models", "hl2toolkit", name + ".mdl");
        if (code != 0 || !File.Exists(mdl)) throw new Exception("studiomdl не смог собрать модель — смотрите лог.");
        log("Модель готова: models/hl2toolkit/" + name + ".mdl");
        return $"models/hl2toolkit/{name}.mdl";
    }
}
