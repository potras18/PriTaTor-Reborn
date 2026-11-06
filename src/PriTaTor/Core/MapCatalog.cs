// Finds every map on this server, works out where each one sits in the world, and remembers it
// in PriTaTor.ini in the server folder. Nothing about map positions is built into the program:
// it all comes from the .smd files found here, including moved copies such as old\forest\o_fore-1.smd.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PriTaTor.Core
{
    public sealed class TwinInfo
    {
        public string Stem;
        public int DX, DZ;      // add to this twin's positions to get positions on this map
        public bool Exact;      // same vertex and face counts
    }

    public sealed class MapEntry
    {
        public string Stem;
        public string SmdPath, RelPath;
        public long SmdSize, SmdTicks;
        public bool HasGeometry;
        public double MinX, MaxX, MinZ, MaxZ;
        public int Verts, Faces;
        public string SppPath, SpcPath, SpmPath;
        public string MinimapPath;
        // A minimap is the map's bounds stretched to fill the image. This agrees with hand-made calibrations
        // to about one pixel, so there's no automatic offset; these hold a position set by hand.
        public double? ManualDX, ManualDZ;
        public string ManualMinimap;
        public List<TwinInfo> Twins = new List<TwinInfo>();
        public bool? InServerExe;

        // refreshed on every scan
        public int FlagCount, FlagsOff, NpcCount, NpcsOff, OutsideCount;

        public string BaseFolder => Path.GetDirectoryName(RelPath ?? "") ?? "";
        public double MinimapDX => ManualDX ?? 0;
        public double MinimapDZ => ManualDZ ?? 0;
        public bool HasSpawnData => SppPath != null || SpcPath != null || SpmPath != null;
        public override string ToString() => Stem;
    }

    public sealed class Catalog
    {
        public string ServerRoot, FieldRoot, GameFieldRoot;
        public List<MapEntry> Maps = new List<MapEntry>();
        public string SettingsPath => Path.Combine(ServerRoot, "PriTaTor.ini");
        public bool GeneratedThisRun;

        public MapEntry Find(string stem) => Maps.FirstOrDefault(m => string.Equals(m.Stem, stem, StringComparison.OrdinalIgnoreCase));

        public static bool LooksLikeServer(string dir) => Directory.Exists(Path.Combine(dir, "GameServer", "Field"));

        public static string ReadFieldOverride(string serverRoot)
        {
            string ini = Path.Combine(serverRoot, "PriTaTor.ini");
            if (!File.Exists(ini)) return null;
            foreach (string l in File.ReadAllLines(ini, Encoding.UTF8))
                if (l.StartsWith("FieldPath=", StringComparison.OrdinalIgnoreCase))
                {
                    string v = l.Substring(10).Trim();
                    return v.Length > 0 ? v : null;
                }
            return null;
        }

        /// <param name="force">Ignore the saved settings and measure everything again.</param>
        public static Catalog Scan(string serverRoot, string fieldRoot, bool force, Action<string, int, int> progress = null)
        {
            var c = new Catalog
            {
                ServerRoot = serverRoot,
                GameFieldRoot = Path.Combine(serverRoot, "GameServer", "Field"),
                FieldRoot = fieldRoot ?? ReadFieldOverride(serverRoot) ?? Path.Combine(serverRoot, "Field")
            };
            progress?.Invoke("Looking for maps…", 0, 0);
            List<string> files = c.FindStageFiles();
            var saved = Ini.Read(c.SettingsPath);
            bool allCached = !force;
            var entries = new List<MapEntry>();
            foreach (string f in files)
            {
                var fi = new FileInfo(f);
                string rel = MakeRel(c.FieldRoot, f);
                var e = new MapEntry
                {
                    Stem = Path.GetFileNameWithoutExtension(f),
                    SmdPath = f,
                    RelPath = rel,
                    SmdSize = fi.Length,
                    SmdTicks = fi.LastWriteTimeUtc.Ticks,
                    HasGeometry = true
                };
                entries.Add(e);
                if (saved.TryGetValue("map " + rel, out var s))
                {
                    ApplyManual(e, s);
                    if (force || !ApplyCached(e, s)) allCached = false;
                }
                else allCached = false;
            }
            // a map was removed from the folder: its entry is stale
            if (allCached && saved.Keys.Count(k => k.StartsWith("map ", StringComparison.OrdinalIgnoreCase)) != entries.Count) allCached = false;

            // same stem twice (two folders): the first one found keeps the plain name
            foreach (var group in entries.GroupBy(e => e.Stem, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (MapEntry dup in group.Skip(1))
                    dup.Stem = dup.Stem + " (" + dup.BaseFolder + ")";

            c.Maps = entries;
            c.LinkSpawnFiles();
            c.LinkMinimaps();
            if (!allCached)
            {
                c.Generate(progress);
                c.GeneratedThisRun = true;
            }
            c.ReadServerExeMaps();
            c.RefreshCounts();
            c.Maps.Sort((a, b) => string.Compare(a.RelPath ?? a.Stem, b.RelPath ?? b.Stem, StringComparison.OrdinalIgnoreCase));
            if (!allCached) c.SaveSettings();
            return c;
        }

        List<string> FindStageFiles()
        {
            var r = new List<string>();
            if (!Directory.Exists(FieldRoot)) return r;
            foreach (string f in Directory.EnumerateFiles(FieldRoot, "*.smd", SearchOption.AllDirectories))
                if (StageMesh.IsStageFile(f)) r.Add(f);
            return r;
        }

        static string MakeRel(string root, string path)
        {
            string r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string p = Path.GetFullPath(path);
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? p.Substring(r.Length) : p;
        }

        static bool ApplyCached(MapEntry e, Dictionary<string, string> s)
        {
            try
            {
                if (long.Parse(s["SmdSize"]) != e.SmdSize || long.Parse(s["SmdTime"]) != e.SmdTicks) return false;
                double[] b = Ini.Doubles(s["Bounds"]);
                e.MinX = b[0]; e.MaxX = b[1]; e.MinZ = b[2]; e.MaxZ = b[3];
                e.Verts = int.Parse(s["Verts"]);
                e.Faces = int.Parse(s["Faces"]);
                e.Twins.Clear();
                if (s.TryGetValue("Twins", out string tw) && tw.Length > 0)
                    foreach (string t in tw.Split(';'))
                    {
                        string[] p = t.Split('|');
                        e.Twins.Add(new TwinInfo { Stem = p[0], DX = int.Parse(p[1]), DZ = int.Parse(p[2]), Exact = p[3] == "1" });
                    }
                return true;
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex is FormatException || ex is IndexOutOfRangeException)
            {
                return false;
            }
        }

        static void ApplyManual(MapEntry e, Dictionary<string, string> s)
        {
            if (s.TryGetValue("MinimapManual", out string man) && man.Length > 0)
            {
                double[] v = Ini.Doubles(man);
                if (v.Length == 2) { e.ManualDX = v[0]; e.ManualDZ = v[1]; }
            }
            if (s.TryGetValue("MinimapFile", out string mf) && mf.Length > 0) e.ManualMinimap = mf;
        }

        void LinkSpawnFiles()
        {
            if (!Directory.Exists(GameFieldRoot)) return;
            var seen = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (MapEntry m in Maps) if (!seen.ContainsKey(m.Stem)) seen[m.Stem] = m;
            foreach (string f in Directory.EnumerateFiles(GameFieldRoot))
            {
                Match x = Regex.Match(Path.GetFileName(f), @"^(.+?)\.ase\.(spm|spp|spc)$", RegexOptions.IgnoreCase);
                if (!x.Success) continue;
                string stem = x.Groups[1].Value;
                if (!seen.TryGetValue(stem, out MapEntry m))
                {
                    // spawn data with no map file in this Field folder
                    m = new MapEntry { Stem = stem, HasGeometry = false };
                    seen[stem] = m;
                    Maps.Add(m);
                }
                switch (x.Groups[2].Value.ToLowerInvariant())
                {
                    case "spp": m.SppPath = f; break;
                    case "spc": m.SpcPath = f; break;
                    default: m.SpmPath = f; break;
                }
            }
        }

        public string NewSpawnPath(MapEntry m, string ext) => Path.Combine(GameFieldRoot, m.Stem + ".ase." + ext);

        static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");

        /// <summary>Minimap file names don't always match: ice_2 / ice2.tga, dun-6a / dun-6.tga, lostisland / lost.tga.</summary>
        void LinkMinimaps()
        {
            string dir = Path.Combine(FieldRoot, "map");
            if (!Directory.Exists(dir)) return;
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var byNorm = new Dictionary<string, string>();
            foreach (string f in Directory.EnumerateFiles(dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".tga" && ext != ".bmp") continue;
                string n = Path.GetFileNameWithoutExtension(f);
                byName[n] = f;
                string k = Norm(n);
                if (!byNorm.ContainsKey(k)) byNorm[k] = f;
            }
            foreach (MapEntry m in Maps)
            {
                if (!m.HasGeometry) continue;
                if (m.ManualMinimap != null)
                {
                    string p = Path.Combine(dir, m.ManualMinimap);
                    if (File.Exists(p)) { m.MinimapPath = p; continue; }
                }
                m.MinimapPath = MatchMinimap(m.Stem, byName, byNorm);
            }
        }

        static string MatchMinimap(string stem, Dictionary<string, string> byName, Dictionary<string, string> byNorm)
        {
            if (byName.TryGetValue(stem, out string f)) return f;
            string n = Norm(stem);
            if (byNorm.TryGetValue(n, out f)) return f;
            // trailing variant letter: dun6a -> dun6
            if (n.Length > 2 && char.IsLetter(n[n.Length - 1]) && char.IsDigit(n[n.Length - 2]) && byNorm.TryGetValue(n.Substring(0, n.Length - 1), out f)) return f;
            // longest minimap name which starts this one: lostisland -> lost
            string best = null;
            foreach (string k in byNorm.Keys)
                if (k.Length >= 4 && n.StartsWith(k, StringComparison.Ordinal) && !char.IsDigit(n[k.Length]) && (best == null || k.Length > best.Length)) best = k;
            return best != null ? byNorm[best] : null;
        }

        /// <summary>Measure every map: world bounds, minimap fit, moved copies.</summary>
        void Generate(Action<string, int, int> progress)
        {
            List<MapEntry> real = Maps.Where(m => m.HasGeometry).ToList();
            var samples = new Dictionary<MapEntry, VertexSample>();
            int done = 0;
            object gate = new object();
            Parallel.ForEach(real, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) }, m =>
            {
                try
                {
                    StageMesh mesh = StageMesh.Load(m.SmdPath);
                    m.MinX = mesh.MinX; m.MaxX = mesh.MaxX; m.MinZ = mesh.MinZ; m.MaxZ = mesh.MaxZ;
                    m.Verts = mesh.VertexCount; m.Faces = mesh.FaceCount;
                    var sample = VertexSample.From(mesh);
                    lock (gate) samples[m] = sample;
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentOutOfRangeException)
                {
                    m.HasGeometry = false;
                }
                int n = Interlocked.Increment(ref done);
                progress?.Invoke("Measuring maps: " + m.Stem, n, real.Count);
            });
            FindTwins(samples);
        }

        void FindTwins(Dictionary<MapEntry, VertexSample> samples)
        {
            List<MapEntry> real = samples.Keys.ToList();
            foreach (MapEntry m in real) m.Twins.Clear();
            for (int i = 0; i < real.Count; i++)
                for (int j = i + 1; j < real.Count; j++)
                {
                    MapEntry a = real[i], b = real[j];
                    bool sameName = string.Equals(StripOld(a.Stem), StripOld(b.Stem), StringComparison.OrdinalIgnoreCase);
                    bool sameShape = a.Verts == b.Verts && a.Faces == b.Faces
                        && Math.Abs((a.MaxX - a.MinX) - (b.MaxX - b.MinX)) < 1 && Math.Abs((a.MaxZ - a.MinZ) - (b.MaxZ - b.MinZ)) < 1;
                    if (!sameName && !sameShape) continue;
                    if (!VertexSample.FindOffset(samples[a], samples[b], out int dx, out int dz)) continue;
                    if (dx == 0 && dz == 0) continue;
                    bool exact = a.Verts == b.Verts && a.Faces == b.Faces;
                    // a's positions + (dx,dz) = b's positions
                    a.Twins.Add(new TwinInfo { Stem = b.Stem, DX = -dx, DZ = -dz, Exact = exact });
                    b.Twins.Add(new TwinInfo { Stem = a.Stem, DX = dx, DZ = dz, Exact = exact });
                }
        }

        public static string StripOld(string s) => s.StartsWith("o_", StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s;

        public void ReadServerExeMaps()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool any = false;
            foreach (string exe in Directory.EnumerateFiles(ServerRoot, "*.exe"))
            {
                if (Path.GetFileName(exe).StartsWith("PriTaTor", StringComparison.OrdinalIgnoreCase)) continue;
                any = true;
                byte[] b = File.ReadAllBytes(exe);
                int start = -1;
                for (int i = 0; i <= b.Length; i++)
                {
                    bool printable = i < b.Length && b[i] >= 32 && b[i] < 127;
                    if (printable) { if (start < 0) start = i; continue; }
                    if (start >= 0 && i - start >= 6 && i - start < 200 &&
                        (b[i - 4] == '.') && (b[i - 3] | 32) == 'a' && (b[i - 2] | 32) == 's' && (b[i - 1] | 32) == 'e')
                    {
                        string s = Encoding.ASCII.GetString(b, start, i - start);
                        names.Add(Path.GetFileNameWithoutExtension(s.Replace('/', '\\')));
                    }
                    start = -1;
                }
            }
            foreach (MapEntry m in Maps) m.InServerExe = any ? names.Contains(m.Stem) : (bool?)null;
        }

        public void RefreshCounts() { foreach (MapEntry m in Maps) RefreshCounts(m); }

        public void RefreshCounts(MapEntry m)
        {
            m.FlagCount = m.FlagsOff = m.NpcCount = m.NpcsOff = m.OutsideCount = 0;
            try
            {
                if (m.SppPath != null)
                    foreach (SpawnFlag f in SppFile.Load(m.SppPath).Flags)
                    {
                        if (f.State != 0) m.FlagCount++; else m.FlagsOff++;
                        if (m.HasGeometry && Outside(m, f.X, f.Z)) m.OutsideCount++;
                    }
                if (m.SpcPath != null)
                    foreach (NpcRecord n in SpcFile.Load(m.SpcPath).Npcs)
                    {
                        if (n.Active) m.NpcCount++; else m.NpcsOff++;
                        if (m.HasGeometry && Outside(m, n.X, n.Z)) m.OutsideCount++;
                    }
            }
            catch (IOException) { }
        }

        public static bool Outside(MapEntry m, double x, double z) => x < m.MinX || x > m.MaxX || z < m.MinZ || z > m.MaxZ;

        public void SaveSettings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; PriTaTor server settings. Generated from this server's own map files, safe to delete:");
            sb.AppendLine("; it is rebuilt whenever a map file changes. Minimap positions you set by hand are kept here.");
            sb.AppendLine();
            sb.AppendLine("[server]");
            sb.AppendLine("Generated=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            string defaultField = Path.Combine(ServerRoot, "Field");
            sb.AppendLine("FieldPath=" + (string.Equals(Path.GetFullPath(FieldRoot).TrimEnd('\\'), Path.GetFullPath(defaultField).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "" : FieldRoot));
            foreach (MapEntry m in Maps.Where(x => x.HasGeometry))
            {
                sb.AppendLine();
                sb.AppendLine("[map " + m.RelPath + "]");
                sb.AppendLine("Name=" + m.Stem);
                sb.AppendLine("SmdSize=" + m.SmdSize);
                sb.AppendLine("SmdTime=" + m.SmdTicks);
                sb.AppendLine("Bounds=" + Ini.Join(m.MinX, m.MaxX, m.MinZ, m.MaxZ));
                sb.AppendLine("Centre=" + Ini.Join((m.MinX + m.MaxX) / 2, (m.MinZ + m.MaxZ) / 2));
                sb.AppendLine("Verts=" + m.Verts);
                sb.AppendLine("Faces=" + m.Faces);
                if (m.ManualDX != null) sb.AppendLine("MinimapManual=" + Ini.Join(m.ManualDX.Value, m.ManualDZ ?? 0));
                if (m.ManualMinimap != null) sb.AppendLine("MinimapFile=" + m.ManualMinimap);
                sb.AppendLine("Twins=" + string.Join(";", m.Twins.Select(t => t.Stem + "|" + t.DX + "|" + t.DZ + "|" + (t.Exact ? "1" : "0"))));
            }
            string tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            File.Move(tmp, SettingsPath);
        }
    }

    sealed class VertexSample
    {
        public int[] X, Z;
        public int MinX, MaxX, MinZ, MaxZ;

        public static VertexSample From(StageMesh m)
        {
            var s = new VertexSample { X = m.XI, Z = m.ZI };
            s.MinX = (int)Math.Round(m.MinX * 256); s.MaxX = (int)Math.Round(m.MaxX * 256);
            s.MinZ = (int)Math.Round(m.MinZ * 256); s.MaxZ = (int)Math.Round(m.MaxZ * 256);
            return s;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>
        /// A moved copy has the same vertices at a fixed offset, even when it's an older version of the map
        /// with most parts changed. Likely offsets are tried against every vertex; the one where most
        /// vertices land exactly on the other map's vertices is the move.
        /// </summary>
        public static bool FindOffset(VertexSample a, VertexSample b, out int dx, out int dz)
        {
            dx = dz = 0;
            int ddx = b.MinX - a.MinX, ddz = b.MinZ - a.MinZ;
            if (ddx == b.MaxX - a.MaxX && ddz == b.MaxZ - a.MaxZ && a.X.Length == b.X.Length)
            {
                dx = (int)Math.Round(ddx / 256.0);
                dz = (int)Math.Round(ddz / 256.0);
                return true;
            }
            var candidates = new HashSet<long>();
            // round moves: maps get shifted by round amounts such as 131072
            const int Round = 4096 * 256;
            long cdx = ((long)b.MinX + b.MaxX - a.MinX - a.MaxX) / 2, cdz = ((long)b.MinZ + b.MaxZ - a.MinZ - a.MaxZ) / 2;
            candidates.Add(Key((int)(Math.Round((double)cdx / Round) * Round), (int)(Math.Round((double)cdz / Round) * Round)));
            foreach (int x in new[] { b.MinX - a.MinX, b.MaxX - a.MaxX })
                foreach (int z in new[] { b.MinZ - a.MinZ, b.MaxZ - a.MaxZ })
                    candidates.Add(Key(x, z));
            // whichever offsets a sample of vertex pairs agree on
            var votes = new Dictionary<long, int>();
            int stepA = Math.Max(1, a.X.Length / 200), stepB = Math.Max(1, b.X.Length / 4000);
            for (int i = 0; i < a.X.Length; i += stepA)
                for (int j = 0; j < b.X.Length; j += stepB)
                {
                    long key = Key(b.X[j] - a.X[i], b.Z[j] - a.Z[i]);
                    votes.TryGetValue(key, out int v);
                    votes[key] = v + 1;
                }
            foreach (var kv in votes.OrderByDescending(k => k.Value).Take(8)) candidates.Add(kv.Key);

            var target = new HashSet<long>();
            for (int j = 0; j < b.X.Length; j++) target.Add(Key(b.X[j], b.Z[j]));
            int bestCount = 0;
            long best = 0;
            foreach (long c in candidates)
            {
                int ox = (int)(c >> 32), oz = (int)(uint)(c & 0xFFFFFFFF);
                int hits = 0;
                for (int i = 0; i < a.X.Length; i++)
                    if (target.Contains(Key(a.X[i] + ox, a.Z[i] + oz))) hits++;
                if (hits > bestCount) { bestCount = hits; best = c; }
            }
            if (bestCount < 20 || bestCount < a.X.Length / 100) return false;
            dx = (int)Math.Round((int)(best >> 32) / 256.0);
            dz = (int)Math.Round((int)(uint)(best & 0xFFFFFFFF) / 256.0);
            return true;
        }
    }

    public static class Ini
    {
        public static Dictionary<string, Dictionary<string, string>> Read(string path)
        {
            var r = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return r;
            Dictionary<string, string> cur = null;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l[0] == ';') continue;
                if (l[0] == '[' && l[l.Length - 1] == ']')
                {
                    cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    r[l.Substring(1, l.Length - 2)] = cur;
                    continue;
                }
                int e = l.IndexOf('=');
                if (e > 0 && cur != null) cur[l.Substring(0, e)] = l.Substring(e + 1);
            }
            return r;
        }

        public static string Join(params double[] v) => string.Join(",", v.Select(d => d.ToString("0.###", CultureInfo.InvariantCulture)));

        public static double[] Doubles(string s) =>
            s.Split(',').Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
    }
}
