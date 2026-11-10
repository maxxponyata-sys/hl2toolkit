using HL2Toolkit.Build;
using HL2Toolkit.Source;

namespace HL2Toolkit;

/// <summary>Проверка ядра без интерфейса: чтение моделей, сборка тестовой карты, импорт OBJ.</summary>
static class SelfTest
{
    public static int Run(string[] args)
    {
        var log = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "selftest.log")) { AutoFlush = true };
        void L(string s) { log.WriteLine(s); }
        try
        {
            var gi = Toolchain.Detect();
            L("HL2: " + gi.Hl2Root + " ok=" + gi.Ok + " rtx=" + gi.RtxAvailable);
            var fs = new GameFS(gi.GameDir);
            foreach (var p in new[] { "models/props_c17/oildrum001.mdl", "models/props_junk/wood_crate001a.mdl", "models/combine_soldier.mdl" })
            {
                var m = Mdl.Load(fs, p);
                L($"{p}: verts={m.Positions.Length / 3} tris={m.Indices.Length / 3} cls={m.SuggestedClass} mat={m.Groups.FirstOrDefault()?.Material}");
            }
            var img = Vtf.ReadPreview(fs.Read("materials/brick/brickwall001a.vtf"), 128);
            L($"vtf: {img?.W}x{img?.H} src {img?.SourceW}x{img?.SourceH}");

            if (args.Contains("--import"))
            {
                string objPath = Path.Combine(Toolchain.WorkDir, "selftest_cube.obj");
                Directory.CreateDirectory(Toolchain.WorkDir);
                File.WriteAllText(objPath, "v -1 0 -1\nv 1 0 -1\nv 1 2 -1\nv -1 2 -1\nv -1 0 1\nv 1 0 1\nv 1 2 1\nv -1 2 1\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\n" +
                    "f 1/1 2/2 3/3 4/4\nf 5/1 8/4 7/3 6/2\nf 1/1 5/2 6/3 2/4\nf 2/1 6/2 7/3 3/4\nf 3/1 7/2 8/3 4/4\nf 4/1 8/2 5/3 1/4\n");
                var obj = ObjModel.Load(objPath);
                var req = new Toolchain.ImportRequest { Name = "selftest_cube", Tris = obj.ToSource(48, false, 0) };
                var mdl = Toolchain.ImportModel(req, L, CancellationToken.None);
                var mm = Mdl.Load(new GameFS(gi.GameDir), mdl);
                L($"imported {mdl}: tris={mm?.Indices.Length / 3} h={mm?.Maxs[2] - mm?.Mins[2]}");
            }

            if (args.Contains("--bigimport"))
            {
                // ~56 000 треугольников с уникальными UV в каждом углу — как тяжёлая модель автобуса
                var rnd = new Random(1);
                var tris = new List<float>();
                int nx = 160, ny = 175;
                // «плоские» нормали (своя у каждого угла), но общие UV — типичный экспорт тяжёлой модели
                void V(int px, int py) => tris.AddRange(new float[] { px, py, MathF.Sin(px * 0.3f) * 4 + 4, rnd.NextSingle() - 0.5f, rnd.NextSingle() - 0.5f, 1, px / (float)nx, py / (float)ny });
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        V(x, y); V(x + 1, y); V(x + 1, y + 1);
                        V(x, y); V(x + 1, y + 1); V(x, y + 1);
                    }
                var big = Toolchain.ImportModel(new Toolchain.ImportRequest { Name = "selftest_big", Tris = tris }, L, CancellationToken.None);
                var bm = Mdl.Load(new GameFS(gi.GameDir), big);
                L($"BIG imported {big}: tris={bm?.Indices.Length / 3} (source {tris.Count / 24})");
                // уникальные UV в каждом углу: сглаживание не поможет, нужна нарезка на части
                var tris2 = new List<float>();
                for (int i = 0; i < 30000 * 3; i++)
                    tris2.AddRange(new float[] { i % 300, i / 300 % 300, i % 7, 0, 0, 1, rnd.NextSingle(), rnd.NextSingle() });
                var big2 = Toolchain.ImportModel(new Toolchain.ImportRequest { Name = "selftest_big2", Tris = tris2 }, L, CancellationToken.None);
                var bm2 = Mdl.Load(new GameFS(gi.GameDir), big2);
                L($"BIG2 imported {big2}: tris={bm2?.Indices.Length / 3} (source {tris2.Count / 24})");
                return 0;
            }

            var scene = Scene.CreateDefault();
            scene.Name = "tk_selftest";
            var t = scene.Terrain;
            for (int y = 0; y < t.Res; y++) for (int x = 0; x < t.Res; x++)
                    t.Heights[y * t.Res + x] = 300 * MathF.Exp(-((x - t.Res * 0.75f) * (x - t.Res * 0.75f) + (y - t.Res * 0.25f) * (y - t.Res * 0.25f)) / 40f);
            scene.Env.Water = true; scene.Env.Fog = true;
            scene.Objects.Add(new SceneObject { Kind = ObjKind.Prop, Cls = "prop_physics", Model = "models/props_c17/oildrum001.mdl", Pos = new float[] { 128, 0, 0 }, Scale = 2 });
            scene.Objects.Add(new SceneObject { Kind = ObjKind.Prop, Cls = "prop_static", Model = "models/props_c17/fence01a.mdl", Pos = new float[] { 0, 256, 0 }, Ang = new float[] { 0, 30, 0 }, Scale = 1.5f });
            scene.Objects.Add(new SceneObject { Kind = ObjKind.Block, Material = "brick/brickwall001a", Pos = new float[] { -300, -300, 0 }, Size = new float[] { 256, 128, 192 }, Ang = new float[] { 0, 30, 0 } });
            scene.Objects.Add(new SceneObject { Kind = ObjKind.Entity, Cls = "npc_combine_s", Pos = new float[] { 400, 400, 8 }, Keys = new() { ["additionalequipment"] = "weapon_smg1" } });
            scene.Objects.Add(new SceneObject { Kind = ObjKind.Light, Cls = "light", Pos = new float[] { 0, 0, 128 } });
            var name = Toolchain.BuildMap(scene, new Toolchain.BuildOptions(), L, (s, p) => { if (s != null) L("## " + s); }, CancellationToken.None);
            L("BUILD OK " + name);
            return 0;
        }
        catch (Exception ex) { L("FAIL " + ex); return 1; }
        finally { log.Dispose(); }
    }
}
