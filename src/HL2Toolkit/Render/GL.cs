using System.Runtime.InteropServices;

namespace HL2Toolkit.Render;

/// <summary>Минимальные привязки OpenGL 1.x / WGL. Фиксированный конвейер — работает на любой видеокарте.</summary>
public static class GL
{
    const string L = "opengl32.dll";
    public const uint COLOR_BUFFER_BIT = 0x4000, DEPTH_BUFFER_BIT = 0x100;
    public const uint DEPTH_TEST = 0x0B71, CULL_FACE = 0x0B44, LIGHTING = 0x0B50, LIGHT0 = 0x4000, LIGHT1 = 0x4001;
    public const uint TEXTURE_2D = 0x0DE1, BLEND = 0x0BE2, FOG = 0x0B60, COLOR_MATERIAL = 0x0B57, NORMALIZE = 0x0BA1;
    public const uint LINE_SMOOTH = 0x0B20, POLYGON_OFFSET_FILL = 0x8037, ALPHA_TEST = 0x0BC0;
    public const uint PROJECTION = 0x1701, MODELVIEW = 0x1700;
    public const uint TRIANGLES = 4, LINES = 1, LINE_STRIP = 3, LINE_LOOP = 2, QUADS = 7, TRIANGLE_FAN = 6, POINTS = 0;
    public const uint SRC_ALPHA = 0x0302, ONE_MINUS_SRC_ALPHA = 0x0303, ONE = 1;
    public const uint POSITION = 0x1203, DIFFUSE = 0x1201, AMBIENT = 0x1200, SPECULAR = 0x1202, AMBIENT_AND_DIFFUSE = 0x1602;
    public const uint FRONT_AND_BACK = 0x0408, BACK = 0x0405, LIGHT_MODEL_AMBIENT = 0x0B53;
    public const uint TEXTURE_MIN_FILTER = 0x2801, TEXTURE_MAG_FILTER = 0x2800, TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803;
    public const int LINEAR = 0x2601, LINEAR_MIPMAP_LINEAR = 0x2703, REPEAT = 0x2901, CLAMP_TO_EDGE = 0x812F;
    public const uint GENERATE_MIPMAP = 0x8191, TEXTURE_MAX_ANISOTROPY = 0x84FE;
    public const uint RGBA = 0x1908, UNSIGNED_BYTE = 0x1401;
    public const uint COMPILE = 0x1300;
    public const uint FOG_MODE = 0x0B65, FOG_START = 0x0B63, FOG_END = 0x0B64, FOG_COLOR = 0x0B66;
    public const uint LEQUAL = 0x0203, LESS = 0x0201, GREATER = 0x0204;
    public const uint SMOOTH = 0x1D01;

    [DllImport(L)] public static extern void glClear(uint mask);
    [DllImport(L)] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport(L)] public static extern void glEnable(uint cap);
    [DllImport(L)] public static extern void glDisable(uint cap);
    [DllImport(L)] public static extern void glViewport(int x, int y, int w, int h);
    [DllImport(L)] public static extern void glMatrixMode(uint mode);
    [DllImport(L)] public static extern void glLoadIdentity();
    [DllImport(L)] public static extern void glLoadMatrixf(float[] m);
    [DllImport(L)] public static extern void glMultMatrixf(float[] m);
    [DllImport(L)] public static extern void glPushMatrix();
    [DllImport(L)] public static extern void glPopMatrix();
    [DllImport(L)] public static extern void glTranslatef(float x, float y, float z);
    [DllImport(L)] public static extern void glRotatef(float a, float x, float y, float z);
    [DllImport(L)] public static extern void glScalef(float x, float y, float z);
    [DllImport(L)] public static extern void glBegin(uint mode);
    [DllImport(L)] public static extern void glEnd();
    [DllImport(L)] public static extern void glVertex3f(float x, float y, float z);
    [DllImport(L)] public static extern void glNormal3f(float x, float y, float z);
    [DllImport(L)] public static extern void glTexCoord2f(float s, float t);
    [DllImport(L)] public static extern void glColor3f(float r, float g, float b);
    [DllImport(L)] public static extern void glColor4f(float r, float g, float b, float a);
    [DllImport(L)] public static extern void glLineWidth(float w);
    [DllImport(L)] public static extern void glPointSize(float s);
    [DllImport(L)] public static extern void glBlendFunc(uint s, uint d);
    [DllImport(L)] public static extern void glDepthMask(byte flag);
    [DllImport(L)] public static extern void glDepthFunc(uint f);
    [DllImport(L)] public static extern void glGenTextures(int n, uint[] tex);
    [DllImport(L)] public static extern void glDeleteTextures(int n, uint[] tex);
    [DllImport(L)] public static extern void glBindTexture(uint target, uint tex);
    [DllImport(L)] public static extern void glTexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, byte[] data);
    [DllImport(L)] public static extern void glTexParameteri(uint target, uint pname, int param);
    [DllImport(L)] public static extern void glTexParameterf(uint target, uint pname, float param);
    [DllImport(L)] public static extern uint glGenLists(int range);
    [DllImport(L)] public static extern void glNewList(uint list, uint mode);
    [DllImport(L)] public static extern void glEndList();
    [DllImport(L)] public static extern void glCallList(uint list);
    [DllImport(L)] public static extern void glDeleteLists(uint list, int range);
    [DllImport(L)] public static extern void glLightfv(uint light, uint pname, float[] p);
    [DllImport(L)] public static extern void glLightModelfv(uint pname, float[] p);
    [DllImport(L)] public static extern void glColorMaterial(uint face, uint mode);
    [DllImport(L)] public static extern void glFogi(uint pname, int p);
    [DllImport(L)] public static extern void glFogf(uint pname, float p);
    [DllImport(L)] public static extern void glFogfv(uint pname, float[] p);
    [DllImport(L)] public static extern void glReadPixels(int x, int y, int w, int h, uint format, uint type, byte[] data);
    [DllImport(L)] public static extern void glPolygonOffset(float f, float u);
    [DllImport(L)] public static extern void glShadeModel(uint m);
    [DllImport(L)] public static extern void glAlphaFunc(uint f, float r);
    [DllImport(L)] public static extern void glFinish();

    [DllImport(L)] public static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport(L)] public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr ctx);
    [DllImport(L)] public static extern bool wglDeleteContext(IntPtr ctx);

    [StructLayout(LayoutKind.Sequential)]
    public struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize, nVersion; public uint dwFlags; public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift,
            cBlueBits, cBlueShift, cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits,
            cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved; public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }
    [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr hdc, int fmt, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr hdc);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    public static float[] M(System.Numerics.Matrix4x4 m) => new[]
    {
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44,
    };

    public static uint UploadTexture(int w, int h, byte[] rgba, bool repeat = true)
    {
        var t = new uint[1];
        glGenTextures(1, t);
        glBindTexture(TEXTURE_2D, t[0]);
        glTexParameteri(TEXTURE_2D, GENERATE_MIPMAP, 1);
        glTexParameteri(TEXTURE_2D, TEXTURE_MIN_FILTER, LINEAR_MIPMAP_LINEAR);
        glTexParameteri(TEXTURE_2D, TEXTURE_MAG_FILTER, LINEAR);
        glTexParameteri(TEXTURE_2D, TEXTURE_WRAP_S, repeat ? REPEAT : CLAMP_TO_EDGE);
        glTexParameteri(TEXTURE_2D, TEXTURE_WRAP_T, repeat ? REPEAT : CLAMP_TO_EDGE);
        glTexParameterf(TEXTURE_2D, TEXTURE_MAX_ANISOTROPY, 8f);
        glTexImage2D(TEXTURE_2D, 0, (int)RGBA, w, h, 0, RGBA, UNSIGNED_BYTE, rgba);
        return t[0];
    }
}
