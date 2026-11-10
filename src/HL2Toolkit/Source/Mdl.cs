using System.Text;

namespace HL2Toolkit.Source;

public sealed class MeshGroup
{
    public int Start, Count;
    public string Material = "";
}

/// <summary>Геометрия модели для превью (тело 0, LOD 0), координаты Source.</summary>
public sealed class MeshData
{
    public float[] Positions = Array.Empty<float>();
    public float[] Normals = Array.Empty<float>();
    public float[] Uvs = Array.Empty<float>();
    public int[] Indices = Array.Empty<int>();
    public List<MeshGroup> Groups = new();
    public float[] Mins = new float[3], Maxs = new float[3];
    public bool IsStaticProp, PhysicsOnly, HasPhysics;
    /// <summary>Рекомендуемый класс: prop_static / prop_physics / prop_dynamic.</summary>
    public string SuggestedClass = "prop_static";
}

public static class Mdl
{
    static int I(byte[] b, int o) => BitConverter.ToInt32(b, o);
    static float F(byte[] b, int o) => BitConverter.ToSingle(b, o);

    public static MeshData Load(GameFS fs, string mdlPath, int maxTris = 150000)
    {
        mdlPath = GameFS.Norm(mdlPath);
        string bas = mdlPath[..^4];
        var mdl = fs.Read(mdlPath);
        if (mdl == null || mdl.Length < 240) return null;
        var m = new MeshData();

        // габариты
        float[] hull = new float[6], view = new float[6];
        for (int i = 0; i < 6; i++) { hull[i] = F(mdl, 104 + i * 4); view[i] = F(mdl, 128 + i * 4); }
        var bb = view.Any(v => Math.Abs(v) > 1e-4f) ? view : hull;
        m.Mins = bb[..3]; m.Maxs = bb[3..];
        int flags = I(mdl, 152);
        m.IsStaticProp = (flags & 0x10) != 0;
        string low = Encoding.Latin1.GetString(mdl).ToLowerInvariant();
        m.PhysicsOnly = low.Contains("prop_data") && !low.Contains("allowstatic");
        m.HasPhysics = fs.Exists(bas + ".phy");
        m.SuggestedClass = m.PhysicsOnly ? "prop_physics" : m.IsStaticProp ? "prop_static" : m.HasPhysics ? "prop_physics" : "prop_dynamic";

        var vvd = fs.Read(bas + ".vvd");
        var vtx = fs.Read(bas + ".dx90.vtx") ?? fs.Read(bas + ".dx80.vtx") ?? fs.Read(bas + ".sw.vtx");
        if (vvd == null || vtx == null) return m;

        // --- VVD: вершины LOD0 с учётом fixup-таблицы
        int lod0 = I(vvd, 16);
        int numFix = I(vvd, 48), fixStart = I(vvd, 52), vdata = I(vvd, 56);
        var order = new List<int>();
        if (numFix > 0)
        {
            for (int k = 0; k < numFix; k++)
            {
                int lod = I(vvd, fixStart + k * 12), src = I(vvd, fixStart + k * 12 + 4), n = I(vvd, fixStart + k * 12 + 8);
                if (lod >= 0) for (int i = 0; i < n; i++) order.Add(src + i);
            }
        }
        else for (int i = 0; i < lod0; i++) order.Add(i);

        // --- материалы с учётом таблицы скинов
        string CStr(int o) { int e = Array.IndexOf(mdl, (byte)0, o); return Encoding.Latin1.GetString(mdl, o, Math.Max(0, e - o)); }
        int ntex = I(mdl, 204), texIdx = I(mdl, 208), ncd = I(mdl, 212), cdIdx = I(mdl, 216);
        int nskinref = I(mdl, 220), nskinfam = I(mdl, 224), skinIdx = I(mdl, 228);
        var texNames = new List<string>();
        for (int i = 0; i < ntex; i++) texNames.Add(CStr(texIdx + i * 64 + I(mdl, texIdx + i * 64)));
        var cdDirs = new List<string>();
        for (int i = 0; i < ncd; i++) cdDirs.Add(CStr(I(mdl, cdIdx + i * 4)));
        string Material(int mi)
        {
            if (nskinref > 0 && nskinfam > 0 && mi < nskinref) mi = BitConverter.ToInt16(mdl, skinIdx + mi * 2);
            if (mi < 0 || mi >= texNames.Count) return "";
            string t = GameFS.Norm(texNames[mi]);
            foreach (var d in cdDirs)
            {
                string p = (GameFS.Norm(d).Trim('/') + "/" + t).Trim('/');
                if (fs.Exists("materials/" + p + ".vmt")) return p;
            }
            return t;
        }

        var pos = new List<float>(); var nrm = new List<float>(); var uv = new List<float>(); var idx = new List<int>();
        var remap = new Dictionary<int, int>();
        int AddVertex(int gi)
        {
            if (remap.TryGetValue(gi, out int r)) return r;
            int vi = order[Math.Clamp(gi, 0, order.Count - 1)];
            int o = vdata + vi * 48 + 16;
            r = pos.Count / 3;
            pos.Add(F(vvd, o)); pos.Add(F(vvd, o + 4)); pos.Add(F(vvd, o + 8));
            nrm.Add(F(vvd, o + 12)); nrm.Add(F(vvd, o + 16)); nrm.Add(F(vvd, o + 20));
            uv.Add(F(vvd, o + 24)); uv.Add(F(vvd, o + 28));
            remap[gi] = r;
            return r;
        }

        int numBp = I(mdl, 232), bpIdx = I(mdl, 236);
        int vNumBp = I(vtx, 28), vBpOff = I(vtx, 32);
        for (int b = 0; b < Math.Min(numBp, vNumBp); b++)
        {
            int bp = bpIdx + b * 16;
            int nmodels = I(mdl, bp + 4), modelIdx = I(mdl, bp + 12);
            int vbp = vBpOff + b * 8;
            int vModelOff = I(vtx, vbp + 4);
            if (nmodels == 0) continue;
            int mo = bp + modelIdx;                        // первая модель тела
            int nmeshes = I(mdl, mo + 72), meshIdx = I(mdl, mo + 76), vertIdx = I(mdl, mo + 84);
            int vm = vbp + vModelOff;
            int vLodOff = I(vtx, vm + 4);
            int vl = vm + vLodOff;
            int vNMeshes = I(vtx, vl), vMeshOff = I(vtx, vl + 4);
            int modelBase = vertIdx / 48;
            for (int me = 0; me < Math.Min(nmeshes, vNMeshes); me++)
            {
                int mesh = mo + meshIdx + me * 116;
                int meshMat = I(mdl, mesh), meshVOff = I(mdl, mesh + 12);
                int vme = vl + vMeshOff + me * 9;
                int nsg = I(vtx, vme), sgOff = I(vtx, vme + 4);
                int gStart = idx.Count;
                for (int s = 0; s < nsg; s++)
                {
                    int sg = vme + sgOff + s * 25;
                    int nv = I(vtx, sg), voff = I(vtx, sg + 4), ni = I(vtx, sg + 8), ioff = I(vtx, sg + 12);
                    var ids = new int[nv];
                    for (int k = 0; k < nv; k++) ids[k] = BitConverter.ToUInt16(vtx, sg + voff + k * 9 + 4);
                    for (int t = 0; t + 2 < ni; t += 3)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            int li = BitConverter.ToUInt16(vtx, sg + ioff + (t + c) * 2);
                            idx.Add(AddVertex(modelBase + meshVOff + ids[li]));
                        }
                        if (idx.Count / 3 >= maxTris) break;
                    }
                }
                if (idx.Count > gStart) m.Groups.Add(new MeshGroup { Start = gStart, Count = idx.Count - gStart, Material = Material(meshMat) });
            }
        }
        m.Positions = pos.ToArray(); m.Normals = nrm.ToArray(); m.Uvs = uv.ToArray(); m.Indices = idx.ToArray();
        return m;
    }
}
