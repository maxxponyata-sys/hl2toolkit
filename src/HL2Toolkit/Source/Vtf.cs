using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HL2Toolkit.Source;

/// <summary>Картинка RGBA (строки сверху вниз).</summary>
public sealed class RgbaImage
{
    public int W, H;
    public byte[] Pixels;          // RGBA
    public int SourceW, SourceH;   // реальный размер VTF (для масштаба текстуры на брашах)
}

public static class Vtf
{
    static int FmtSize(int fmt, int w, int h)
    {
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        return fmt switch
        {
            13 => bw * bh * 8,
            14 or 15 => bw * bh * 16,
            0 or 1 or 12 or 16 => w * h * 4,
            2 or 3 => w * h * 3,
            24 or 25 => w * h * 8,
            _ => -1
        };
    }

    /// <summary>Декодирует мип, ближайший к maxSize.</summary>
    public static RgbaImage ReadPreview(byte[] d, int maxSize = 256)
    {
        if (d == null || d.Length < 80 || d[0] != 'V' || d[1] != 'T' || d[2] != 'F') return null;
        int minor = BitConverter.ToInt32(d, 8), hsize = BitConverter.ToInt32(d, 12);
        int w = BitConverter.ToUInt16(d, 16), h = BitConverter.ToUInt16(d, 18);
        uint flags = BitConverter.ToUInt32(d, 20);
        int frames = BitConverter.ToUInt16(d, 24), first = BitConverter.ToUInt16(d, 26);
        int fmt = BitConverter.ToInt32(d, 52), mips = d[56], lfmt = BitConverter.ToInt32(d, 57);
        int lw = d[61], lh = d[62];
        int depth = minor >= 2 ? Math.Max(1, (int)BitConverter.ToUInt16(d, 63)) : 1;
        int faces = (flags & 0x4000) != 0 ? (minor < 5 && first == 0xFFFF ? 7 : 6) : 1;
        long start;
        if (minor >= 3)
        {
            int nres = BitConverter.ToInt32(d, 68);
            start = -1;
            for (int i = 0; i < nres; i++)
            {
                int o = 80 + i * 8;
                if (d[o] == 0x30 && d[o + 1] == 0 && d[o + 2] == 0) start = BitConverter.ToUInt32(d, o + 4);
            }
            if (start < 0) return null;
        }
        else
        {
            int low = lfmt != -1 && lw > 0 ? Math.Max(0, FmtSize(lfmt, lw, lh)) : 0;
            start = hsize + low;
        }
        if (FmtSize(fmt, 4, 4) < 0) return null;
        int pick = mips - 1;
        for (int mm = 0; mm < mips; mm++)
            if (Math.Max(w >> mm, 1) <= maxSize && Math.Max(h >> mm, 1) <= maxSize) { pick = mm; break; }
        long off = start;
        for (int mm = mips - 1; mm > pick; mm--)
            off += (long)FmtSize(fmt, Math.Max(1, w >> mm), Math.Max(1, h >> mm)) * frames * faces * depth;
        int pw = Math.Max(1, w >> pick), ph = Math.Max(1, h >> pick);
        if (off + FmtSize(fmt, pw, ph) > d.Length) return null;
        var px = Decode(fmt, d, (int)off, pw, ph);
        return px == null ? null : new RgbaImage { W = pw, H = ph, Pixels = px, SourceW = w, SourceH = h };
    }

    static byte[] Decode(int fmt, byte[] d, int p, int w, int h)
    {
        var o = new byte[w * h * 4];
        if (fmt is 13 or 14 or 15)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var cols = new byte[16];
            var alpha = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    bool hasA = false;
                    if (fmt == 15)
                    {
                        byte a0 = d[p], a1 = d[p + 1];
                        ulong bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (ulong)d[p + 2 + i] << (8 * i);
                        var al = new int[8]; al[0] = a0; al[1] = a1;
                        if (a0 > a1) for (int i = 1; i < 7; i++) al[i + 1] = ((7 - i) * a0 + i * a1) / 7;
                        else { for (int i = 1; i < 5; i++) al[i + 1] = ((5 - i) * a0 + i * a1) / 5; al[6] = 0; al[7] = 255; }
                        for (int i = 0; i < 16; i++) alpha[i] = (byte)al[(int)((bits >> (3 * i)) & 7)];
                        hasA = true; p += 8;
                    }
                    else if (fmt == 14)
                    {
                        ulong bits = BitConverter.ToUInt64(d, p);
                        for (int i = 0; i < 16; i++) alpha[i] = (byte)(((bits >> (4 * i)) & 15) * 17);
                        hasA = true; p += 8;
                    }
                    int c0 = BitConverter.ToUInt16(d, p), c1 = BitConverter.ToUInt16(d, p + 2);
                    uint idx = BitConverter.ToUInt32(d, p + 4);
                    p += 8;
                    static void Rgb(int c, byte[] t, int at) { t[at] = (byte)(((c >> 11) & 31) * 255 / 31); t[at + 1] = (byte)(((c >> 5) & 63) * 255 / 63); t[at + 2] = (byte)((c & 31) * 255 / 31); t[at + 3] = 255; }
                    Rgb(c0, cols, 0); Rgb(c1, cols, 4);
                    if (c0 > c1 || fmt != 13)
                        for (int k = 0; k < 3; k++) { cols[8 + k] = (byte)((2 * cols[k] + cols[4 + k]) / 3); cols[12 + k] = (byte)((cols[k] + 2 * cols[4 + k]) / 3); }
                    else
                        for (int k = 0; k < 3; k++) { cols[8 + k] = (byte)((cols[k] + cols[4 + k]) / 2); cols[12 + k] = 0; }
                    cols[11] = 255; cols[15] = (byte)(c0 > c1 || fmt != 13 ? 255 : 0);
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                        if (x >= w || y >= h) continue;
                        int ci = (int)((idx >> (2 * i)) & 3) * 4, oo = (y * w + x) * 4;
                        o[oo] = cols[ci]; o[oo + 1] = cols[ci + 1]; o[oo + 2] = cols[ci + 2];
                        o[oo + 3] = hasA ? alpha[i] : cols[ci + 3];
                    }
                }
            return o;
        }
        for (int i = 0; i < w * h; i++)
        {
            int q = i * 4;
            switch (fmt)
            {
                case 0: o[q] = d[p + q]; o[q + 1] = d[p + q + 1]; o[q + 2] = d[p + q + 2]; o[q + 3] = d[p + q + 3]; break;
                case 12: o[q] = d[p + q + 2]; o[q + 1] = d[p + q + 1]; o[q + 2] = d[p + q]; o[q + 3] = d[p + q + 3]; break;
                case 16: o[q] = d[p + q + 2]; o[q + 1] = d[p + q + 1]; o[q + 2] = d[p + q]; o[q + 3] = 255; break;
                case 1: o[q] = d[p + q + 3]; o[q + 1] = d[p + q + 2]; o[q + 2] = d[p + q + 1]; o[q + 3] = d[p + q]; break;
                case 2: { int s = p + i * 3; o[q] = d[s]; o[q + 1] = d[s + 1]; o[q + 2] = d[s + 2]; o[q + 3] = 255; break; }
                case 3: { int s = p + i * 3; o[q] = d[s + 2]; o[q + 1] = d[s + 1]; o[q + 2] = d[s]; o[q + 3] = 255; break; }
                default: return null;
            }
        }
        return o;
    }

    static int Pow2(int n) { int r = 4; while (r * 2 <= n && r < 1024) r *= 2; return r; }

    /// <summary>Записывает VTF 7.2 (BGRA8888, с мипмапами).</summary>
    public static void Write(string path, Bitmap src)
    {
        int w = Pow2(src.Width), h = Pow2(src.Height);
        var mips = new List<byte[]>();
        bool hasAlpha = false;
        int cw = w, ch = h;
        while (true)
        {
            using var bmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using var ia = new ImageAttributes();
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle(0, 0, cw, ch), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            var bd = bmp.LockBits(new Rectangle(0, 0, cw, ch), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[cw * ch * 4];   // память GDI+ уже в порядке BGRA
            for (int y = 0; y < ch; y++)
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, buf, y * cw * 4, cw * 4);
            bmp.UnlockBits(bd);
            if (mips.Count == 0) for (int i = 3; i < buf.Length; i += 4) if (buf[i] < 255) { hasAlpha = true; break; }
            mips.Add(buf);
            if (cw == 1 && ch == 1) break;
            cw = Math.Max(1, cw / 2); ch = Math.Max(1, ch / 2);
        }
        using var f = new BinaryWriter(File.Create(path));
        f.Write("VTF\0"u8.ToArray());
        f.Write(7); f.Write(2); f.Write(80);
        f.Write((ushort)w); f.Write((ushort)h);
        f.Write(hasAlpha ? 0x2000u : 0u);
        f.Write((ushort)1); f.Write((ushort)0);
        f.Write(0);
        f.Write(0.5f); f.Write(0.5f); f.Write(0.5f);
        f.Write(0);
        f.Write(1.0f);
        f.Write(12);                       // BGRA8888
        f.Write((byte)mips.Count);
        f.Write(-1);                       // нет low-res картинки
        f.Write((byte)0); f.Write((byte)0);
        f.Write((ushort)1);                // depth
        while (f.BaseStream.Position < 80) f.Write((byte)0);
        for (int i = mips.Count - 1; i >= 0; i--) f.Write(mips[i]);
    }
}
