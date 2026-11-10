using System.Drawing.Imaging;
using System.Numerics;
using HL2Toolkit.Source;

namespace HL2Toolkit.Render;

/// <summary>Данные, которые перетаскивают из библиотеки во вьюпорт.</summary>
public static class DragPayload
{
    public const string Marker = "HL2TK_OBJECT";
    /// <summary>Шаблон объекта, который сейчас перетаскивают из библиотеки.</summary>
    public static SceneObject Current;
}

/// <summary>3D-окно редактора на OpenGL. Координаты Source: Z вверх.</summary>
public sealed class Viewport : Control
{
    readonly EditorState _ed;
    public readonly Assets Assets;
    IntPtr _hdc, _ctx;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    DateTime _last = DateTime.Now;

    // камера
    public Vector3 CamPos = new(-1400, -1400, 900);
    public float CamYaw = 45, CamPitch = -25;
    float _speed = 900;
    Matrix4x4 _view, _proj;

    readonly HashSet<Keys> _keys = new();
    bool _rmb, _lmb, _mmb;
    Point _mouse, _downMouse;

    // рельеф
    uint[] _tileLists = Array.Empty<uint>();
    bool[] _tileDirty = Array.Empty<bool>();
    Vector3? _brushHit;
    float _flattenTarget;

    // перетаскивание объектов / гизмо
    enum Drag { None, Free, AxisX, AxisY, AxisZ, Ring, ScaleX, ScaleY, ScaleZ, Terrain }
    Drag _drag;
    float[] _dragStartPos, _dragStartSize;
    float _dragStartYaw, _dragStartScale;
    Vector3 _grabOffset;
    float _grabHeight;
    Drag _hover;
    SceneObject _ghost;            // объект, который сейчас перетаскивают из библиотеки

    readonly Queue<(string model, Action<Bitmap> done)> _thumbQueue = new();

    public event Action<string> Status;
    public event Action<string> FilesDropped;

    public Viewport(EditorState ed, Assets assets)
    {
        _ed = ed; Assets = assets;
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
        DoubleBuffered = false;
        AllowDrop = true;
        TabStop = true;
        _timer.Tick += (_, _) => Tick();
        _ed.TerrainChanged += full => { if (full) RebuildTerrainLists(); else MarkAllDirty(); };
        Assets.Loaded += () => { try { BeginInvoke(new Action(() => { MarkAllDirty(); Invalidate(); })); } catch { } };
    }

    // ------------------------------------------------------------------ GL контекст
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hdc = GL.GetDC(Handle);
        var pfd = new GL.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<GL.PIXELFORMATDESCRIPTOR>(), nVersion = 1,
            dwFlags = 0x4 | 0x20 | 0x1, iPixelType = 0, cColorBits = 32, cAlphaBits = 8, cDepthBits = 24, cStencilBits = 8,
        };
        int fmt = GL.ChoosePixelFormat(_hdc, ref pfd);
        GL.SetPixelFormat(_hdc, fmt, ref pfd);
        _ctx = GL.wglCreateContext(_hdc);
        GL.wglMakeCurrent(_hdc, _ctx);
        _timer.Start();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _timer.Stop();
        if (_ctx != IntPtr.Zero) { GL.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); GL.wglDeleteContext(_ctx); }
        GL.ReleaseDC(Handle, _hdc);
        base.OnHandleDestroyed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    public void RequestThumbnail(string model, Action<Bitmap> done) => _thumbQueue.Enqueue((model, done));

    // ------------------------------------------------------------------ цикл
    void Tick()
    {
        var now = DateTime.Now;
        float dt = (float)Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        if (_rmb || (_keys.Count > 0 && Focused && !_lmb))
        {
            var fwd = Forward(); var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitZ));
            var mv = Vector3.Zero;
            if (_rmb)
            {
                if (_keys.Contains(Keys.W)) mv += fwd;
                if (_keys.Contains(Keys.S)) mv -= fwd;
                if (_keys.Contains(Keys.D)) mv += right;
                if (_keys.Contains(Keys.A)) mv -= right;
                if (_keys.Contains(Keys.E)) mv += Vector3.UnitZ;
                if (_keys.Contains(Keys.Q)) mv -= Vector3.UnitZ;
            }
            if (_keys.Contains(Keys.Up)) mv += fwd;
            if (_keys.Contains(Keys.Down)) mv -= fwd;
            if (_keys.Contains(Keys.Right)) mv += right;
            if (_keys.Contains(Keys.Left)) mv -= right;
            float sp = _speed * (ModifierKeys.HasFlag(Keys.Shift) ? 3 : 1) * (ModifierKeys.HasFlag(Keys.Control) && _rmb ? 0.25f : 1);
            if (mv != Vector3.Zero) CamPos += Vector3.Normalize(mv) * sp * dt;
        }
        if (_drag == Drag.Terrain && _lmb) ApplyBrush(dt);
        Invalidate();
    }

    Vector3 Forward()
    {
        float y = CamYaw * MathF.PI / 180, p = CamPitch * MathF.PI / 180;
        return new Vector3(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p));
    }

    void SetupMatrices(int w, int h)
    {
        _view = Matrix4x4.CreateLookAt(CamPos, CamPos + Forward(), Vector3.UnitZ);
        _proj = Matrix4x4.CreatePerspectiveFieldOfView(70 * MathF.PI / 180, Math.Max(1, w) / (float)Math.Max(1, h), 4, 65000);
    }

    (Vector3 o, Vector3 d) Ray(Point p)
    {
        Matrix4x4.Invert(_view * _proj, out var inv);
        float x = 2f * p.X / Math.Max(1, Width) - 1, y = 1 - 2f * p.Y / Math.Max(1, Height);
        var a = Vector4.Transform(new Vector4(x, y, 0, 1), inv);
        var b = Vector4.Transform(new Vector4(x, y, 1, 1), inv);
        var pa = new Vector3(a.X, a.Y, a.Z) / a.W; var pb = new Vector3(b.X, b.Y, b.Z) / b.W;
        return (pa, Vector3.Normalize(pb - pa));
    }

    PointF? Project(Vector3 p)
    {
        var c = Vector4.Transform(new Vector4(p, 1), _view * _proj);
        if (c.W <= 0.01f) return null;
        return new PointF((c.X / c.W * 0.5f + 0.5f) * Width, (1 - (c.Y / c.W * 0.5f + 0.5f)) * Height);
    }

    // ------------------------------------------------------------------ рисование
    protected override void OnPaint(PaintEventArgs e)
    {
        if (_ctx == IntPtr.Zero) return;
        GL.wglMakeCurrent(_hdc, _ctx);
        ProcessThumbnails();
        int w = Width, h = Height;
        GL.glViewport(0, 0, w, h);
        SetupMatrices(w, h);
        var env = _ed.Scene.Env;
        var fogC = env.Fog ? new[] { env.FogColor[0] / 255f, env.FogColor[1] / 255f, env.FogColor[2] / 255f } : new[] { 0.55f, 0.65f, 0.8f };
        GL.glClearColor(fogC[0], fogC[1], fogC[2], 1);
        GL.glClear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);
        GL.glMatrixMode(GL.PROJECTION); GL.glLoadMatrixf(GL.M(_proj));
        GL.glMatrixMode(GL.MODELVIEW); GL.glLoadMatrixf(GL.M(_view));

        DrawSky();
        GL.glEnable(GL.DEPTH_TEST);
        GL.glDepthFunc(GL.LEQUAL);
        SetupLighting();
        if (env.Fog)
        {
            GL.glEnable(GL.FOG); GL.glFogi(GL.FOG_MODE, GL.LINEAR);
            GL.glFogf(GL.FOG_START, env.FogStart); GL.glFogf(GL.FOG_END, env.FogEnd);
            GL.glFogfv(GL.FOG_COLOR, new[] { fogC[0], fogC[1], fogC[2], 1f });
        }
        DrawTerrain();
        foreach (var o in _ed.Scene.Objects) DrawObject(o, false);
        if (_ghost != null) DrawObject(_ghost, true);
        DrawWater();
        GL.glDisable(GL.FOG);
        GL.glDisable(GL.LIGHTING);
        if (_ed.Selected != null) { DrawSelection(_ed.Selected); DrawGizmo(_ed.Selected); }
        if (_ed.IsTerrainTool && _brushHit.HasValue) DrawBrush(_brushHit.Value);
        DrawCompass();
        GL.SwapBuffers(_hdc);
    }

    void SetupLighting()
    {
        var env = _ed.Scene.Env;
        var (pitch, sun, sunB, amb, ambB) = env.Sun();
        float p = pitch * MathF.PI / 180, y = env.SunYaw * MathF.PI / 180;
        // направление НА солнце
        var dir = new Vector3(-MathF.Cos(p) * MathF.Cos(y), -MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p));
        GL.glEnable(GL.LIGHTING); GL.glEnable(GL.LIGHT0); GL.glEnable(GL.NORMALIZE);
        GL.glEnable(GL.COLOR_MATERIAL); GL.glColorMaterial(GL.FRONT_AND_BACK, GL.AMBIENT_AND_DIFFUSE);
        GL.glLightfv(GL.LIGHT0, GL.POSITION, new[] { dir.X, dir.Y, dir.Z, 0f });
        float sb = Math.Clamp(sunB / 400f, 0.2f, 1.1f), ab = Math.Clamp(ambB / 160f, 0.2f, 0.8f);
        GL.glLightfv(GL.LIGHT0, GL.DIFFUSE, new[] { sun[0] / 255f * sb, sun[1] / 255f * sb, sun[2] / 255f * sb, 1f });
        GL.glLightfv(GL.LIGHT0, GL.AMBIENT, new[] { 0f, 0, 0, 1 });
        GL.glLightModelfv(GL.LIGHT_MODEL_AMBIENT, new[] { amb[0] / 255f * ab, amb[1] / 255f * ab, amb[2] / 255f * ab, 1f });
        GL.glShadeModel(GL.SMOOTH);
    }

    void DrawSky()
    {
        string sky = "skybox/" + _ed.Scene.Env.Sky;
        GL.glDisable(GL.DEPTH_TEST); GL.glDisable(GL.LIGHTING); GL.glDepthMask(0);
        GL.glPushMatrix();
        GL.glTranslatef(CamPos.X, CamPos.Y, CamPos.Z);
        const float s = 1000;
        // грань: суффикс, направление взгляда, «вправо»
        var faces = new (string suf, Vector3 dir, Vector3 right)[]
        {
            // проверено по стыкам картинок: rt → ft → lf → bk при повороте направо
            ("rt", Vector3.UnitX, -Vector3.UnitY), ("ft", -Vector3.UnitY, -Vector3.UnitX),
            ("lf", -Vector3.UnitX, Vector3.UnitY), ("bk", Vector3.UnitY, Vector3.UnitX),
        };
        GL.glEnable(GL.TEXTURE_2D);
        foreach (var (suf, dir, right) in faces)
        {
            var tex = Assets.Texture(sky + suf, 1, false);
            if (tex == null) { GL.glDisable(GL.TEXTURE_2D); GL.glColor3f(0.55f, 0.65f, 0.8f); }
            else { GL.glEnable(GL.TEXTURE_2D); GL.glBindTexture(GL.TEXTURE_2D, tex.Id); GL.glColor3f(1, 1, 1); }
            var c = dir * s;
            // верхняя половина грани — картинка, нижняя — растянутый последний ряд (как в Source, scale 1 2)
            GL.glBegin(GL.QUADS);
            V(c - right * s + Vector3.UnitZ * s, 0, 0); V(c + right * s + Vector3.UnitZ * s, 1, 0);
            V(c + right * s - Vector3.UnitZ * s, 1, 2); V(c - right * s - Vector3.UnitZ * s, 0, 2);
            GL.glEnd();
        }
        var up = Assets.Texture(sky + "up", 1, false);
        if (up != null) { GL.glEnable(GL.TEXTURE_2D); GL.glBindTexture(GL.TEXTURE_2D, up.Id); GL.glColor3f(1, 1, 1); }
        else { GL.glDisable(GL.TEXTURE_2D); GL.glColor3f(0.5f, 0.62f, 0.8f); }
        GL.glBegin(GL.QUADS);
        V(new(s, s, s), 0, 0); V(new(s, -s, s), 1, 0); V(new(-s, -s, s), 1, 1); V(new(-s, s, s), 0, 1);
        GL.glEnd();
        GL.glDisable(GL.TEXTURE_2D);
        GL.glPopMatrix();
        GL.glDepthMask(1);
        static void V(Vector3 p, float u, float v) { GL.glTexCoord2f(u, v); GL.glVertex3f(p.X, p.Y, p.Z); }
    }

    // ---------------------------------------------------------------- рельеф
    public void RebuildTerrainLists()
    {
        if (_ctx == IntPtr.Zero) return;
        GL.wglMakeCurrent(_hdc, _ctx);
        foreach (var l in _tileLists) if (l != 0) GL.glDeleteLists(l, 1);
        int n = _ed.Scene.Terrain.Tiles * _ed.Scene.Terrain.Tiles;
        _tileLists = new uint[n];
        _tileDirty = Enumerable.Repeat(true, n).ToArray();
    }

    void MarkAllDirty() { for (int i = 0; i < _tileDirty.Length; i++) _tileDirty[i] = true; }

    void MarkDirty(int gx0, int gy0, int gx1, int gy1)
    {
        var t = _ed.Scene.Terrain;
        int tx0 = Math.Max(0, (gx0 - 1) / Terrain.Seg), tx1 = Math.Min(t.Tiles - 1, gx1 / Terrain.Seg);
        int ty0 = Math.Max(0, (gy0 - 1) / Terrain.Seg), ty1 = Math.Min(t.Tiles - 1, gy1 / Terrain.Seg);
        for (int ty = ty0; ty <= ty1; ty++) for (int tx = tx0; tx <= tx1; tx++) _tileDirty[ty * t.Tiles + tx] = true;
    }

    Vector3 TerrainNormal(int gx, int gy)
    {
        var t = _ed.Scene.Terrain; int r = t.Res;
        float H(int x, int y) => t.Heights[Math.Clamp(y, 0, r - 1) * r + Math.Clamp(x, 0, r - 1)];
        var n = new Vector3(H(gx - 1, gy) - H(gx + 1, gy), H(gx, gy - 1) - H(gx, gy + 1), 2 * t.Step);
        return Vector3.Normalize(n);
    }

    void BuildTile(int tx, int ty, int pass)
    {
        var t = _ed.Scene.Terrain; int r = t.Res;
        for (int row = 0; row < Terrain.Seg; row++)
        {
            GL.glBegin(GL.TRIANGLES);
            for (int col = 0; col < Terrain.Seg; col++)
            {
                int gx = tx * Terrain.Seg + col, gy = ty * Terrain.Seg + row;
                void Vx(int x, int y)
                {
                    float wx = -t.Half + x * t.Step, wy = -t.Half + y * t.Step, hz = t.Heights[y * r + x];
                    var nn = TerrainNormal(x, y);
                    float a = pass == 1 ? t.Alpha[y * r + x] / 255f : 1f;
                    GL.glColor4f(1, 1, 1, a);
                    GL.glNormal3f(nn.X, nn.Y, nn.Z);
                    GL.glTexCoord2f(wx, -wy);
                    GL.glVertex3f(wx, wy, hz);
                }
                // чередуем диагонали как в Source
                if (((gx + gy) & 1) == 0) { Vx(gx, gy); Vx(gx + 1, gy); Vx(gx + 1, gy + 1); Vx(gx, gy); Vx(gx + 1, gy + 1); Vx(gx, gy + 1); }
                else { Vx(gx, gy); Vx(gx + 1, gy); Vx(gx, gy + 1); Vx(gx + 1, gy); Vx(gx + 1, gy + 1); Vx(gx, gy + 1); }
            }
            GL.glEnd();
        }
    }

    void DrawTerrain()
    {
        var t = _ed.Scene.Terrain;
        if (_tileLists.Length != t.Tiles * t.Tiles) RebuildTerrainLists();
        for (int i = 0; i < _tileLists.Length; i++)
        {
            if (!_tileDirty[i] && _tileLists[i] != 0) continue;
            if (_tileLists[i] == 0) _tileLists[i] = GL.glGenLists(2);
            for (int pass = 0; pass < 2; pass++)
            {
                GL.glNewList(_tileLists[i] + (uint)pass, GL.COMPILE);
                BuildTile(i % t.Tiles, i / t.Tiles, pass);
                GL.glEndList();
            }
            _tileDirty[i] = false;
        }
        var tex1 = Assets.Texture(t.Material, 1); var tex2 = Assets.Texture(t.Material, 2);
        for (int pass = 0; pass < 2; pass++)
        {
            var tex = pass == 0 ? tex1 : tex2;
            if (pass == 1)
            {
                if (tex == null) break;
                GL.glEnable(GL.BLEND); GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA); GL.glDepthMask(0);
            }
            if (tex != null)
            {
                GL.glEnable(GL.TEXTURE_2D); GL.glBindTexture(GL.TEXTURE_2D, tex.Id);
                GL.glMatrixMode(0x1702); GL.glLoadIdentity(); GL.glScalef(1f / (tex.W * 0.25f), 1f / (tex.H * 0.25f), 1); GL.glMatrixMode(GL.MODELVIEW);
            }
            else GL.glDisable(GL.TEXTURE_2D);
            if (tex == null && pass == 0) GL.glColor3f(0.45f, 0.5f, 0.35f);
            foreach (var l in _tileLists) GL.glCallList(l + (uint)pass);
            GL.glMatrixMode(0x1702); GL.glLoadIdentity(); GL.glMatrixMode(GL.MODELVIEW);
        }
        GL.glDisable(GL.BLEND); GL.glDepthMask(1); GL.glDisable(GL.TEXTURE_2D);
    }

    void DrawWater()
    {
        var env = _ed.Scene.Env;
        if (!env.Water) return;
        float h = _ed.Scene.Terrain.Half, z = env.WaterZ;
        GL.glDisable(GL.LIGHTING); GL.glDisable(GL.TEXTURE_2D);
        GL.glEnable(GL.BLEND); GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA); GL.glDepthMask(0);
        GL.glColor4f(0.15f, 0.35f, 0.45f, 0.6f);
        GL.glBegin(GL.QUADS);
        GL.glVertex3f(-h, -h, z); GL.glVertex3f(h, -h, z); GL.glVertex3f(h, h, z); GL.glVertex3f(-h, h, z);
        GL.glEnd();
        GL.glDepthMask(1); GL.glDisable(GL.BLEND); GL.glEnable(GL.LIGHTING);
    }

    // ---------------------------------------------------------------- объекты
    /// <summary>Локальные габариты объекта (без поворота).</summary>
    public (Vector3 mn, Vector3 mx) LocalBounds(SceneObject o)
    {
        switch (o.Kind)
        {
            case ObjKind.Block:
                return (new(-o.Size[0] / 2, -o.Size[1] / 2, 0), new(o.Size[0] / 2, o.Size[1] / 2, o.Size[2]));
            case ObjKind.Light:
                return (new(-12, -12, -12), new(12, 12, 12));
        }
        var m = string.IsNullOrEmpty(o.Model) ? null : Assets.GetMesh(o.Model);
        if (m != null && m.Maxs[0] > m.Mins[0])
            return (new Vector3(m.Mins[0], m.Mins[1], m.Mins[2]) * o.Scale, new Vector3(m.Maxs[0], m.Maxs[1], m.Maxs[2]) * o.Scale);
        return o.Cls == "info_player_start" ? (new(-16, -16, 0), new(16, 16, 72)) : (new(-16, -16, 0), new(16, 16, 48));
    }

    Matrix4x4 World(SceneObject o)
    {
        float d = MathF.PI / 180;
        return Matrix4x4.CreateRotationX(o.Ang[2] * d) * Matrix4x4.CreateRotationY(o.Ang[0] * d) *
               Matrix4x4.CreateRotationZ(o.Ang[1] * d) * Matrix4x4.CreateTranslation(o.Pos[0], o.Pos[1], o.Pos[2]);
    }

    void DrawObject(SceneObject o, bool ghost)
    {
        GL.glPushMatrix();
        GL.glMultMatrixf(GL.M(World(o)));
        if (ghost) { GL.glEnable(GL.BLEND); GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA); }
        float a = ghost ? 0.55f : 1f;
        switch (o.Kind)
        {
            case ObjKind.Block: DrawBlock(o, a); break;
            case ObjKind.Light:
                GL.glDisable(GL.LIGHTING);
                GL.glColor4f(o.Color[0] / 255f, o.Color[1] / 255f, o.Color[2] / 255f, a);
                Octahedron(12);
                GL.glEnable(GL.LIGHTING);
                break;
            default:
                uint list = Assets.ModelList(o.Model);
                if (list != 0)
                {
                    GL.glColor4f(1, 1, 1, a);
                    GL.glScalef(o.Scale, o.Scale, o.Scale);
                    GL.glCallList(list);
                }
                else
                {
                    var (mn, mx) = LocalBounds(o);
                    GL.glDisable(GL.LIGHTING);
                    if (o.Cls == "info_player_start") GL.glColor4f(0.2f, 0.9f, 0.3f, a); else GL.glColor4f(0.7f, 0.7f, 0.75f, a);
                    SolidBox(mn, mx);
                    GL.glEnable(GL.LIGHTING);
                }
                if (o.Cls == "info_player_start")
                {
                    GL.glDisable(GL.LIGHTING); GL.glDisable(GL.DEPTH_TEST); GL.glLineWidth(3); GL.glColor3f(0.2f, 1, 0.3f);
                    GL.glBegin(GL.LINES); GL.glVertex3f(0, 0, 40); GL.glVertex3f(48, 0, 40); GL.glVertex3f(48, 0, 40); GL.glVertex3f(36, 8, 40);
                    GL.glVertex3f(48, 0, 40); GL.glVertex3f(36, -8, 40); GL.glEnd();
                    GL.glLineWidth(1); GL.glEnable(GL.DEPTH_TEST); GL.glEnable(GL.LIGHTING);
                }
                break;
        }
        if (ghost) GL.glDisable(GL.BLEND);
        GL.glPopMatrix();
    }

    void DrawBlock(SceneObject o, float a)
    {
        var tex = Assets.Texture(o.Material);
        var (mn, mx) = LocalBounds(o);
        if (tex != null)
        {
            GL.glEnable(GL.TEXTURE_2D); GL.glBindTexture(GL.TEXTURE_2D, tex.Id);
            GL.glMatrixMode(0x1702); GL.glLoadIdentity(); GL.glScalef(1f / (tex.W * 0.25f), 1f / (tex.H * 0.25f), 1); GL.glMatrixMode(GL.MODELVIEW);
            GL.glColor4f(1, 1, 1, a);
        }
        else GL.glColor4f(0.6f, 0.6f, 0.65f, a);
        // UV в мировых единицах, как в VMF (uaxis/vaxis)
        float yaw = o.Ang[1] * MathF.PI / 180, cs = MathF.Cos(yaw), sn = MathF.Sin(yaw);
        Vector3 Wp(float x, float y, float z) => new(o.Pos[0] + x * cs - y * sn, o.Pos[1] + x * sn + y * cs, o.Pos[2] + z);
        void Face(Vector3 n, Vector3 u, Vector3 v, params Vector3[] p)
        {
            var ru = new Vector3(u.X * cs - u.Y * sn, u.X * sn + u.Y * cs, u.Z);
            var rv = new Vector3(v.X * cs - v.Y * sn, v.X * sn + v.Y * cs, v.Z);
            GL.glNormal3f(n.X, n.Y, n.Z);
            foreach (var q in p)
            {
                var w = Wp(q.X, q.Y, q.Z);
                GL.glTexCoord2f(Vector3.Dot(w, ru), Vector3.Dot(w, rv));
                GL.glVertex3f(q.X, q.Y, q.Z);
            }
        }
        GL.glBegin(GL.QUADS);
        Face(Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitY, new(mn.X, mn.Y, mx.Z), new(mx.X, mn.Y, mx.Z), new(mx.X, mx.Y, mx.Z), new(mn.X, mx.Y, mx.Z));
        Face(-Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitY, new(mn.X, mn.Y, mn.Z), new(mn.X, mx.Y, mn.Z), new(mx.X, mx.Y, mn.Z), new(mx.X, mn.Y, mn.Z));
        Face(Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ, new(mx.X, mn.Y, mn.Z), new(mx.X, mx.Y, mn.Z), new(mx.X, mx.Y, mx.Z), new(mx.X, mn.Y, mx.Z));
        Face(-Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ, new(mn.X, mn.Y, mn.Z), new(mn.X, mn.Y, mx.Z), new(mn.X, mx.Y, mx.Z), new(mn.X, mx.Y, mn.Z));
        Face(Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ, new(mn.X, mx.Y, mn.Z), new(mn.X, mx.Y, mx.Z), new(mx.X, mx.Y, mx.Z), new(mx.X, mx.Y, mn.Z));
        Face(-Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ, new(mn.X, mn.Y, mn.Z), new(mx.X, mn.Y, mn.Z), new(mx.X, mn.Y, mx.Z), new(mn.X, mn.Y, mx.Z));
        GL.glEnd();
        GL.glMatrixMode(0x1702); GL.glLoadIdentity(); GL.glMatrixMode(GL.MODELVIEW);
        GL.glDisable(GL.TEXTURE_2D);
    }

    static void SolidBox(Vector3 mn, Vector3 mx)
    {
        GL.glBegin(GL.QUADS);
        GL.glNormal3f(0, 0, 1); GL.glVertex3f(mn.X, mn.Y, mx.Z); GL.glVertex3f(mx.X, mn.Y, mx.Z); GL.glVertex3f(mx.X, mx.Y, mx.Z); GL.glVertex3f(mn.X, mx.Y, mx.Z);
        GL.glNormal3f(0, 0, -1); GL.glVertex3f(mn.X, mn.Y, mn.Z); GL.glVertex3f(mn.X, mx.Y, mn.Z); GL.glVertex3f(mx.X, mx.Y, mn.Z); GL.glVertex3f(mx.X, mn.Y, mn.Z);
        GL.glNormal3f(1, 0, 0); GL.glVertex3f(mx.X, mn.Y, mn.Z); GL.glVertex3f(mx.X, mx.Y, mn.Z); GL.glVertex3f(mx.X, mx.Y, mx.Z); GL.glVertex3f(mx.X, mn.Y, mx.Z);
        GL.glNormal3f(-1, 0, 0); GL.glVertex3f(mn.X, mn.Y, mn.Z); GL.glVertex3f(mn.X, mn.Y, mx.Z); GL.glVertex3f(mn.X, mx.Y, mx.Z); GL.glVertex3f(mn.X, mx.Y, mn.Z);
        GL.glNormal3f(0, 1, 0); GL.glVertex3f(mn.X, mx.Y, mn.Z); GL.glVertex3f(mn.X, mx.Y, mx.Z); GL.glVertex3f(mx.X, mx.Y, mx.Z); GL.glVertex3f(mx.X, mx.Y, mn.Z);
        GL.glNormal3f(0, -1, 0); GL.glVertex3f(mn.X, mn.Y, mn.Z); GL.glVertex3f(mx.X, mn.Y, mn.Z); GL.glVertex3f(mx.X, mn.Y, mx.Z); GL.glVertex3f(mn.X, mn.Y, mx.Z);
        GL.glEnd();
    }

    static void WireBox(Vector3 mn, Vector3 mx)
    {
        GL.glBegin(GL.LINES);
        Vector3[] c = { new(mn.X, mn.Y, mn.Z), new(mx.X, mn.Y, mn.Z), new(mx.X, mx.Y, mn.Z), new(mn.X, mx.Y, mn.Z),
                        new(mn.X, mn.Y, mx.Z), new(mx.X, mn.Y, mx.Z), new(mx.X, mx.Y, mx.Z), new(mn.X, mx.Y, mx.Z) };
        int[] e = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        foreach (var i in e) GL.glVertex3f(c[i].X, c[i].Y, c[i].Z);
        GL.glEnd();
    }

    static void Octahedron(float r)
    {
        Vector3[] v = { new(r, 0, 0), new(-r, 0, 0), new(0, r, 0), new(0, -r, 0), new(0, 0, r), new(0, 0, -r) };
        int[] f = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
        GL.glBegin(GL.TRIANGLES);
        foreach (var i in f) GL.glVertex3f(v[i].X, v[i].Y, v[i].Z);
        GL.glEnd();
    }

    void DrawSelection(SceneObject o)
    {
        var (mn, mx) = LocalBounds(o);
        GL.glPushMatrix(); GL.glMultMatrixf(GL.M(World(o)));
        GL.glDisable(GL.TEXTURE_2D); GL.glDisable(GL.DEPTH_TEST);
        GL.glColor3f(1f, 0.6f, 0.1f); GL.glLineWidth(2);
        WireBox(mn - new Vector3(1), mx + new Vector3(1));
        if (o.Kind == ObjKind.Light)
        {
            GL.glColor3f(1, 0.9f, 0.4f); GL.glLineWidth(1);
            GL.glBegin(GL.LINE_LOOP);
            for (int i = 0; i < 48; i++) { float a = i * MathF.Tau / 48; GL.glVertex3f(MathF.Cos(a) * o.Range, MathF.Sin(a) * o.Range, 0); }
            GL.glEnd();
        }
        GL.glLineWidth(1); GL.glEnable(GL.DEPTH_TEST);
        GL.glPopMatrix();
    }

    // ---------------------------------------------------------------- гизмо
    float GizmoLen(SceneObject o) => Vector3.Distance(CamPos, P(o)) * 0.16f;
    static Vector3 P(SceneObject o) => new(o.Pos[0], o.Pos[1], o.Pos[2]);

    Vector3 GizmoOrigin(SceneObject o)
    {
        var (mn, mx) = LocalBounds(o);
        return P(o) + new Vector3(0, 0, o.Kind == ObjKind.Block ? o.Size[2] / 2 : (mn.Z + mx.Z) / 2 * 0 + 0);
    }

    Vector3 Axis(SceneObject o, int i)
    {
        if (o.Kind == ObjKind.Block && i < 2 && _ed.Tool == Tool.Scale)
        {
            float y = o.Ang[1] * MathF.PI / 180;
            return i == 0 ? new(MathF.Cos(y), MathF.Sin(y), 0) : new(-MathF.Sin(y), MathF.Cos(y), 0);
        }
        return i == 0 ? Vector3.UnitX : i == 1 ? Vector3.UnitY : Vector3.UnitZ;
    }

    void DrawGizmo(SceneObject o)
    {
        if (_ed.IsTerrainTool || _ed.Tool == Tool.Select) return;
        var c = GizmoOrigin(o); float L = GizmoLen(o);
        GL.glDisable(GL.DEPTH_TEST); GL.glDisable(GL.TEXTURE_2D);
        float[][] col = { new[] { 0.95f, 0.25f, 0.25f }, new[] { 0.3f, 0.9f, 0.3f }, new[] { 0.3f, 0.5f, 1f } };
        if (_ed.Tool == Tool.Rotate)
        {
            GL.glLineWidth(_hover == Drag.Ring || _drag == Drag.Ring ? 4 : 2.5f);
            GL.glColor3f(0.3f, 0.5f, 1f);
            if (_hover == Drag.Ring || _drag == Drag.Ring) GL.glColor3f(1, 1, 0.3f);
            GL.glBegin(GL.LINE_LOOP);
            for (int i = 0; i < 64; i++) { float a = i * MathF.Tau / 64; GL.glVertex3f(c.X + MathF.Cos(a) * L, c.Y + MathF.Sin(a) * L, c.Z); }
            GL.glEnd();
            float yaw = o.Ang[1] * MathF.PI / 180;
            GL.glBegin(GL.LINES); GL.glVertex3f(c.X, c.Y, c.Z); GL.glVertex3f(c.X + MathF.Cos(yaw) * L, c.Y + MathF.Sin(yaw) * L, c.Z); GL.glEnd();
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                var ax = Axis(o, i); var e = c + ax * L;
                bool hot = _hover == (_ed.Tool == Tool.Scale ? Drag.ScaleX + i : Drag.AxisX + i) || _drag == (_ed.Tool == Tool.Scale ? Drag.ScaleX + i : Drag.AxisX + i);
                if (hot) GL.glColor3f(1, 1, 0.3f); else GL.glColor3f(col[i][0], col[i][1], col[i][2]);
                GL.glLineWidth(hot ? 4 : 2.5f);
                GL.glBegin(GL.LINES); GL.glVertex3f(c.X, c.Y, c.Z); GL.glVertex3f(e.X, e.Y, e.Z); GL.glEnd();
                float hs = L * 0.07f;
                GL.glPushMatrix(); GL.glTranslatef(e.X, e.Y, e.Z);
                if (_ed.Tool == Tool.Scale) SolidBox(new Vector3(-hs), new Vector3(hs)); else Octahedron(hs * 1.2f);
                GL.glPopMatrix();
            }
        }
        GL.glLineWidth(1); GL.glEnable(GL.DEPTH_TEST);
    }

    static float DistToSeg(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        float t = l2 < 1e-6f ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
        float x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
        return MathF.Sqrt(x * x + y * y);
    }

    Drag HitGizmo(Point m)
    {
        var o = _ed.Selected;
        if (o == null || _ed.IsTerrainTool || _ed.Tool == Tool.Select) return Drag.None;
        var c = GizmoOrigin(o); float L = GizmoLen(o);
        var pc = Project(c); if (pc == null) return Drag.None;
        if (_ed.Tool == Tool.Rotate)
        {
            PointF? prev = null;
            for (int i = 0; i <= 64; i++)
            {
                float a = i * MathF.Tau / 64;
                var pp = Project(c + new Vector3(MathF.Cos(a) * L, MathF.Sin(a) * L, 0));
                if (pp != null && prev != null && DistToSeg(m, prev.Value, pp.Value) < 9) return Drag.Ring;
                prev = pp;
            }
            return Drag.None;
        }
        Drag best = Drag.None; float bd = 10;
        for (int i = 0; i < 3; i++)
        {
            var pe = Project(c + Axis(o, i) * L); if (pe == null) continue;
            float d = DistToSeg(m, pc.Value, pe.Value);
            if (d < bd) { bd = d; best = (_ed.Tool == Tool.Scale ? Drag.ScaleX : Drag.AxisX) + i; }
        }
        return best;
    }

    // ---------------------------------------------------------------- пикинг
    public Vector3? PickTerrain(Point m)
    {
        var (o, d) = Ray(m);
        var t = _ed.Scene.Terrain;
        float step = t.Step * 0.5f, prev = 0;
        bool Inside(Vector3 p) => Math.Abs(p.X) <= t.Half && Math.Abs(p.Y) <= t.Half;
        for (float s = 0; s < 40000; s += step)
        {
            var p = o + d * s;
            if (Inside(p) && p.Z <= t.HeightAt(p.X, p.Y))
            {
                float lo = prev, hi = s;
                for (int i = 0; i < 20; i++)
                {
                    float mid = (lo + hi) / 2; var q = o + d * mid;
                    if (q.Z <= t.HeightAt(q.X, q.Y)) hi = mid; else lo = mid;
                }
                return o + d * hi;
            }
            prev = s;
            step = Math.Max(t.Step * 0.25f, s * 0.01f);
        }
        // промах — пересечение с плоскостью z=0
        if (Math.Abs(d.Z) > 1e-4f) { float s = -o.Z / d.Z; if (s > 0) { var p = o + d * s; if (Inside(p)) return p; } }
        return null;
    }

    public SceneObject PickObject(Point m, out float dist)
    {
        var (o, d) = Ray(m);
        SceneObject best = null; dist = float.MaxValue;
        foreach (var obj in _ed.Scene.Objects)
        {
            var (mn, mx) = LocalBounds(obj);
            if (!Matrix4x4.Invert(World(obj), out var inv)) continue;
            var lo = Vector3.Transform(o, inv); var ld = Vector3.TransformNormal(d, inv);
            float t0 = 0, t1 = float.MaxValue; bool hit = true;
            for (int i = 0; i < 3; i++)
            {
                float oo = i == 0 ? lo.X : i == 1 ? lo.Y : lo.Z, dd = i == 0 ? ld.X : i == 1 ? ld.Y : ld.Z;
                float a = i == 0 ? mn.X : i == 1 ? mn.Y : mn.Z, b = i == 0 ? mx.X : i == 1 ? mx.Y : mx.Z;
                if (Math.Abs(dd) < 1e-8f) { if (oo < a || oo > b) { hit = false; break; } continue; }
                float ta = (a - oo) / dd, tb = (b - oo) / dd;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Math.Max(t0, ta); t1 = Math.Min(t1, tb);
                if (t0 > t1) { hit = false; break; }
            }
            if (hit && t0 < dist) { dist = t0; best = obj; }
        }
        return best;
    }

    // ---------------------------------------------------------------- кисти рельефа
    void ApplyBrush(float dt)
    {
        _brushHit = PickTerrain(_mouse);
        if (!_brushHit.HasValue) return;
        var t = _ed.Scene.Terrain; var c = _brushHit.Value;
        float R = _ed.BrushRadius, k = _ed.BrushStrength * dt;
        int r = t.Res;
        int gx0 = Math.Max(0, (int)((c.X - R + t.Half) / t.Step)), gx1 = Math.Min(r - 1, (int)((c.X + R + t.Half) / t.Step) + 1);
        int gy0 = Math.Max(0, (int)((c.Y - R + t.Half) / t.Step)), gy1 = Math.Min(r - 1, (int)((c.Y + R + t.Half) / t.Step) + 1);
        var tool = _ed.Tool;
        bool invert = ModifierKeys.HasFlag(Keys.Control) || ModifierKeys.HasFlag(Keys.Shift);
        if (tool == Tool.TerrainRaise && invert) tool = Tool.TerrainLower;
        else if (tool == Tool.TerrainLower && invert) tool = Tool.TerrainRaise;
        var src = tool == Tool.TerrainSmooth ? (float[])t.Heights.Clone() : null;
        for (int gy = gy0; gy <= gy1; gy++)
            for (int gx = gx0; gx <= gx1; gx++)
            {
                float wx = -t.Half + gx * t.Step, wy = -t.Half + gy * t.Step;
                float dd = MathF.Sqrt((wx - c.X) * (wx - c.X) + (wy - c.Y) * (wy - c.Y)) / R;
                if (dd >= 1) continue;
                float f = 1 - dd; f = f * f * (3 - 2 * f);   // плавный спад к краю
                int i = gy * r + gx;
                switch (tool)
                {
                    case Tool.TerrainRaise: t.Heights[i] += f * k * 220; break;
                    case Tool.TerrainLower: t.Heights[i] -= f * k * 220; break;
                    case Tool.TerrainFlatten: t.Heights[i] += (_flattenTarget - t.Heights[i]) * Math.Min(1, f * k * 8); break;
                    case Tool.TerrainSmooth:
                        {
                            float s = 0; int n = 0;
                            for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                                {
                                    int x = gx + ox, y = gy + oy;
                                    if (x < 0 || y < 0 || x >= r || y >= r) continue;
                                    s += src[y * r + x]; n++;
                                }
                            t.Heights[i] += (s / n - t.Heights[i]) * Math.Min(1, f * k * 10);
                            break;
                        }
                    case Tool.TerrainPaint:
                        t.Alpha[i] = Math.Clamp(t.Alpha[i] + (invert ? -1 : 1) * f * k * 1200, 0, 255);
                        break;
                }
                t.Heights[i] = Math.Clamp(t.Heights[i], VmfWriter_FloorBase + 16, 8000);
            }
        MarkDirty(gx0, gy0, gx1, gy1);
        _ed.Dirty = true;
    }
    const float VmfWriter_FloorBase = -512;

    void DrawBrush(Vector3 c)
    {
        var t = _ed.Scene.Terrain;
        GL.glDisable(GL.DEPTH_TEST); GL.glDisable(GL.TEXTURE_2D);
        GL.glColor3f(_ed.Tool == Tool.TerrainPaint ? 0.4f : 1f, 0.85f, 0.2f); GL.glLineWidth(2);
        GL.glBegin(GL.LINE_LOOP);
        for (int i = 0; i < 64; i++)
        {
            float a = i * MathF.Tau / 64, x = c.X + MathF.Cos(a) * _ed.BrushRadius, y = c.Y + MathF.Sin(a) * _ed.BrushRadius;
            GL.glVertex3f(x, y, t.HeightAt(x, y) + 4);
        }
        GL.glEnd();
        GL.glLineWidth(1); GL.glEnable(GL.DEPTH_TEST);
    }

    void DrawCompass()
    {
        // маленькие оси в углу
        GL.glMatrixMode(GL.PROJECTION); GL.glPushMatrix(); GL.glLoadIdentity();
        GL.glMatrixMode(GL.MODELVIEW); GL.glPushMatrix();
        var rot = _view; rot.M41 = 0; rot.M42 = 0; rot.M43 = 0;
        var m = rot * Matrix4x4.CreateScale(0.08f * Height / Math.Max(1, Width), 0.08f, 0.08f) * Matrix4x4.CreateTranslation(-0.88f, -0.85f, 0);
        GL.glLoadMatrixf(GL.M(m));
        GL.glDisable(GL.DEPTH_TEST); GL.glLineWidth(2);
        GL.glBegin(GL.LINES);
        GL.glColor3f(0.95f, 0.3f, 0.3f); GL.glVertex3f(0, 0, 0); GL.glVertex3f(1, 0, 0);
        GL.glColor3f(0.3f, 0.9f, 0.3f); GL.glVertex3f(0, 0, 0); GL.glVertex3f(0, 1, 0);
        GL.glColor3f(0.3f, 0.5f, 1f); GL.glVertex3f(0, 0, 0); GL.glVertex3f(0, 0, 1);
        GL.glEnd();
        GL.glLineWidth(1); GL.glEnable(GL.DEPTH_TEST);
        GL.glPopMatrix(); GL.glMatrixMode(GL.PROJECTION); GL.glPopMatrix(); GL.glMatrixMode(GL.MODELVIEW);
    }

    // ---------------------------------------------------------------- миниатюры для библиотеки
    void ProcessThumbnails()
    {
        int n = _thumbQueue.Count, done = 0;
        for (int k = 0; k < n && done < 4; k++)
        {
            var (model, cb) = _thumbQueue.Dequeue();
            if (Assets.MeshFailed(model)) continue;
            uint list = Assets.ModelList(model);
            var mesh = Assets.GetMesh(model);
            if (list == 0 || mesh == null) { _thumbQueue.Enqueue((model, cb)); continue; }
            const int S = 96;
            GL.glViewport(0, 0, S, S);
            GL.glClearColor(0.16f, 0.17f, 0.19f, 1);
            GL.glClear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);
            var mn = new Vector3(mesh.Mins[0], mesh.Mins[1], mesh.Mins[2]); var mx = new Vector3(mesh.Maxs[0], mesh.Maxs[1], mesh.Maxs[2]);
            var center = (mn + mx) / 2; float rad = Math.Max(1, (mx - mn).Length() / 2);
            var eye = center + Vector3.Normalize(new Vector3(-1, -0.8f, 0.6f)) * rad * 2.6f;
            GL.glMatrixMode(GL.PROJECTION); GL.glLoadMatrixf(GL.M(Matrix4x4.CreatePerspectiveFieldOfView(0.7f, 1, rad * 0.1f, rad * 10)));
            GL.glMatrixMode(GL.MODELVIEW); GL.glLoadMatrixf(GL.M(Matrix4x4.CreateLookAt(eye, center, Vector3.UnitZ)));
            GL.glEnable(GL.DEPTH_TEST); GL.glEnable(GL.LIGHTING); GL.glEnable(GL.LIGHT0); GL.glEnable(GL.COLOR_MATERIAL); GL.glEnable(GL.NORMALIZE);
            GL.glColorMaterial(GL.FRONT_AND_BACK, GL.AMBIENT_AND_DIFFUSE);
            GL.glLightfv(GL.LIGHT0, GL.POSITION, new[] { -0.5f, -0.8f, 1f, 0 });
            GL.glLightfv(GL.LIGHT0, GL.DIFFUSE, new[] { 0.9f, 0.9f, 0.85f, 1 });
            GL.glLightModelfv(GL.LIGHT_MODEL_AMBIENT, new[] { 0.45f, 0.45f, 0.5f, 1 });
            GL.glColor3f(1, 1, 1);
            GL.glCallList(list);
            var px = new byte[S * S * 4];
            GL.glReadPixels(0, 0, S, S, GL.RGBA, GL.UNSIGNED_BYTE, px);
            var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, S, S), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var row = new byte[S * 4];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int s = ((S - 1 - y) * S + x) * 4;
                    row[x * 4] = px[s + 2]; row[x * 4 + 1] = px[s + 1]; row[x * 4 + 2] = px[s]; row[x * 4 + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, S * 4);
            }
            bmp.UnlockBits(bd);
            cb(bmp);
            done++;
        }
    }

    // ---------------------------------------------------------------- ввод
    protected override bool IsInputKey(Keys k) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _keys.Add(e.KeyCode);
        if (_rmb) return;   // в режиме полёта клавиши — это движение
        switch (e.KeyCode)
        {
            case Keys.Q: _ed.SetTool(Tool.Select); break;
            case Keys.W: _ed.SetTool(Tool.Move); break;
            case Keys.E: _ed.SetTool(Tool.Rotate); break;
            case Keys.R: _ed.SetTool(Tool.Scale); break;
            case Keys.F: FocusSelected(); break;
            case Keys.Delete: _ed.DeleteSelected(); break;
            case Keys.Escape: _ed.Select(null); break;
            case Keys.End: DropToGround(); break;
            case Keys.Home: LookAtCenter(); break;
            case Keys.D when e.Control: _ed.DuplicateSelected(); break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e) { _keys.Remove(e.KeyCode); }
    protected override void OnLostFocus(EventArgs e) { _keys.Clear(); _rmb = false; base.OnLostFocus(e); }

    public void FocusSelected()
    {
        var o = _ed.Selected; if (o == null) return;
        var (mn, mx) = LocalBounds(o);
        float rad = Math.Max(64, (mx - mn).Length());
        CamPos = P(o) + new Vector3(0, 0, (mn.Z + mx.Z) / 2) - Forward() * rad * 2.2f;
    }

    public void DropToGround()
    {
        var o = _ed.Selected; if (o == null) return;
        _ed.PushUndo();
        o.Pos[2] = _ed.Scene.Terrain.HeightAt(o.Pos[0], o.Pos[1]) - (o.Kind == ObjKind.Prop ? Math.Min(0, LocalBounds(o).mn.Z) : 0);
        _ed.NotifyChanged();
    }

    public void LookAtCenter()
    {
        CamPos = new Vector3(-_ed.Scene.Terrain.Half * 0.6f, -_ed.Scene.Terrain.Half * 0.6f, 1200);
        CamYaw = 45; CamPitch = -25;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        _mouse = _downMouse = e.Location;
        if (e.Button == MouseButtons.Right) { _rmb = true; Capture = true; return; }
        if (e.Button == MouseButtons.Middle) { _mmb = true; Capture = true; return; }
        if (e.Button != MouseButtons.Left) return;
        _lmb = true; Capture = true;
        if (_ed.IsTerrainTool)
        {
            _ed.PushUndo();
            var h = PickTerrain(e.Location);
            _flattenTarget = h?.Z ?? 0;
            _drag = Drag.Terrain;
            return;
        }
        var g = HitGizmo(e.Location);
        var sel = _ed.Selected;
        if (g != Drag.None && sel != null)
        {
            _ed.PushUndo();
            _drag = g;
            _dragStartPos = (float[])sel.Pos.Clone(); _dragStartSize = (float[])sel.Size.Clone();
            _dragStartYaw = sel.Ang[1]; _dragStartScale = sel.Scale;
            return;
        }
        var hit = PickObject(e.Location, out _);
        _ed.Select(hit);
        if (hit != null && (_ed.Tool == Tool.Move || _ed.Tool == Tool.Select))
        {
            var tp = PickTerrainExcluding(e.Location);
            if (tp.HasValue)
            {
                _ed.PushUndo();
                _drag = Drag.Free;
                _grabOffset = new Vector3(hit.Pos[0] - tp.Value.X, hit.Pos[1] - tp.Value.Y, 0);
                _grabHeight = hit.Pos[2] - _ed.Scene.Terrain.HeightAt(hit.Pos[0], hit.Pos[1]);
            }
        }
    }

    Vector3? PickTerrainExcluding(Point m) => PickTerrain(m);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var dx = e.X - _mouse.X; var dy = e.Y - _mouse.Y;
        var prev = _mouse;
        _mouse = e.Location;
        if (_rmb)
        {
            CamYaw -= dx * 0.25f; CamPitch = Math.Clamp(CamPitch - dy * 0.25f, -89, 89);
            return;
        }
        if (_mmb)
        {
            var fwd = Forward(); var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitZ)); var up = Vector3.Cross(right, fwd);
            CamPos += (-right * dx + up * dy) * 2f;
            return;
        }
        if (_ed.IsTerrainTool) { _brushHit = PickTerrain(e.Location); return; }
        var o = _ed.Selected;
        if (_drag == Drag.None || o == null) { _hover = HitGizmo(e.Location); return; }
        switch (_drag)
        {
            case Drag.Free:
                {
                    var tp = PickTerrain(e.Location);
                    if (!tp.HasValue) break;
                    float nx = _ed.SnapV(tp.Value.X + _grabOffset.X), ny = _ed.SnapV(tp.Value.Y + _grabOffset.Y);
                    o.Pos[0] = nx; o.Pos[1] = ny;
                    o.Pos[2] = _ed.Scene.Terrain.HeightAt(nx, ny) + _grabHeight;
                    break;
                }
            case Drag.AxisX: case Drag.AxisY: case Drag.AxisZ:
                {
                    int i = _drag - Drag.AxisX;
                    float d = AxisDelta(o, i, e.Location);
                    o.Pos[i] = _ed.SnapV(_dragStartPos[i] + d);
                    break;
                }
            case Drag.ScaleX: case Drag.ScaleY: case Drag.ScaleZ:
                {
                    int i = _drag - Drag.ScaleX;
                    float d = AxisDelta(o, i, e.Location);
                    if (o.Kind == ObjKind.Block)
                        o.Size[i] = Math.Max(_ed.Snap ? _ed.Grid : 1, _ed.SnapV(_dragStartSize[i] + (i == 2 ? d : d * 2)));
                    else
                        o.Scale = Math.Clamp(MathF.Round((_dragStartScale * (1 + d / Math.Max(16, GizmoLen(o)))) * 20) / 20, 0.05f, 20);
                    break;
                }
            case Drag.Ring:
                o.Ang[1] = _ed.SnapA(_dragStartYaw - (e.X - _downMouse.X) * 0.6f);
                o.Ang[1] = ((o.Ang[1] % 360) + 360) % 360;
                break;
        }
        _ed.NotifyChanged();
    }

    float AxisDelta(SceneObject o, int i, Point m)
    {
        var c = GizmoOrigin(o); float L = GizmoLen(o);
        var a = new Vector3(_dragStartPos[0], _dragStartPos[1], _dragStartPos[2]) + (c - P(o));
        var pa = Project(a); var pb = Project(a + Axis(o, i) * L);
        if (pa == null || pb == null) return 0;
        float sx = pb.Value.X - pa.Value.X, sy = pb.Value.Y - pa.Value.Y, sl = MathF.Sqrt(sx * sx + sy * sy);
        if (sl < 2) return 0;
        float mdx = m.X - _downMouse.X, mdy = m.Y - _downMouse.Y;
        return (mdx * sx + mdy * sy) / sl / sl * L;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) _rmb = false;
        if (e.Button == MouseButtons.Middle) _mmb = false;
        if (e.Button == MouseButtons.Left)
        {
            _lmb = false;
            if (_drag == Drag.Terrain) _ed.NotifyTerrain(false);
            else if (_drag != Drag.None) _ed.NotifyChanged();
            _drag = Drag.None;
        }
        Capture = _rmb || _mmb || _lmb;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_rmb) { _speed = Math.Clamp(_speed * (e.Delta > 0 ? 1.2f : 1 / 1.2f), 100, 8000); Status?.Invoke($"Скорость камеры: {_speed:0}"); return; }
        if (_ed.IsTerrainTool && ModifierKeys.HasFlag(Keys.Alt)) return;
        CamPos += Forward() * e.Delta * 0.8f * (ModifierKeys.HasFlag(Keys.Shift) ? 4 : 1);
    }

    // ---------------------------------------------------------------- drag & drop из библиотеки и из проводника
    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data.GetDataPresent(DragPayload.Marker) && DragPayload.Current != null)
        {
            _ghost = DragPayload.Current.Clone();
            e.Effect = DragDropEffects.Copy;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (_ghost == null) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; return; }
        e.Effect = DragDropEffects.Copy;
        var p = PointToClient(new Point(e.X, e.Y));
        _mouse = p;
        var hit = PickTerrain(p);
        if (hit.HasValue)
        {
            float x = _ed.SnapV(hit.Value.X), y = _ed.SnapV(hit.Value.Y);
            _ghost.Pos = new[] { x, y, _ed.Scene.Terrain.HeightAt(x, y) + (_ghost.Kind == ObjKind.Light ? 96 : 0) };
            if (_ghost.Kind == ObjKind.Prop)
            {
                var mesh = Assets.GetMesh(_ghost.Model);
                if (mesh != null && mesh.Mins[2] < 0) _ghost.Pos[2] -= mesh.Mins[2] * _ghost.Scale;
            }
            if (_ghost.Kind == ObjKind.Entity && _ghost.Cls.StartsWith("npc_")) _ghost.Pos[2] += 4;
        }
        Invalidate();
    }

    protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); _ghost = null; }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (_ghost != null)
        {
            var o = _ghost; _ghost = null;
            _ed.Add(o);
            if (_ed.IsTerrainTool) _ed.SetTool(Tool.Move);
            Focus();
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            foreach (var f in files) FilesDropped?.Invoke(f);
    }
}
