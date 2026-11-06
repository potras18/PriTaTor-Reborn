// Reader for map terrain ("SMD Stage data Ver 0.72"), based on the layout worked out in the
// SMD-ANIM project (src\PTSmd\SmdFile.cs, Material.cs). Coordinates here stay in game axes:
// x and z across the map, y up, world unit = stored integer / 256.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PriTaTor.Core
{
    public enum FaceKind : byte
    {
        Solid = 0,   // walkable or visible geometry
        Wall = 1,    // invisible collision walls and helpers
        Pass = 2,    // visible, no collision (tree canopies, plants)
        Water = 3
    }

    public sealed class StageMesh
    {
        const int OffNVertex = 0x40260, OffNFace = 0x40264, OffMatGroup = 0x402A0, OffMatCount = 0x402A8;
        const int MatGroupHeader = 88, MaterialSize = 320;
        public static readonly byte[] Header = Encoding.ASCII.GetBytes("SMD Stage data Ver 0.72");

        public int[] XI, YI, ZI;   // raw fixed point
        public int[] A, B, C;
        public FaceKind[] Kind;
        public double MinX, MaxX, MinZ, MaxZ, MinY, MaxY;
        public int VertexCount => XI.Length;
        public int FaceCount => A.Length;

        public static bool IsStageFile(string path)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var h = new byte[Header.Length];
                    if (f.Read(h, 0, h.Length) != h.Length) return false;
                    for (int i = 0; i < h.Length; i++) if (h[i] != Header[i]) return false;
                    return true;
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static StageMesh Load(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            for (int i = 0; i < Header.Length; i++)
                if (i >= b.Length || b[i] != Header[i]) throw new InvalidDataException(path + " is not a map file.");
            int nV = BitConverter.ToInt32(b, OffNVertex), nF = BitConverter.ToInt32(b, OffNFace);
            int nMat = BitConverter.ToInt32(b, OffMatCount);
            if (nV < 0 || nF < 0 || nMat < 0 || nMat > 100000) throw new InvalidDataException(path + " has a bad header.");
            int off = OffMatGroup + MatGroupHeader;
            var kinds = new FaceKind[nMat];
            for (int m = 0; m < nMat; m++)
            {
                if (off + MaterialSize > b.Length) throw new InvalidDataException(path + ": material table is cut short.");
                int inUse = BitConverter.ToInt32(b, off);
                int texCount = BitConverter.ToInt32(b, off + 4);
                int useState = BitConverter.ToInt32(b, off + 164);
                int animCount = BitConverter.ToInt32(b, off + 304);
                if ((useState & 0x400) != 0 || (texCount == 0 && animCount == 0)) kinds[m] = FaceKind.Wall;
                else if ((useState & 0x200) != 0 || (useState & 0x10000) != 0) kinds[m] = FaceKind.Water;
                else if ((useState & 0x800) != 0) kinds[m] = FaceKind.Pass;
                else kinds[m] = FaceKind.Solid;
                off += MaterialSize;
                if (inUse != 0) off += 4 + BitConverter.ToInt32(b, off);
            }
            if (off + nV * 28L + nF * 28L > b.Length) throw new InvalidDataException(path + " is cut short.");
            var s = new StageMesh { XI = new int[nV], YI = new int[nV], ZI = new int[nV] };
            for (int i = 0; i < nV; i++, off += 28)
            {
                s.XI[i] = BitConverter.ToInt32(b, off + 8);
                s.YI[i] = BitConverter.ToInt32(b, off + 12);
                s.ZI[i] = BitConverter.ToInt32(b, off + 16);
            }
            var a = new int[nF]; var bb = new int[nF]; var c = new int[nF]; var k = new FaceKind[nF];
            int keep = 0;
            for (int i = 0; i < nF; i++, off += 28)
            {
                int fa = BitConverter.ToUInt16(b, off + 8), fb = BitConverter.ToUInt16(b, off + 10), fc = BitConverter.ToUInt16(b, off + 12);
                int mat = BitConverter.ToUInt16(b, off + 14);
                if (fa >= nV || fb >= nV || fc >= nV) continue;
                a[keep] = fa; bb[keep] = fb; c[keep] = fc;
                k[keep] = mat < nMat ? kinds[mat] : FaceKind.Solid;
                keep++;
            }
            Array.Resize(ref a, keep); Array.Resize(ref bb, keep); Array.Resize(ref c, keep); Array.Resize(ref k, keep);
            s.A = a; s.B = bb; s.C = c; s.Kind = k;
            s.ComputeBounds();
            return s;
        }

        /// <summary>Bounds of the vertices used by a face; the file also carries unused ones.</summary>
        void ComputeBounds()
        {
            var used = new bool[VertexCount];
            for (int i = 0; i < A.Length; i++) { used[A[i]] = true; used[B[i]] = true; used[C[i]] = true; }
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < used.Length; i++)
            {
                if (!used[i]) continue;
                if (XI[i] < minX) minX = XI[i];
                if (XI[i] > maxX) maxX = XI[i];
                if (ZI[i] < minZ) minZ = ZI[i];
                if (ZI[i] > maxZ) maxZ = ZI[i];
                if (YI[i] < minY) minY = YI[i];
                if (YI[i] > maxY) maxY = YI[i];
            }
            if (minX > maxX) { minX = maxX = minZ = maxZ = minY = maxY = 0; }
            MinX = minX / 256.0; MaxX = maxX / 256.0; MinZ = minZ / 256.0; MaxZ = maxZ / 256.0; MinY = minY / 256.0; MaxY = maxY / 256.0;
        }

        // ----- height lookup -----

        List<int>[] grid;
        int gridN;
        double cellX, cellZ;

        void BuildGrid()
        {
            gridN = 128;
            cellX = Math.Max(1, MaxX - MinX) / gridN;
            cellZ = Math.Max(1, MaxZ - MinZ) / gridN;
            var g = new List<int>[gridN * gridN];
            for (int f = 0; f < A.Length; f++)
            {
                if (Kind[f] == FaceKind.Wall) continue;
                double ax = XI[A[f]] / 256.0, bx = XI[B[f]] / 256.0, cx = XI[C[f]] / 256.0;
                double az = ZI[A[f]] / 256.0, bz = ZI[B[f]] / 256.0, cz = ZI[C[f]] / 256.0;
                int x0 = Clamp((int)((Math.Min(ax, Math.Min(bx, cx)) - MinX) / cellX));
                int x1 = Clamp((int)((Math.Max(ax, Math.Max(bx, cx)) - MinX) / cellX));
                int z0 = Clamp((int)((Math.Min(az, Math.Min(bz, cz)) - MinZ) / cellZ));
                int z1 = Clamp((int)((Math.Max(az, Math.Max(bz, cz)) - MinZ) / cellZ));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = z * gridN + x;
                        (g[i] ?? (g[i] = new List<int>(4))).Add(f);
                    }
            }
            grid = g;
        }

        int Clamp(int v) => v < 0 ? 0 : v >= gridN ? gridN - 1 : v;

        /// <summary>Heights of the surfaces at a point, highest first. Water and plants only count where nothing solid is there.</summary>
        public List<double> SurfacesAt(double x, double z)
        {
            var r = new List<double>();
            var soft = new List<double>();
            if (grid == null) lock (this) { if (grid == null) BuildGrid(); }
            if (x < MinX || x > MaxX || z < MinZ || z > MaxZ) return r;
            List<int> cell = grid[Clamp((int)((z - MinZ) / cellZ)) * gridN + Clamp((int)((x - MinX) / cellX))];
            if (cell == null) return r;
            foreach (int f in cell)
            {
                double ax = XI[A[f]] / 256.0, bx = XI[B[f]] / 256.0, cx = XI[C[f]] / 256.0;
                double az = ZI[A[f]] / 256.0, bz = ZI[B[f]] / 256.0, cz = ZI[C[f]] / 256.0;
                double d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
                if (Math.Abs(d) < 1e-6) continue;
                double w0 = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d;
                double w1 = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d;
                double w2 = 1 - w0 - w1;
                if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6) continue;
                double h = (w0 * YI[A[f]] + w1 * YI[B[f]] + w2 * YI[C[f]]) / 256.0;
                (Kind[f] == FaceKind.Solid ? r : soft).Add(h);
            }
            if (r.Count == 0) r = soft;
            r.Sort((p, q) => q.CompareTo(p));
            // merge surfaces that are effectively the same
            for (int i = r.Count - 1; i > 0; i--) if (r[i - 1] - r[i] < 0.5) r.RemoveAt(i);
            return r;
        }

        /// <summary>Ground height for a dropped NPC: the top surface, which matches 96% of stored NPCs.</summary>
        public double? GroundAt(double x, double z)
        {
            List<double> s = SurfacesAt(x, z);
            return s.Count > 0 ? s[0] : (double?)null;
        }
    }
}
