// Minimap loading and the terrain picture.
//
// The game scrambles BMP and TGA headers: 'G8' replaces the first two bytes of a TGA and then
// byte[i] += i*i for i in 2..17 ('A8' / 2..13 for BMP). See SMD-ANIM src\PTSmd\Textures.cs.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PriTaTor.Core
{
    public static class GameImage
    {
        public static void Descramble(byte[] d)
        {
            if (d.Length < 18) return;
            if (d[0] == 'G' && d[1] == '8')
            {
                d[0] = 0; d[1] = 0;
                for (int i = 2; i < 18; i++) d[i] = (byte)(d[i] - i * i);
            }
            else if (d[0] == 'A' && d[1] == '8')
            {
                d[0] = (byte)'B'; d[1] = (byte)'M';
                for (int i = 2; i < 14; i++) d[i] = (byte)(d[i] - i * i);
            }
        }

        public static Bitmap Load(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            Descramble(d);
            if (d.Length > 2 && d[0] == 'B' && d[1] == 'M')
                using (var ms = new MemoryStream(d))
                using (var img = new Bitmap(ms))
                    return new Bitmap(img);
            return DecodeTga(d);
        }

        static Bitmap DecodeTga(byte[] d)
        {
            if (d.Length < 18) throw new InvalidDataException("TGA file is too short.");
            int idLen = d[0], type = d[2];
            int w = BitConverter.ToUInt16(d, 12), h = BitConverter.ToUInt16(d, 14);
            int bpp = d[16], desc = d[17];
            if (w == 0 || h == 0 || (type != 2 && type != 3 && type != 10 && type != 11)) throw new InvalidDataException("Unsupported TGA type " + type + ".");
            if (bpp != 8 && bpp != 16 && bpp != 24 && bpp != 32) throw new InvalidDataException("Unsupported TGA depth " + bpp + ".");
            int bytes = bpp / 8, p = 18 + idLen + (d[1] != 0 ? BitConverter.ToUInt16(d, 5) * (d[7] / 8) : 0);
            var argb = new int[w * h];
            bool rle = type >= 9, topDown = (desc & 0x20) != 0;
            int count = w * h, i = 0;
            Func<int, int> read = q =>
            {
                switch (bytes)
                {
                    case 1: { int g = d[q]; return unchecked((int)0xFF000000) | g << 16 | g << 8 | g; }
                    case 2:
                    {
                        int v = BitConverter.ToUInt16(d, q);
                        return unchecked((int)0xFF000000) | ((v >> 10) & 31) * 255 / 31 << 16 | ((v >> 5) & 31) * 255 / 31 << 8 | (v & 31) * 255 / 31;
                    }
                    case 3: return unchecked((int)0xFF000000) | d[q + 2] << 16 | d[q + 1] << 8 | d[q];
                    default: return d[q + 3] << 24 | d[q + 2] << 16 | d[q + 1] << 8 | d[q];
                }
            };
            if (!rle)
            {
                if (p + (long)count * bytes > d.Length) throw new InvalidDataException("TGA data is cut short.");
                for (; i < count; i++, p += bytes) argb[i] = read(p);
            }
            else
            {
                while (i < count && p < d.Length)
                {
                    int head = d[p++], n = (head & 127) + 1;
                    if (p + bytes > d.Length) break;
                    if ((head & 128) != 0)
                    {
                        int v = read(p);
                        p += bytes;
                        for (int k = 0; k < n && i < count; k++) argb[i++] = v;
                    }
                    else
                        for (int k = 0; k < n && i < count && p + bytes <= d.Length; k++, p += bytes) argb[i++] = read(p);
                }
            }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(argb, (topDown ? y : h - 1 - y) * w, bd.Scan0 + y * bd.Stride, w);
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }
    }

    public sealed class TerrainRaster
    {
        public int W, H;
        public float[] Height;    // NaN where there is no terrain
        public byte[] Kind;

        public static TerrainRaster Build(StageMesh m, int longest)
        {
            double ww = Math.Max(1, m.MaxX - m.MinX), wh = Math.Max(1, m.MaxZ - m.MinZ);
            double s = longest / Math.Max(ww, wh);
            int w = Math.Max(1, (int)Math.Round(ww * s)), h = Math.Max(1, (int)Math.Round(wh * s));
            var r = new TerrainRaster { W = w, H = h, Height = new float[w * h], Kind = new byte[w * h] };
            for (int i = 0; i < r.Height.Length; i++) r.Height[i] = float.NaN;
            double sx = w / ww, sy = h / wh;
            bool any = false;
            for (int pass = 0; pass < 2 && !any; pass++)
            {
                for (int f = 0; f < m.FaceCount; f++)
                {
                    FaceKind k = m.Kind[f];
                    if (pass == 0 && (k == FaceKind.Wall || k == FaceKind.Pass)) continue;
                    any = true;
                    Raster(r, m, f, sx, sy, k);
                }
            }
            return r;
        }

        static void Raster(TerrainRaster r, StageMesh m, int f, double sx, double sy, FaceKind k)
        {
            double ax = (m.XI[m.A[f]] / 256.0 - m.MinX) * sx, ay = (m.MaxZ - m.ZI[m.A[f]] / 256.0) * sy, ah = m.YI[m.A[f]] / 256.0;
            double bx = (m.XI[m.B[f]] / 256.0 - m.MinX) * sx, by = (m.MaxZ - m.ZI[m.B[f]] / 256.0) * sy, bh = m.YI[m.B[f]] / 256.0;
            double cx = (m.XI[m.C[f]] / 256.0 - m.MinX) * sx, cy = (m.MaxZ - m.ZI[m.C[f]] / 256.0) * sy, ch = m.YI[m.C[f]] / 256.0;
            double d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(d) < 1e-9) return;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
            int x1 = Math.Min(r.W - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
            int y1 = Math.Min(r.H - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            for (int y = y0; y <= y1; y++)
            {
                double py = y + 0.5;
                for (int x = x0; x <= x1; x++)
                {
                    double px = x + 0.5;
                    double w0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d;
                    double w1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d;
                    double w2 = 1 - w0 - w1;
                    if (w0 < -0.02 || w1 < -0.02 || w2 < -0.02) continue;
                    float v = (float)(w0 * ah + w1 * bh + w2 * ch);
                    int i = y * r.W + x;
                    if (float.IsNaN(r.Height[i]) || v > r.Height[i])
                    {
                        r.Height[i] = v;
                        r.Kind[i] = (byte)k;
                    }
                }
            }
        }

        /// <summary>Hill-shaded picture: exact by construction since it comes from the map's own vertices.</summary>
        public Bitmap ToBitmap()
        {
            var values = new float[Height.Length];
            int n = 0;
            for (int i = 0; i < Height.Length; i += 7) if (!float.IsNaN(Height[i])) values[n++] = Height[i];
            float lo = 0, hi = 1;
            if (n > 0)
            {
                Array.Sort(values, 0, n);
                lo = values[n / 50]; hi = values[n - 1 - n / 50];
                if (hi - lo < 1) hi = lo + 1;
            }
            var px = new int[W * H];
            int w = W, h = H;
            float[] H0 = Height;
            byte[] K0 = Kind;
            float rangeLo = lo, range = hi - lo;
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float v = H0[i];
                    if (float.IsNaN(v)) { px[i] = unchecked((int)0xFF14171C); continue; }
                    float l = x > 0 && !float.IsNaN(H0[i - 1]) ? H0[i - 1] : v;
                    float t = y > 0 && !float.IsNaN(H0[i - w]) ? H0[i - w] : v;
                    double shade = 1.0 + Math.Max(-0.55, Math.Min(0.55, ((l - v) + (t - v)) * -0.035));
                    double a = Math.Max(0, Math.Min(1, (v - rangeLo) / range));
                    double rr, gg, bb;
                    if (K0[i] == (byte)FaceKind.Water) { rr = 40; gg = 92; bb = 140; }
                    else
                    {
                        // dark moss through ochre to pale stone
                        if (a < 0.5) { double q = a * 2; rr = 46 + 72 * q; gg = 66 + 60 * q; bb = 52 + 16 * q; }
                        else { double q = (a - 0.5) * 2; rr = 118 + 88 * q; gg = 126 + 78 * q; bb = 68 + 118 * q; }
                    }
                    int R = Clamp8(rr * shade), G = Clamp8(gg * shade), B = Clamp8(bb * shade);
                    px[i] = unchecked((int)0xFF000000) | R << 16 | G << 8 | B;
                }
            });
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(px, 0, bd.Scan0, px.Length); }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        static int Clamp8(double v) => v < 0 ? 0 : v > 255 ? 255 : (int)v;
    }
}
