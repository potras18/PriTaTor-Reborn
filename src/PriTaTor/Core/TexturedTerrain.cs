// Draws a map from straight above with its real textures, baked lighting, lightmaps and transparency.
// Servers don't normally hold textures. They work when copied into the server's Field folder, or from a client
// folder chosen in the File menu. Maps without enough of them use the plain terrain instead.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PriTaTor.Core
{
    public static class TexturedTerrain
    {
        const int MaterialSize = 320, MatGroupHeader = 88, VertexSize = 28, FaceSize = 28, TexLinkSize = 32;
        /// <summary>A map missing more of its textures than this looks better as plain terrain.</summary>
        const double MinimumFound = 0.5;

        sealed class Tex
        {
            public int[][] Mip;
            public int[] W, H;
            public bool HasAlpha;
        }

        sealed class Mat
        {
            public bool Hidden, Blended;
            public int TexCount, AnimCount, BlendType, UseState, LightScale;
            public double Transparency;
            public double[] Diffuse = new double[3];
            public int[] Stage = new int[8];
            public string Base, BaseAlpha, Light;
            public Tex BaseTex, LightTex;
            public int Faces;
        }

        static bool ValidName(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('.') < 0) return false;
            foreach (char c in s) if (c < 32 || c >= 127) return false;
            return true;
        }

        static Tex BuildMips(int[] argb, int w, int h)
        {
            var mips = new List<int[]> { argb };
            var ws = new List<int> { w };
            var hs = new List<int> { h };
            bool alpha = false;
            foreach (int p in argb) if ((p >> 24 & 255) < 250) { alpha = true; break; }
            while (w > 1 || h > 1)
            {
                int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
                var n = new int[nw * nh];
                int[] s = mips[mips.Count - 1];
                for (int y = 0; y < nh; y++)
                {
                    int y0 = Math.Min(y * 2, h - 1), y1 = Math.Min(y * 2 + 1, h - 1);
                    for (int x = 0; x < nw; x++)
                    {
                        int x0 = Math.Min(x * 2, w - 1), x1 = Math.Min(x * 2 + 1, w - 1);
                        int p0 = s[y0 * w + x0], p1 = s[y0 * w + x1], p2 = s[y1 * w + x0], p3 = s[y1 * w + x1];
                        // weighted by alpha, so the colour hidden in transparent pixels can't bleed in
                        int a0 = p0 >> 24 & 255, a1 = p1 >> 24 & 255, a2 = p2 >> 24 & 255, a3 = p3 >> 24 & 255;
                        int sum = a0 + a1 + a2 + a3;
                        if (sum == 0) continue;
                        int r = ((p0 >> 16 & 255) * a0 + (p1 >> 16 & 255) * a1 + (p2 >> 16 & 255) * a2 + (p3 >> 16 & 255) * a3) / sum;
                        int g = ((p0 >> 8 & 255) * a0 + (p1 >> 8 & 255) * a1 + (p2 >> 8 & 255) * a2 + (p3 >> 8 & 255) * a3) / sum;
                        int b = ((p0 & 255) * a0 + (p1 & 255) * a1 + (p2 & 255) * a2 + (p3 & 255) * a3) / sum;
                        n[y * nw + x] = (sum / 4) << 24 | r << 16 | g << 8 | b;
                    }
                }
                mips.Add(n); ws.Add(nw); hs.Add(nh);
                w = nw; h = nh;
            }
            return new Tex { Mip = mips.ToArray(), W = ws.ToArray(), H = hs.ToArray(), HasAlpha = alpha };
        }

        static int[] Pixels(Bitmap b)
        {
            BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var a = new int[b.Width * b.Height];
            try { Marshal.Copy(d.Scan0, a, 0, a.Length); }
            finally { b.UnlockBits(d); }
            return a;
        }

        static Tex LoadTexture(string name, string alphaName, Func<string, string> resolve)
        {
            string path = resolve(name);
            if (path == null) return null;
            try
            {
                using (Bitmap b = GameImage.Load(path))
                {
                    int[] px = Pixels(b);
                    // a material naming its own file as the alpha map means the image's real alpha channel
                    if (!string.IsNullOrEmpty(alphaName) && !string.Equals(alphaName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        string ap = resolve(alphaName);
                        if (ap != null)
                            using (Bitmap am = GameImage.Load(ap))
                                if (am.Width == b.Width && am.Height == b.Height)
                                {
                                    int[] apx = Pixels(am);
                                    bool own = false;
                                    foreach (int q in apx) if ((q >> 24 & 255) < 250) { own = true; break; }
                                    for (int i = 0; i < px.Length; i++)
                                        px[i] = (px[i] & 0xFFFFFF) | ((own ? apx[i] >> 24 & 255 : apx[i] >> 8 & 255) << 24);
                                }
                    }
                    return BuildMips(px, b.Width, b.Height);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is OutOfMemoryException || ex is ExternalException)
            {
                return null;
            }
        }

        static void Sample(Tex t, int level, double u, double v, out double a, out double r, out double g, out double b)
        {
            if (level >= t.Mip.Length) level = t.Mip.Length - 1;
            int w = t.W[level], h = t.H[level];
            int[] s = t.Mip[level];
            double fx = (u - Math.Floor(u)) * w - 0.5, fy = (v - Math.Floor(v)) * h - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double dx = fx - x0, dy = fy - y0;
            int x1 = x0 + 1, y1 = y0 + 1;
            x0 = (x0 % w + w) % w; x1 = (x1 % w + w) % w; y0 = (y0 % h + h) % h; y1 = (y1 % h + h) % h;
            int p00 = s[y0 * w + x0], p10 = s[y0 * w + x1], p01 = s[y1 * w + x0], p11 = s[y1 * w + x1];
            double w00 = (1 - dx) * (1 - dy), w10 = dx * (1 - dy), w01 = (1 - dx) * dy, w11 = dx * dy;
            a = ((p00 >> 24 & 255) * w00 + (p10 >> 24 & 255) * w10 + (p01 >> 24 & 255) * w01 + (p11 >> 24 & 255) * w11) / 255.0;
            r = ((p00 >> 16 & 255) * w00 + (p10 >> 16 & 255) * w10 + (p01 >> 16 & 255) * w01 + (p11 >> 16 & 255) * w11) / 255.0;
            g = ((p00 >> 8 & 255) * w00 + (p10 >> 8 & 255) * w10 + (p01 >> 8 & 255) * w01 + (p11 >> 8 & 255) * w11) / 255.0;
            b = ((p00 & 255) * w00 + (p10 & 255) * w10 + (p01 & 255) * w01 + (p11 & 255) * w11) / 255.0;
        }

        static int Level(double u0, double v0, double u1, double v1, double u2, double v2, Tex t, double pixelArea)
        {
            double texels = Math.Abs((u1 - u0) * (v2 - v0) - (u2 - u0) * (v1 - v0)) * t.W[0] * t.H[0];
            double rho = Math.Sqrt(texels / pixelArea);
            return rho > 1 ? (int)Math.Round(Math.Log(rho, 2)) : 0;
        }

        /// <summary>The picture, or null when too few of the map's textures can be found.</summary>
        public static Bitmap Draw(string smdPath, StageMesh mesh, Func<string, string> resolve, int longest)
        {
            byte[] b = File.ReadAllBytes(smdPath);
            int nV = BitConverter.ToInt32(b, 0x40260), nF = BitConverter.ToInt32(b, 0x40264), nT = BitConverter.ToInt32(b, 0x40268), nMat = BitConverter.ToInt32(b, 0x402A8);
            uint basePtr = BitConverter.ToUInt32(b, 0x4024C);
            int off = 0x402A0 + MatGroupHeader;
            var mats = new Mat[nMat];
            Encoding latin1 = Encoding.GetEncoding(28591);
            for (int i = 0; i < nMat; i++)
            {
                int inUse = BitConverter.ToInt32(b, off);
                var m = new Mat
                {
                    TexCount = BitConverter.ToInt32(b, off + 4),
                    BlendType = BitConverter.ToInt32(b, off + 116),
                    Transparency = BitConverter.ToSingle(b, off + 144),
                    UseState = BitConverter.ToInt32(b, off + 164),
                    AnimCount = BitConverter.ToInt32(b, off + 304)
                };
                int type = BitConverter.ToInt32(b, off + 112);
                for (int k = 0; k < 3; k++) m.Diffuse[k] = BitConverter.ToSingle(b, off + 132 + 4 * k);
                for (int k = 0; k < 8; k++) m.Stage[k] = BitConverter.ToInt32(b, off + 40 + 4 * k);
                m.Hidden = (m.UseState & 0x400) != 0 || (m.TexCount == 0 && m.AnimCount == 0);
                off += MaterialSize;
                if (inUse != 0)
                {
                    int sz = BitConverter.ToInt32(b, off);
                    off += 4;
                    string[] names = latin1.GetString(b, off, sz).Split('\0');
                    off += sz;
                    Func<int, string> at = k => k < names.Length ? names[k] : "";
                    // texture pairs (name, alpha map) come first, then animation frames
                    int animAt = Math.Min(m.TexCount, 64) * 2;
                    if (type == 1 && m.AnimCount > 0)
                    {
                        m.Base = at(animAt);
                        m.BaseAlpha = at(animAt + 1);
                    }
                    else if (m.TexCount > 0)
                    {
                        m.Base = at(0);
                        m.BaseAlpha = at(1);
                    }
                    if (m.TexCount > 1 && m.Stage[1] >= 4 && m.Stage[1] <= 6)
                    {
                        m.Light = at(2);
                        m.LightScale = m.Stage[1] == 4 ? 1 : m.Stage[1] == 5 ? 2 : 4;
                    }
                    if (!ValidName(m.Base)) m.Base = null;
                    if (!ValidName(m.BaseAlpha)) m.BaseAlpha = null;   // includes the game's "alpha is in the image" marker
                    if (!ValidName(m.Light)) m.Light = null;
                }
                mats[i] = m;
            }
            int vertexOff = off, faceOff = vertexOff + nV * VertexSize, linkOff = faceOff + nF * FaceSize;
            if (linkOff + (long)nT * TexLinkSize > b.Length) throw new InvalidDataException(smdPath + " is cut short.");

            for (int f = 0; f < nF; f++)
            {
                int m = BitConverter.ToUInt16(b, faceOff + f * FaceSize + 14);
                if (m < nMat) mats[m].Faces++;
            }

            // does this server have enough of what the map needs?
            var cache = new Dictionary<string, Tex>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int found = 0;
            foreach (Mat m in mats)
                if (m.Faces > 0 && !m.Hidden && m.Base != null && wanted.Add(m.Base) && resolve(m.Base) != null) found++;
            if (wanted.Count == 0 || found < wanted.Count * MinimumFound) return null;

            Func<string, string, Tex> get = (n, a) =>
            {
                if (n == null) return null;
                string k = n + "|" + a;
                if (!cache.TryGetValue(k, out Tex t)) cache[k] = t = LoadTexture(n, a, resolve);
                return t;
            };
            foreach (Mat m in mats)
            {
                if (m.Faces == 0 || m.Hidden) continue;
                m.BaseTex = get(m.Base, m.BaseAlpha);
                m.LightTex = get(m.Light, null);
                m.Blended = m.BlendType >= 2 || m.Transparency > 0.01 || (m.BaseTex != null && m.BaseTex.HasAlpha);   // type 1 is the normal default
            }

            // this matches the plain terrain and the map's bounds exactly
            double minX = mesh.MinX, maxZ = mesh.MaxZ;
            double ww = Math.Max(1, mesh.MaxX - mesh.MinX), wh = Math.Max(1, mesh.MaxZ - mesh.MinZ), scale = longest / Math.Max(ww, wh);
            int W = Math.Max(1, (int)Math.Round(ww * scale)), H = Math.Max(1, (int)Math.Round(wh * scale));
            double sx = W / ww, sy = H / wh;
            var depth = new float[W * H];
            var cr = new float[W * H]; var cg = new float[W * H]; var cb = new float[W * H];
            for (int i = 0; i < depth.Length; i++) { depth[i] = float.NegativeInfinity; cr[i] = 0.078f; cg[i] = 0.09f; cb[i] = 0.11f; }

            var solid = new List<int>();
            var blended = new List<KeyValuePair<double, int>>();
            for (int f = 0; f < nF; f++)
            {
                int fo = faceOff + f * FaceSize;
                int va = BitConverter.ToUInt16(b, fo + 8), vb = BitConverter.ToUInt16(b, fo + 10), vc = BitConverter.ToUInt16(b, fo + 12), mi = BitConverter.ToUInt16(b, fo + 14);
                if (va >= nV || vb >= nV || vc >= nV || mi >= nMat || mats[mi].Hidden) continue;
                if (!mats[mi].Blended) { solid.Add(f); continue; }
                double height = (double)BitConverter.ToInt32(b, vertexOff + va * VertexSize + 12) + BitConverter.ToInt32(b, vertexOff + vb * VertexSize + 12) + BitConverter.ToInt32(b, vertexOff + vc * VertexSize + 12);
                blended.Add(new KeyValuePair<double, int>(height, f));
            }
            blended.Sort((p, q) => p.Key.CompareTo(q.Key));   // low to high, so upper layers blend over lower ones

            Action<int, bool> raster = (f, isBlend) =>
            {
                int fo = faceOff + f * FaceSize;
                int va = BitConverter.ToUInt16(b, fo + 8), vb = BitConverter.ToUInt16(b, fo + 10), vc = BitConverter.ToUInt16(b, fo + 12);
                Mat L = mats[BitConverter.ToUInt16(b, fo + 14)];
                int oa = vertexOff + va * VertexSize, ob = vertexOff + vb * VertexSize, oc = vertexOff + vc * VertexSize;
                double ax = (BitConverter.ToInt32(b, oa + 8) / 256.0 - minX) * sx, ay = (maxZ - BitConverter.ToInt32(b, oa + 16) / 256.0) * sy, ah = BitConverter.ToInt32(b, oa + 12) / 256.0;
                double bx = (BitConverter.ToInt32(b, ob + 8) / 256.0 - minX) * sx, by = (maxZ - BitConverter.ToInt32(b, ob + 16) / 256.0) * sy, bh = BitConverter.ToInt32(b, ob + 12) / 256.0;
                double cx = (BitConverter.ToInt32(b, oc + 8) / 256.0 - minX) * sx, cy = (maxZ - BitConverter.ToInt32(b, oc + 16) / 256.0) * sy, ch = BitConverter.ToInt32(b, oc + 12) / 256.0;
                double d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
                if (Math.Abs(d) < 1e-9) return;

                // first two texture layers of this face
                var uv = new float[12];
                int layers = 0;
                uint ptr = BitConverter.ToUInt32(b, fo + 16);
                while (ptr != 0 && layers < 2)
                {
                    long rel = (long)ptr - basePtr;
                    if (rel < 0 || rel % TexLinkSize != 0 || rel / TexLinkSize >= nT) break;
                    int lo = linkOff + (int)rel;
                    for (int k = 0; k < 6; k++) uv[layers * 6 + k] = BitConverter.ToSingle(b, lo + 4 * k);
                    ptr = BitConverter.ToUInt32(b, lo + 28);
                    layers++;
                }
                bool hasBase = layers > 0 && L.BaseTex != null;
                bool hasLight = layers > 1 && L.LightTex != null;
                int level0 = hasBase ? Level(uv[0], uv[3], uv[1], uv[4], uv[2], uv[5], L.BaseTex, Math.Abs(d)) : 0;
                int level1 = hasLight ? Level(uv[6], uv[9], uv[7], uv[10], uv[8], uv[11], L.LightTex, Math.Abs(d)) : 0;
                double ra = BitConverter.ToInt16(b, oa + 20), ga = BitConverter.ToInt16(b, oa + 22), ba = BitConverter.ToInt16(b, oa + 24);
                double rb = BitConverter.ToInt16(b, ob + 20), gb = BitConverter.ToInt16(b, ob + 22), bb = BitConverter.ToInt16(b, ob + 24);
                double rc = BitConverter.ToInt16(b, oc + 20), gc = BitConverter.ToInt16(b, oc + 22), bc = BitConverter.ToInt16(b, oc + 24);
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), y1 = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                double opacity = 1 - L.Transparency;
                // water: from straight above, what's under it shouldn't dominate
                if ((L.UseState & 0x10200) != 0) opacity = Math.Max(opacity, 0.9);
                int blend = L.BlendType;
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
                        double h = w0 * ah + w1 * bh + w2 * ch;
                        int i = y * W + x;
                        if (isBlend ? h < depth[i] - 2 : h < depth[i]) continue;
                        double a = 1, r = L.Diffuse[0], g = L.Diffuse[1], bl = L.Diffuse[2];
                        if (hasBase) Sample(L.BaseTex, level0, w0 * uv[0] + w1 * uv[1] + w2 * uv[2], w0 * uv[3] + w1 * uv[4] + w2 * uv[5], out a, out r, out g, out bl);
                        if (hasLight)
                        {
                            Sample(L.LightTex, level1, w0 * uv[6] + w1 * uv[7] + w2 * uv[8], w0 * uv[9] + w1 * uv[10] + w2 * uv[11], out double la, out double lr, out double lg, out double lb);
                            r *= lr * L.LightScale; g *= lg * L.LightScale; bl *= lb * L.LightScale;
                        }
                        if (blend == 4 || blend == 5)
                        {
                            // lamps and glow add light
                            cr[i] = (float)Math.Min(1, cr[i] + r * a * opacity);
                            cg[i] = (float)Math.Min(1, cg[i] + g * a * opacity);
                            cb[i] = (float)Math.Min(1, cb[i] + bl * a * opacity);
                            continue;
                        }
                        if (blend == 3)
                        {
                            float k = (float)(1 - (1 - (r + g + bl) / 3) * opacity);
                            cr[i] *= k; cg[i] *= k; cb[i] *= k;
                            continue;
                        }
                        // baked lighting: vertex colours (sDef_Color / 255)
                        r = Math.Min(1, r * Math.Max(0, w0 * ra + w1 * rb + w2 * rc) / 255.0);
                        g = Math.Min(1, g * Math.Max(0, w0 * ga + w1 * gb + w2 * gc) / 255.0);
                        bl = Math.Min(1, bl * Math.Max(0, w0 * ba + w1 * bb + w2 * bc) / 255.0);
                        double al = isBlend ? a * opacity : 1;
                        if (al < 0.02) continue;
                        cr[i] = (float)(cr[i] + (r - cr[i]) * al);
                        cg[i] = (float)(cg[i] + (g - cg[i]) * al);
                        cb[i] = (float)(cb[i] + (bl - cb[i]) * al);
                        if (al > 0.5) depth[i] = (float)h;
                    }
                }
            };
            foreach (int f in solid) raster(f, false);
            foreach (KeyValuePair<double, int> kv in blended) raster(kv.Value, true);

            var pix = new int[W * H];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = unchecked((int)0xFF000000) | (int)(Math.Min(1, cr[i]) * 255) << 16 | (int)(Math.Min(1, cg[i]) * 255) << 8 | (int)(Math.Min(1, cb[i]) * 255);
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pix, 0, bd.Scan0, pix.Length); }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }
    }
}
