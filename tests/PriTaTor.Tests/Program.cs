// Checks the file handling against a real server. Run it on a COPY of a server's GameServer folder:
//   PriTaTor.Tests <copy of server root> <Field folder>
// It edits spawn and text files in the copy, and only ever reads the Field folder.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using PriTaTor.Core;

static class Program
{
    static int failures, passes;

    static void Check(bool ok, string what)
    {
        if (ok) { passes++; Console.WriteLine("  ok    " + what); }
        else { failures++; Console.WriteLine("  FAIL  " + what); }
    }

    /// <summary>Moves every vertex of a stage file, the way a map moved in the original tools would be.</summary>
    static void ShiftStage(string path, int dx, int dz)
    {
        byte[] b = File.ReadAllBytes(path);
        int nV = BitConverter.ToInt32(b, 0x40260), nMat = BitConverter.ToInt32(b, 0x402A8);
        int off = 0x402A0 + 88;
        for (int m = 0; m < nMat; m++)
        {
            int inUse = BitConverter.ToInt32(b, off);
            off += 320;
            if (inUse != 0) off += 4 + BitConverter.ToInt32(b, off);
        }
        for (int i = 0; i < nV; i++, off += 28)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.ToInt32(b, off + 8) + dx * 256), 0, b, off + 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.ToInt32(b, off + 16) + dz * 256), 0, b, off + 16, 4);
        }
        File.WriteAllBytes(path, b);
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string root = args[0], field = args[1];
        if (root.StartsWith(@"X:\pristontale", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Refusing to run on an original server folder."); return 2; }
        SafeWrite.ServerRoot = root;

        Console.WriteLine("== scan");
        var sw = Stopwatch.StartNew();
        Catalog cat = Catalog.Scan(root, field, true, null);
        long full = sw.ElapsedMilliseconds;
        sw.Restart();
        Catalog cat2 = Catalog.Scan(root, field, false, null);
        Console.WriteLine($"  full scan {full} ms, cached scan {sw.ElapsedMilliseconds} ms, maps {cat.Maps.Count}");
        Check(File.Exists(cat.SettingsPath), "settings file written");
        Check(!cat2.GeneratedThisRun, "second scan used the settings file");
        Check(cat.Maps.Count(m => m.HasGeometry) >= 52, "found all stage maps");

        Console.WriteLine("== terrain pictures");
        {
            string folder = TerrainCache.Root(cat);
            List<string> pngs = Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories).ToList();
            long bytes = pngs.Sum(f => new FileInfo(f).Length);
            Console.WriteLine($"  {pngs.Count} pictures, {bytes / (1024 * 1024)} MB");
            Check(pngs.Count == cat.Maps.Count(m => m.HasGeometry), "one picture for every map");
            Check(File.Exists(Path.Combine(folder, "old", "forest", "o_fore-1.png")), "pictures follow the Field folders");
            Check(cat2.MapsMeasured == 0 && cat2.MapsDrawn == 0, "the second start measured and drew nothing");
            MapEntry e = cat2.Find("fore-1");
            Check(TerrainCache.IsCurrent(cat2, e), "the saved settings know the picture is current");
            StageMesh mesh = StageMesh.Load(e.SmdPath);
            using (System.Drawing.Bitmap fromFile = TerrainCache.Load(cat2, e, mesh, out bool saved1))
            using (System.Drawing.Bitmap drawn = TerrainRaster.Build(mesh, TerrainCache.LongestSide).ToBitmap())
            {
                Check(!saved1, "loading a current picture doesn't draw it again");
                Check(fromFile.Width == drawn.Width && fromFile.Height == drawn.Height, "same size as a fresh drawing");
                int different = 0;
                for (int y = 0; y < drawn.Height; y += 37)
                    for (int x = 0; x < drawn.Width; x += 37)
                        if (fromFile.GetPixel(x, y).ToArgb() != drawn.GetPixel(x, y).ToArgb()) different++;
                Check(different == 0, "and the same pixels");
            }
            // a broken picture gets replaced rather than stopping the map opening
            string one = TerrainCache.PathFor(cat2, e);
            File.WriteAllText(one, "not a picture");
            using (System.Drawing.Bitmap fixedUp = TerrainCache.Load(cat2, e, mesh, out bool saved2))
                Check(saved2 && fixedUp.Width > 1000, "a damaged picture is redrawn");
            using (System.Drawing.Image again = System.Drawing.Image.FromFile(one))
                Check(again.Width > 1000, "and saved again");
            // a deleted picture costs one redraw, and no measuring
            File.Delete(one);
            Catalog cat3 = Catalog.Scan(root, field, false, null);
            Check(cat3.MapsMeasured == 0 && cat3.MapsDrawn == 1 && File.Exists(one), "a deleted picture is redrawn alone");
            DateTime pngTime = File.GetLastWriteTimeUtc(TerrainCache.PathFor(cat3, cat3.Find("fore-2")));
            Catalog cat4 = Catalog.Scan(root, field, false, null);
            Check(cat4.MapsDrawn == 0 && File.GetLastWriteTimeUtc(TerrainCache.PathFor(cat4, cat4.Find("fore-2"))) == pngTime, "starting again touches no pictures");
            Check(cat4.Find("o_fore-1").Twins.Any(t => t.Stem.Equals("fore-1", StringComparison.OrdinalIgnoreCase) && t.DX == -131072), "moved-copy results are kept between starts");
        }

        Console.WriteLine("== moved copies");
        foreach (MapEntry m in cat.Maps.Where(x => x.Stem.StartsWith("o_", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"  {m.Stem,-22} {m.RelPath,-42} minimap={(m.MinimapPath != null ? Path.GetFileName(m.MinimapPath) : "-"),-24} twins: {string.Join(", ", m.Twins.Select(t => t.Stem + " (" + t.DX + "," + t.DZ + (t.Exact ? ",same" : ",older") + ")"))}");
        var olds = cat.Maps.Where(x => x.Stem.StartsWith("o_", StringComparison.OrdinalIgnoreCase)).ToList();
        if (olds.Count > 0)
        {
            Check(olds.Count == 27, "picked up all 27 old\\ copies, got " + olds.Count);
            Check(olds.Where(m => !m.Stem.Equals("o_office", StringComparison.OrdinalIgnoreCase)).All(m => m.MinimapPath != null && Path.GetFileNameWithoutExtension(m.MinimapPath).StartsWith("o_", StringComparison.OrdinalIgnoreCase)), "every copy with a minimap on disk found its own o_ minimap");
            TwinInfo td = cat.Find("o_dun-3").Twins.FirstOrDefault(x => x.Stem.Equals("dun-3", StringComparison.OrdinalIgnoreCase));
            Check(td != null && td.DX == -131072 && td.DZ == 0, "o_dun-3, only 2% of whose vertices match dun-3, still gets -131072,0");
            MapEntry of1 = cat.Find("o_fore-1");
            TwinInfo t = of1.Twins.FirstOrDefault(x => x.Stem.Equals("fore-1", StringComparison.OrdinalIgnoreCase));
            Check(t != null && t.DX == -131072 && t.DZ == 0, "o_fore-1 offset from fore-1 is -131072,0 (an older version of the map)");
            MapEntry of2 = cat.Find("o_fore-2");
            TwinInfo t2 = of2.Twins.FirstOrDefault(x => x.Stem.Equals("fore-2", StringComparison.OrdinalIgnoreCase));
            Check(t2 != null && t2.DX == -131072 && t2.DZ == 0, "o_fore-2, whose bounds differ from fore-2, still gets -131072,0");
            MapEntry ov = cat.Find("o_village-2");
            TwinInfo t3 = ov.Twins.FirstOrDefault(x => x.Stem.Equals("village-2", StringComparison.OrdinalIgnoreCase));
            Check(t3 != null && t3.DX == -131072 && t3.DZ == -65536, "o_village-2 offset is -131072,-65536");
        }

        Console.WriteLine("== alignment");
        int flags = 0, outside = 0, npcs = 0;
        foreach (MapEntry m in cat.Maps) { flags += m.FlagCount + m.FlagsOff; npcs += m.NpcCount + m.NpcsOff; outside += m.OutsideCount; }
        Console.WriteLine($"  flags {flags}, npcs {npcs}, outside their map's bounds {outside}");
        Check(outside <= 2 + 15, "almost everything sits inside its map");

        Console.WriteLine("== minimap names");
        foreach (MapEntry m in cat.Maps.Where(x => x.HasGeometry && !x.Stem.StartsWith("o_")))
            if (m.MinimapPath == null) Console.WriteLine("  no minimap: " + m.Stem);
        Check(cat.Find("ice_2").MinimapPath.EndsWith("ice2.tga", StringComparison.OrdinalIgnoreCase), "ice_2 uses ice2.tga");
        Check(cat.Find("dun-6a").MinimapPath.EndsWith("dun-6.tga", StringComparison.OrdinalIgnoreCase), "dun-6a uses dun-6.tga");
        Check(cat.Find("lostisland").MinimapPath.EndsWith("lost.tga", StringComparison.OrdinalIgnoreCase), "lostisland uses Lost.tga");
        Check(cat.Find("lost_temple").MinimapPath.EndsWith("Losttemple.tga", StringComparison.OrdinalIgnoreCase), "lost_temple uses Losttemple.tga");
        int loaded = 0;
        foreach (MapEntry m in cat.Maps.Where(x => x.MinimapPath != null))
            using (var b = GameImage.Load(m.MinimapPath)) if (b.Width > 0) loaded++;
        Check(loaded == cat.Maps.Count(x => x.MinimapPath != null), $"all {loaded} minimaps decode");

        Console.WriteLine("== NPC height from terrain");
        var diffs = new List<double>();
        int noSurface = 0, onTop = 0;
        foreach (MapEntry m in cat.Maps.Where(x => x.HasGeometry && x.SpcPath != null))
        {
            StageMesh mesh = StageMesh.Load(m.SmdPath);
            foreach (NpcRecord n in SpcFile.Load(m.SpcPath).Npcs)
            {
                double? g = mesh.GroundAt(n.X, n.Z);
                if (g == null) { noSurface++; continue; }
                diffs.Add(Math.Abs(g.Value - n.Y));
                if (Math.Abs(g.Value - n.Y) < 5) onTop++;
            }
        }
        diffs.Sort();
        Console.WriteLine($"  median {diffs[diffs.Count / 2]:0.00}, p90 {diffs[diffs.Count * 9 / 10]:0.00}, within 5 units {onTop}/{diffs.Count}, no surface {noSurface}");
        Check(onTop * 100 / Math.Max(1, diffs.Count) >= 90, "ground lookup matches stored NPC heights");

        Console.WriteLine("== byte-exact round trips");
        int textFiles = 0, textBad = 0;
        string[] exts = { ".inf", ".npc", ".txt", ".zhoon", ".spm", ".ini" };
        foreach (string f in Directory.EnumerateFiles(Path.Combine(root, "GameServer"), "*.*", SearchOption.AllDirectories))
        {
            if (!exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)) continue;
            textFiles++;
            byte[] orig = File.ReadAllBytes(f);
            var d = new TextDoc();
            d.Parse(orig);
            if (!d.ToBytes().SequenceEqual(orig)) { textBad++; Console.WriteLine("  differs: " + f); continue; }
            var d2 = new TextDoc();
            d2.Parse(orig);
            d2.SetAllText(d2.GetAllText());
            if (!d2.ToBytes().SequenceEqual(orig)) { textBad++; Console.WriteLine("  edit-buffer differs: " + f); }
        }
        Check(textBad == 0, $"{textFiles} text files come back unchanged, also through the editor buffer");
        int binFiles = 0, binBad = 0;
        foreach (MapEntry m in cat.Maps)
        {
            if (m.SppPath != null)
            {
                binFiles++;
                SppFile s = SppFile.Load(m.SppPath);
                if (!s.ToBytes(s.Flags).SequenceEqual(File.ReadAllBytes(m.SppPath))) { binBad++; Console.WriteLine("  spp differs " + m.Stem); }
            }
            if (m.SpcPath != null)
            {
                binFiles++;
                SpcFile s = SpcFile.Load(m.SpcPath);
                if (!s.ToBytes(s.Npcs).SequenceEqual(File.ReadAllBytes(m.SpcPath))) { binBad++; Console.WriteLine("  spc differs " + m.Stem); }
            }
        }
        Check(binBad == 0, $"{binFiles} spawn files come back unchanged");

        Console.WriteLine("== editing flags");
        {
            MapEntry m = cat.Find("fore-2");
            byte[] before = File.ReadAllBytes(m.SppPath);
            var s = new MapSession(cat, m);
            s.Checkpoint();
            SpawnFlag f0 = s.Flags[3];
            f0.X += 100; f0.Z -= 50;
            Check(s.IsDirty, "session is dirty after a move");
            s.Undo();
            Check(!s.IsDirty, "undo returns to clean");
            s.Redo();
            s.Save();
            byte[] after = File.ReadAllBytes(m.SppPath);
            int changed = Enumerable.Range(0, before.Length).Count(i => before[i] != after[i]);
            int slotOff = 3 * 12;
            bool onlySlot = Enumerable.Range(0, before.Length).All(i => before[i] == after[i] || (i >= slotOff && i < slotOff + 12));
            Check(onlySlot && changed > 0, $"moving one flag changed only its record ({changed} bytes)");
            int oldCount = s.Flags.Count;
            s.Checkpoint();
            SpawnFlag added = s.AddFlag(-4000, -10000);
            s.Flags.RemoveAll(x => x.Slot == 0);
            s.Save();
            SppFile re = SppFile.Load(m.SppPath);
            Check(re.Flags.Count == oldCount && re.Flags.Any(x => x.X == -4000 && x.Z == -10000) && re.Flags.All(x => x.Slot != 0), "adding and removing flags saved");
            Check(after.Length == File.ReadAllBytes(m.SppPath).Length, "file size unchanged");
            Check(Directory.Exists(Path.Combine(root, SafeWrite.BackupFolderName)), "backup folder created");
        }
        {
            // the short village-1 file grows when a flag goes past its end
            MapEntry m = cat.Find("village-1");
            var s = new MapSession(cat, m);
            s.Checkpoint();
            while (s.Flags.Count < 60) if (s.AddFlag(22000 + s.Flags.Count, 0) == null) break;
            s.Save();
            Check(new FileInfo(m.SppPath).Length == 2400, "a 600-byte flag file grows to the standard 2400");
            Check(SppFile.Load(m.SppPath).Flags.Count == 60, "and holds all 60 flags");
        }
        {
            // full map refuses gracefully
            MapEntry m = cat.Find("de-4");
            var s = new MapSession(cat, m);
            int free = 200 - s.Flags.Count;
            s.Checkpoint();
            for (int i = 0; i < free; i++) s.AddFlag(40000, 10000);
            Check(s.AddFlag(40000, 10000) == null, "a full flag file refuses a new flag");
            s.Undo();
        }

        Console.WriteLine("== editing NPCs");
        {
            MapEntry m = cat.Find("village-2");
            byte[] before = File.ReadAllBytes(m.SpcPath);
            var s = new MapSession(cat, m);
            NpcRecord n = s.Npcs[2];
            int slot = n.Slot;
            s.Checkpoint();
            n.X = n.X + 256;
            n.Degrees = 90;
            s.Save();
            byte[] after = File.ReadAllBytes(m.SpcPath);
            var changedAt = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
            bool expected = changedAt.All(i => i - slot * 504 >= 476 && i - slot * 504 < 480 || i - slot * 504 >= 492 && i - slot * 504 < 496);
            Check(expected && changedAt.Count > 0, $"moving and turning one NPC changed only x and angle ({changedAt.Count} bytes)");
            NpcRecord re = SpcFile.Load(m.SpcPath).Npcs.First(x => x.Slot == slot);
            Check(Math.Abs(re.Degrees - 90) < 0.1, "angle saved as 90 degrees = " + re.AngleRaw);
            Check(re.AngleRaw == 1024, "90 degrees is a quarter of 4096");

            s.Checkpoint();
            NpcRecord add = s.AddNpc(1000, -17000, "테스트", @"char\npc\TN-003\TN-003.ini", @"GameServer\npc\ricarden-equip1.NPC", 45);
            int addSlot = add.Slot;
            s.Save();
            SpcFile sp = SpcFile.Load(m.SpcPath);
            NpcRecord ra = sp.Npcs.First(x => x.Slot == addSlot);
            Check(ra.Name == "테스트" && ra.NpcFile.EndsWith("ricarden-equip1.NPC") && ra.Active, "new NPC saved with name and file");
            Check(Math.Abs(ra.X - 1000) < 0.01 && Math.Abs(ra.Z + 17000) < 0.01 && Math.Abs(ra.Degrees - 45) < 0.1, "new NPC position and facing");
            Check(Math.Abs(ra.Y - (s.Mesh.GroundAt(1000, -17000) ?? -9999)) < 0.01, "new NPC placed on the ground, y=" + ra.Y);
            Check(sp.Npcs.Select(x => x.Serial).Count(v => v == ra.Serial) == 1, "new NPC has its own serial " + ra.Serial);
            Check(new FileInfo(m.SpcPath).Length == 50400, "spc file size unchanged");

            s.Checkpoint();
            s.Npcs.RemoveAll(x => x.Slot == addSlot);
            s.Save();
            byte[] finalBytes = File.ReadAllBytes(m.SpcPath);
            Check(finalBytes.Skip(addSlot * 504).Take(504).All(b => b == 0), "removing an NPC leaves an empty slot");
            Check(SpcFile.Load(m.SpcPath).Npcs.Count == sp.Npcs.Count - 1, "one fewer NPC after removal");
        }
        {
            NpcRecord r = new NpcRecord(0, new byte[504]);
            r.AngleRaw = 5193;
            Check(Math.Abs(r.Degrees - 1097 * 360.0 / 4096) < 0.01, "stored angles above 4095 wrap");
            bool threw = false;
            try { r.Name = new string('x', 40); } catch (ArgumentException) { threw = true; }
            Check(threw, "a too-long NPC name is refused rather than overflowing");
        }

        Console.WriteLine("== monster list");
        {
            MapEntry m = cat.Find("De-4");
            byte[] before = File.ReadAllBytes(m.SpmPath);
            SpmDoc d = SpmDoc.Load(m.SpmPath);
            List<SpmActor> a = d.Actors();
            List<SpmBoss> b = d.Bosses();
            Check(a.Count == 6 && a[0].Percent == "10", "read 6 monsters, got " + a.Count);
            Check(b.Count == 1 && b[0].GuardCount == "8" && b[0].Hours.StartsWith("12 14"), "read the boss row");
            Check(d.Get(SpmDoc.KeysMax) == "130", "read max concurrent");
            d.SetActors(a);
            d.SetBosses(b);
            Check(d.ToBytes().SequenceEqual(before), "setting the same rows changes nothing");
            a[1].Percent = "33";
            a.Add(new SpmActor { Monster = "테스트몬스터", Percent = "5" });
            a.RemoveAt(0);
            d.SetActors(a);
            d.Set(SpmDoc.KeysMax, "140");
            string text = Cp949.Enc.GetString(d.ToBytes());
            Check(text.Contains("//몬스터 출현 비율") && text.Contains("140") && text.Contains("테스트몬스터"), "edits applied, comments kept");
            Check(text.Contains("//*출연자두목"), "commented-out rows untouched");
            SpmDoc re = new SpmDoc();
            re.Parse(d.ToBytes());
            Check(re.Actors().Count == 6 && re.Actors().Any(x => x.Monster == "테스트몬스터"), "edited list reads back");
            Check(re.Bosses().Count == 1, "boss row survived");
        }

        Console.WriteLine("== keyed files");
        {
            string f = Directory.EnumerateFiles(Path.Combine(root, "GameServer", "Monster"), "*.inf").First();
            byte[] before = File.ReadAllBytes(f);
            TextDoc d = TextDoc.Load(f);
            DocEntry e = d.Entries().First(x => x.Key == "*레벨");
            d.SetValue(e.Line, "99");
            string[] la = Cp949.Enc.GetString(before).Split('\n');
            string[] lb = Cp949.Enc.GetString(d.ToBytes()).Split('\n');
            Check(la.Length == lb.Length && Enumerable.Range(0, la.Length).Count(i => la[i] != lb[i]) == 1, "changing one value changes one line");
            Check(d.First("*레벨") == "99", "value reads back");
            d.AddEntry("*넉백금지", "1");
            var dn = new TextDoc();
            dn.Parse(d.ToBytes());
            Check(dn.First("*넉백금지") == "1", "added key reads back");
            int lineLink = dn.Entries().First(x => x.Key == "*연결파일").Line;
            int lineNew = dn.Entries().First(x => x.Key == "*넉백금지").Line;
            Check(lineNew < lineLink, "added key goes above the link line");
        }

        Console.WriteLine("== schema coverage");
        foreach (var (schema, sub, ext) in new[] { (Schema.Monster, "Monster", ".inf"), (Schema.Npc, "NPC", ".npc"), (Schema.Item, "OpenItem", ".txt") })
        {
            var unknown = new Dictionary<string, int>();
            foreach (string f in Directory.EnumerateFiles(Path.Combine(root, "GameServer", sub)))
                if (Path.GetExtension(f).Equals(ext, StringComparison.OrdinalIgnoreCase))
                    foreach (DocEntry e in TextDoc.Load(f).Entries())
                        if (schema.Lookup(e.Key) == null)
                        {
                            unknown.TryGetValue(e.Key, out int c);
                            unknown[e.Key] = c + 1;
                        }
            Console.WriteLine($"  {schema.Name}: keys without a label: {string.Join(", ", unknown.OrderByDescending(k => k.Value).Take(12).Select(k => k.Key + "(" + k.Value + ")"))}");
        }

        Console.WriteLine("== a server that moved a map");
        {
            // a scratch Field folder holding one map, whose file then gets moved like a server admin would
            string tmpField = Path.Combine(root, "scratch_field");
            Directory.CreateDirectory(Path.Combine(tmpField, "forest"));
            string smd = Path.Combine(tmpField, "forest", "fore-3.smd");
            string original = Directory.EnumerateFiles(field, "fore-3.smd", SearchOption.AllDirectories).First();
            File.Copy(original, smd, true);
            MapEntry refMap = Catalog.Scan(root, field, false, null).Find("fore-3");
            int flagsThere = refMap.FlagCount + refMap.FlagsOff;
            int npcsThere = refMap.NpcCount + refMap.NpcsOff;

            Catalog c1 = Catalog.Scan(root, tmpField, true, null);
            MapEntry a = c1.Find("fore-3");
            Check(c1.Maps.Any(m => !m.HasGeometry && m.HasSpawnData), "spawn files with no map file are still listed");
            Check(Catalog.Scan(root, tmpField, false, null).MapsDrawn == 0, "and don't stop the next start");
            Check(a.OutsideCount == 0, "before the move everything is on the map");
            double oldMinX = a.MinX, oldMaxZ = a.MaxZ;

            ShiftStage(smd, 65536, -4096);
            File.SetLastWriteTimeUtc(smd, DateTime.UtcNow.AddMinutes(5));
            Catalog c2 = Catalog.Scan(root, tmpField, false, null);
            MapEntry b = c2.Find("fore-3");
            Check(c2.GeneratedThisRun, "changing the map file regenerates the settings without being asked");
            Check(Math.Abs(b.MinX - oldMinX - 65536) < 0.01 && Math.Abs(b.MaxZ - oldMaxZ + 4096) < 0.01, "new position measured from the map itself");
            Check(b.OutsideCount == flagsThere + npcsThere, $"all {b.OutsideCount} flags and NPCs reported off the map");

            var sess = new MapSession(c2, b);
            sess.Checkpoint();
            sess.ShiftAll(65536, -4096, true, true);
            sess.Save();
            Check(b.OutsideCount == 0, "shifting them by the same amount puts them all back on the map");
            Catalog c3 = Catalog.Scan(root, tmpField, false, null);
            Check(!c3.GeneratedThisRun && Math.Abs(c3.Find("fore-3").MinX - b.MinX) < 0.01, "settings are reused on the next run");

            // only the map that changed gets prepared again
            string other = Path.Combine(tmpField, "forest", "fore-2.smd");
            File.Copy(Directory.EnumerateFiles(field, "fore-2.smd", SearchOption.AllDirectories).First(), other, true);
            string oldCopy = Directory.EnumerateFiles(field, "o_fore-3.smd", SearchOption.AllDirectories).FirstOrDefault();
            string copyTo = Path.Combine(tmpField, "forest", "o_fore-3.smd");
            if (oldCopy != null) File.Copy(oldCopy, copyTo, true);
            Catalog p1 = Catalog.Scan(root, tmpField, false, null);
            Check(p1.MapsMeasured == (oldCopy != null ? 2 : 1), "two maps added: only those measured, got " + p1.MapsMeasured);
            string fore2Png = TerrainCache.PathFor(p1, p1.Find("fore-2"));
            DateTime fore2Time = File.GetLastWriteTimeUtc(fore2Png);
            if (oldCopy != null)
            {
                TwinInfo tw = p1.Find("fore-3").Twins.FirstOrDefault(t => t.Stem.Equals("o_fore-3", StringComparison.OrdinalIgnoreCase));
                Check(tw != null, "a newly added copy is matched to its original");
                int dxBefore = tw?.DX ?? 0;
                ShiftStage(copyTo, 4096, 0);
                File.SetLastWriteTimeUtc(copyTo, DateTime.UtcNow.AddMinutes(10));
                Catalog p2 = Catalog.Scan(root, tmpField, false, null);
                Check(p2.MapsMeasured == 1 && p2.MapsDrawn == 1, "changing one .smd prepares just that map");
                Check(File.GetLastWriteTimeUtc(TerrainCache.PathFor(p2, p2.Find("fore-2"))) == fore2Time, "other pictures aren't touched");
                TwinInfo tw2 = p2.Find("fore-3").Twins.FirstOrDefault(t => t.Stem.Equals("o_fore-3", StringComparison.OrdinalIgnoreCase));
                Check(tw2 != null && tw2.DX == dxBefore - 4096, $"its offset is updated on the map that didn't change: {dxBefore} -> {tw2?.DX}");
                File.Delete(copyTo);
                Catalog p3 = Catalog.Scan(root, tmpField, false, null);
                Check(p3.Find("o_fore-3") == null || !p3.Find("o_fore-3").HasGeometry, "a removed map is forgotten");
                Check(!File.Exists(Path.Combine(TerrainCache.Root(p3), "forest", "o_fore-3.png")), "and its picture is deleted");
                Check(!p3.Find("fore-3").Twins.Any(t => t.Stem.Equals("o_fore-3", StringComparison.OrdinalIgnoreCase)), "and its match is cleared");
            }
            // a non-map .smd isn't opened again, but a new file is noticed
            string index = Path.Combine(root, TerrainCache.FolderName, "NotMaps.txt");
            string model = Path.Combine(tmpField, "forest", "prop.smd");
            File.WriteAllBytes(model, new byte[300]);
            Catalog p4 = Catalog.Scan(root, tmpField, false, null);
            Check(File.ReadAllText(index).Contains("prop.smd") && p4.Find("prop") == null, "a non-map .smd is remembered and not listed");
            File.Copy(other, model, true);
            File.SetLastWriteTimeUtc(model, DateTime.UtcNow.AddMinutes(11));
            Catalog p5 = Catalog.Scan(root, tmpField, false, null);
            Check(p5.Find("prop") != null && p5.Find("prop").HasGeometry, "if that file becomes a map, it's picked up");
            File.Delete(model);
            File.Delete(other);
            Catalog.Scan(root, tmpField, false, null);

            // positions set by hand survive a rebuild
            MapEntry h = c3.Find("fore-3");
            h.ManualDX = 12; h.ManualDZ = -34;
            c3.SaveSettings();
            Catalog c4 = Catalog.Scan(root, tmpField, true, null);
            Check(c4.Find("fore-3").ManualDX == 12 && c4.Find("fore-3").ManualDZ == -34, "a minimap position set by hand survives Rebuild");
            File.Delete(Path.Combine(root, "PriTaTor.ini"));

            // a folder pictures can't be saved in still works
            string roRoot = Path.Combine(root, "scratch_readonly");
            Directory.CreateDirectory(Path.Combine(roRoot, "GameServer", "Field"));
            File.WriteAllText(Path.Combine(roRoot, TerrainCache.FolderName), "a file in the way");
            Catalog ro = Catalog.Scan(roRoot, tmpField, false, null);
            MapEntry roMap = ro.Find("fore-3");
            Check(!ro.ImagesWritable && roMap != null && roMap.MaxX > roMap.MinX, "if pictures can't be saved, maps are still measured");
            using (System.Drawing.Bitmap bmp = TerrainCache.Load(ro, roMap, StageMesh.Load(roMap.SmdPath), out bool savedRo))
                Check(bmp.Width > 1000 && !savedRo, "and drawn when they open");
        }

        Console.WriteLine("== names from .zhoon files");
        {
            Func<string, string> z = s => LocalName.FromZhoon(Encoding.ASCII.GetBytes(s));
            Check(z("//x\r\n\r\n*E_NAME\t\t\"Arcane Ring\"\r\n") == "Arcane Ring", "*E_NAME");
            Check(z("*J_NAME \"Hopy\"") == "Hopy" && z("*B_NAME\t\"Hopy\"") == "Hopy" && z("*E_Name \"Hopy\"") == "Hopy", "any language letter, any case");
            Check(z("*E_NAME  \"  Spaced Out \"") == "Spaced Out", "spaces inside the quotes are trimmed");
            Check(z("*E_NAME Plain Name") == "Plain Name", "no quotes");
            Check(z("//*E_NAME \"Old\"\r\n*E_NAME \"New\"") == "New", "commented lines are skipped");
            Check(z("*E_CHAT \"Hello\"") == "", "dialogue isn't a name");
            Func<int, string, string, byte[]> mk = (page, key, text) =>
                Encoding.ASCII.GetBytes("*" + key + " \"").Concat(Encoding.GetEncoding(page).GetBytes(text)).Concat(new[] { (byte)'"' }).ToArray();
            Check(LocalName.FromZhoon(mk(932, "J_NAME", "ホピー")) == "ホピー", "Japanese decodes");
            Check(LocalName.FromZhoon(mk(1252, "B_NAME", "Poção")) == "Poção", "Brazilian decodes");
            Check(LocalName.FromZhoon(mk(949, "K_NAME", "호피")) == "호피", "Korean decodes");
            Check(LocalName.FromZhoon(mk(950, "T_NAME", "導遊")) == "導遊", "Traditional Chinese decodes");
            string dragoman = Path.Combine(root, "GameServer", "NPC", "name", "ricarden-dragoman.zhoon");
            if (File.Exists(dragoman)) Console.WriteLine("  ricarden-dragoman.zhoon reads as: " + LocalName.FromZhoon(File.ReadAllBytes(dragoman)));

            var sd = ServerData.Load(root);
            Func<List<DataItem>, string> stats = l => $"{l.Count(x => !string.IsNullOrEmpty(x.LocalName))} of {l.Count} named";
            Console.WriteLine("  NPCs: " + stats(sd.Npcs) + ", monsters: " + stats(sd.Monsters));
            Check(sd.Npcs.All(x => !string.IsNullOrEmpty(x.LocalName)), "every NPC has a name");
            int koreanNpc = sd.Npcs.Count(x => x.LocalName.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3));
            int koreanMon = sd.Monsters.Count(x => (x.LocalName ?? "").Any(ch => ch >= 0xAC00 && ch <= 0xD7A3));
            Console.WriteLine($"  still Korean: {koreanNpc} NPCs, {koreanMon} monsters");
            Check(koreanNpc == 0, "no NPC name is Korean");
            Console.WriteLine("  non-English NPC names: " + string.Join(", ", sd.Npcs.Where(x => x.LocalName.Any(ch => ch > 127)).Select(x => x.FileName + "=" + x.LocalName)));
        }

        Console.WriteLine("== item catalog");
        {
            ItemCatalog ic = ItemCatalog.Load(Path.Combine(root, "GameServer", "OpenItem"));
            Console.WriteLine($"  {ic.Items.Count} items; other: " + string.Join(", ", ic.Items.Where(i => i.Group == "Other").GroupBy(i => i.Category).Select(g => g.Key + "(" + g.Count() + ")")));
            Check(ic.Items.Count == ic.ByCode.Count && ic.Items.Count > 1300, "one row per code");
            Check(ic.Items.All(i => i.Code == i.Code.ToLowerInvariant() && !i.Code.Contains('"')), "codes are clean");
            Check(ic.Items.Count(i => i.Group == "Other") == 0, "every item prefix on this server has a category");
            Check(ic.ByCode["wa108"].Category == "Axes" && ic.ByCode["ws108"].Category == "Bows" && ic.ByCode["ws210"].Category == "Swords" && ic.ByCode["oa101"].Category == "Amulets" && ic.ByCode["oa201"].Category == "Bracelets", "categories");
            Check(ic.ByCode["pl101"].Group == "Potions" && !string.IsNullOrEmpty(ic.ByCode["pl101"].Name), "potion: " + ic.ByCode["pl101"].Name);
            Check(ic.Items.Count(i => i.Name.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)) < 40, "almost all item names are localised, Korean left: " + ic.Items.Count(i => i.Name.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3)));
        }

        Console.WriteLine("== shop lines");
        {
            Func<string, TextDoc> mk = t => { var d = new TextDoc(); d.Parse(Cp949.Encode(t)); return d; };
            string K = NpcShop.Keys[0];
            string src = "//c\r\n" + K + "\twa104 wa105\twc104 wc105\t\tWP106 //hi\r\n*연결파일 \"x\"\r\n";
            TextDoc d1 = mk(src);
            List<string> r = NpcShop.Read(d1, K);
            Check(r.Count == 5 && r[4] == "WP106", "reads codes, ignores the comment");
            NpcShop.Write(d1, K, r);
            Check(Cp949.Enc.GetString(d1.ToBytes()) == src, "writing the same list changes nothing, even in another case form");
            NpcShop.Write(d1, K, new[] { "wa104", "wa105", "wc105", "WP106" });
            Check(d1.Lines[1].Text.Contains("wa104 wa105 wc105\t\tWP106"), "removing takes only that code and the gap before it: " + d1.Lines[1].Text.Replace("\t", "<T>"));
            Check(d1.Lines[1].Text.Contains("//hi"), "comment kept");
            NpcShop.Write(d1, K, new[] { "wa104", "wa105", "wc105", "WP106", "ws101" });
            Check(NpcShop.Read(d1, K).Last() == "ws101", "adding goes on the end");
            NpcShop.Write(d1, K, new[] { "wa105", "wa104", "wc105", "WP106", "ws101" });
            Check(NpcShop.Read(d1, K).SequenceEqual(new[] { "wa105", "wa104", "wc105", "WP106", "ws101" }), "reordering");
            NpcShop.Write(d1, K, new string[0]);
            Check(d1.First(K).StartsWith(NpcShop.Nothing) && NpcShop.Read(d1, K).Count == 0, "removing everything writes 없음");
            NpcShop.Write(d1, K, new[] { "wa101" });
            Check(!d1.First(K).Contains(NpcShop.Nothing) && NpcShop.Read(d1, K)[0] == "wa101", "adding replaces 없음");
            TextDoc d2 = mk(K + " wa101 wa102\r\n" + K + " wa103\r\n");
            NpcShop.Write(d2, K, new[] { "wa101", "wa103" });
            Check(d2.Lines.Count == 2 && NpcShop.Read(d2, K).SequenceEqual(new[] { "wa101", "wa103" }), "shops split over two lines stay split");
            NpcShop.Write(d2, K, new[] { "wa101" });
            Check(d2.Lines.Count == 1, "an emptied second line is removed");
            TextDoc d3 = mk("*이름 \"A\"\r\n*연결파일 \"x\"\r\n");
            NpcShop.Write(d3, K, new[] { "wa101" });
            Check(d3.Lines[1].Text.StartsWith(K) && d3.Lines[2].Text.StartsWith("*연결파일"), "an NPC with no shop gets one above the link line");
            int shopFiles = 0, changed = 0;
            foreach (string f in Directory.EnumerateFiles(Path.Combine(root, "GameServer", "NPC"), "*.npc"))
            {
                byte[] orig = File.ReadAllBytes(f);
                TextDoc nd = new TextDoc();
                nd.Parse(orig);
                shopFiles++;
                foreach (string key in NpcShop.Keys) NpcShop.Write(nd, key, NpcShop.Read(nd, key));
                if (!nd.ToBytes().SequenceEqual(orig)) changed++;
            }
            Check(changed == 0, $"reading and rewriting {shopFiles} NPC shops changes no bytes");
        }

        Console.WriteLine("== safe write");
        {
            string tmp = Path.Combine(root, "safewrite_test.txt");
            File.WriteAllText(tmp, "one");
            Check(!SafeWrite.Save(tmp, File.ReadAllBytes(tmp)), "identical data is not rewritten");
            Check(SafeWrite.Save(tmp, Encoding.ASCII.GetBytes("two")), "changed data is written");
            Check(File.ReadAllText(tmp) == "two" && !File.Exists(tmp + ".pritator_tmp"), "no temp file left");
            File.Delete(tmp);
        }

        Console.WriteLine();
        Console.WriteLine($"passed {passes}, failed {failures}");
        return failures == 0 ? 0 : 1;
    }
}
