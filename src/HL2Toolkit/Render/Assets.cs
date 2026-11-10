using System.Collections.Concurrent;
using HL2Toolkit.Source;

namespace HL2Toolkit.Render;

public sealed class GpuTexture
{
    public uint Id;
    public int W, H;          // реальный размер VTF — для масштаба мировых UV
    public bool Alpha;
}

/// <summary>Фоновая загрузка моделей и текстур из игры; загрузка в GPU — только из потока отрисовки.</summary>
public sealed class Assets
{
    public GameFS FS { get; private set; }
    public event Action Loaded;

    readonly ConcurrentDictionary<string, MeshData> _meshes = new();
    readonly ConcurrentDictionary<string, byte> _meshPending = new();
    readonly ConcurrentDictionary<string, (RgbaImage img, bool alpha)> _images = new();
    readonly ConcurrentDictionary<string, byte> _imgPending = new();
    readonly Dictionary<string, GpuTexture> _gpuTex = new();
    readonly Dictionary<string, uint> _gpuModels = new();
    static readonly MeshData Failed = new();
    static readonly RgbaImage FailedImg = new();

    public void SetGame(string gameDir)
    {
        FS = gameDir != null && Directory.Exists(gameDir) ? new GameFS(gameDir) : null;
        _meshes.Clear(); _images.Clear();
    }

    /// <summary>Сбросить кэш одной модели (после переимпорта).</summary>
    public void Invalidate(string model)
    {
        model = GameFS.Norm(model);
        _meshes.TryRemove(model, out _);
        _invalidated.Enqueue(model);
    }
    readonly ConcurrentQueue<string> _invalidated = new();

    public MeshData GetMesh(string path)
    {
        if (FS == null || string.IsNullOrEmpty(path)) return null;
        path = GameFS.Norm(path);
        if (_meshes.TryGetValue(path, out var m)) return ReferenceEquals(m, Failed) ? null : m;
        if (_meshPending.TryAdd(path, 0))
            Task.Run(() =>
            {
                MeshData r = null;
                try { r = Mdl.Load(FS, path); } catch { }
                _meshes[path] = r ?? Failed;
                _meshPending.TryRemove(path, out _);
                if (r != null) foreach (var g in r.Groups) RequestImage(g.Material, 1);
                Loaded?.Invoke();
            });
        return null;
    }

    public bool MeshFailed(string path) => _meshes.TryGetValue(GameFS.Norm(path ?? ""), out var m) && ReferenceEquals(m, Failed);

    void RequestImage(string material, int layer)
    {
        string key = material + "|" + layer;
        if (FS == null || _images.ContainsKey(key) || !_imgPending.TryAdd(key, 0)) return;
        Task.Run(() =>
        {
            RgbaImage img = null; bool alpha = false;
            try
            {
                string tex = material;
                var vmt = FS.ReadVmt(material);
                if (vmt != null)
                {
                    tex = layer == 2 && vmt.TryGetValue("$basetexture2", out var t2) ? t2 : vmt.GetValueOrDefault("$basetexture", material);
                    alpha = vmt.ContainsKey("$alphatest") || vmt.ContainsKey("$translucent");
                }
                img = Vtf.ReadPreview(FS.Read("materials/" + GameFS.Norm(tex) + ".vtf"), 256);
            }
            catch { }
            _images[key] = (img ?? FailedImg, alpha);
            _imgPending.TryRemove(key, out _);
            Loaded?.Invoke();
        });
    }

    /// <summary>GL-поток: текстура материала (null пока грузится или если не найдена).</summary>
    public GpuTexture Texture(string material, int layer = 1, bool repeat = true)
    {
        if (string.IsNullOrEmpty(material)) return null;
        string key = material + "|" + layer;
        if (_gpuTex.TryGetValue(key, out var t)) return t;
        if (!_images.TryGetValue(key, out var im)) { RequestImage(material, layer); return null; }
        if (ReferenceEquals(im.img, FailedImg)) { _gpuTex[key] = null; return null; }
        t = new GpuTexture { Id = GL.UploadTexture(im.img.W, im.img.H, im.img.Pixels, repeat), W = im.img.SourceW, H = im.img.SourceH, Alpha = im.alpha };
        _gpuTex[key] = t;
        return t;
    }

    public bool TextureReady(string material, int layer = 1)
    {
        string key = material + "|" + layer;
        return _gpuTex.ContainsKey(key) || _images.ContainsKey(key);
    }

    /// <summary>GL-поток: display list модели (0 — ещё не готова).</summary>
    public uint ModelList(string path)
    {
        while (_invalidated.TryDequeue(out var inv))
            if (_gpuModels.Remove(inv, out var old)) GL.glDeleteLists(old, 1);
        path = GameFS.Norm(path ?? "");
        if (_gpuModels.TryGetValue(path, out var list)) return list;
        var mesh = GetMesh(path);
        if (mesh == null) return 0;
        foreach (var g in mesh.Groups) if (!TextureReady(g.Material)) { Texture(g.Material); return 0; }
        list = GL.glGenLists(1);
        GL.glNewList(list, GL.COMPILE);
        foreach (var g in mesh.Groups)
        {
            var tex = Texture(g.Material);
            if (tex != null) { GL.glEnable(GL.TEXTURE_2D); GL.glBindTexture(GL.TEXTURE_2D, tex.Id); }
            else GL.glDisable(GL.TEXTURE_2D);
            if (tex?.Alpha == true) { GL.glEnable(GL.ALPHA_TEST); GL.glAlphaFunc(GL.GREATER, 0.5f); }
            GL.glBegin(GL.TRIANGLES);
            for (int i = g.Start; i < g.Start + g.Count; i++)
            {
                int v = mesh.Indices[i];
                GL.glNormal3f(mesh.Normals[v * 3], mesh.Normals[v * 3 + 1], mesh.Normals[v * 3 + 2]);
                GL.glTexCoord2f(mesh.Uvs[v * 2], mesh.Uvs[v * 2 + 1]);
                GL.glVertex3f(mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]);
            }
            GL.glEnd();
            GL.glDisable(GL.ALPHA_TEST);
        }
        GL.glDisable(GL.TEXTURE_2D);
        GL.glEndList();
        _gpuModels[path] = list;
        return list;
    }
}
