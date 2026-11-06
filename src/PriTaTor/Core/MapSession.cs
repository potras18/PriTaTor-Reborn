// The editable state of one open map: flags, NPCs and the monster list, with undo and saving.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PriTaTor.Core
{
    public sealed class MapState
    {
        public List<SpawnFlag> Flags;
        public List<NpcRecord> Npcs;
    }

    public sealed class MapSession
    {
        public readonly Catalog Cat;
        public readonly MapEntry Entry;
        public SppFile Spp;
        public SpcFile Spc;
        public SpmDoc Spm;
        public StageMesh Mesh;
        public List<SpawnFlag> Flags = new List<SpawnFlag>();
        public List<NpcRecord> Npcs = new List<NpcRecord>();

        readonly Stack<MapState> undo = new Stack<MapState>();
        readonly Stack<MapState> redo = new Stack<MapState>();
        byte[] savedSpp, savedSpc;
        public bool SpawnDirty;

        public MapSession(Catalog cat, MapEntry entry)
        {
            Cat = cat;
            Entry = entry;
            Spp = entry.SppPath != null ? SppFile.Load(entry.SppPath) : SppFile.CreateEmpty(cat.NewSpawnPath(entry, "spp"));
            Spc = entry.SpcPath != null ? SpcFile.Load(entry.SpcPath) : SpcFile.CreateEmpty(cat.NewSpawnPath(entry, "spc"));
            Spm = entry.SpmPath != null ? SpmDoc.Load(entry.SpmPath) : null;
            Flags = Spp.Flags.Select(f => f.Clone()).ToList();
            Npcs = Spc.Npcs.Select(n => n.Clone()).ToList();
            if (entry.HasGeometry && entry.SmdPath != null) Mesh = StageMesh.Load(entry.SmdPath);
            savedSpp = Spp.ToBytes(Flags);
            savedSpc = Spc.ToBytes(Npcs);
        }

        public bool IsDirty => SpawnDirty || (Spm != null && Spm.Dirty);

        // ----- undo -----

        MapState Snapshot() => new MapState { Flags = Flags.Select(f => f.Clone()).ToList(), Npcs = Npcs.Select(n => n.Clone()).ToList() };

        /// <summary>Call before a change.</summary>
        public void Checkpoint()
        {
            undo.Push(Snapshot());
            redo.Clear();
            if (undo.Count > 200) TrimUndo();
            SpawnDirty = true;
        }

        void TrimUndo()
        {
            MapState[] items = undo.ToArray();
            undo.Clear();
            for (int i = 150; i >= 0; i--) undo.Push(items[i]);
        }

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public void Undo()
        {
            if (undo.Count == 0) return;
            redo.Push(Snapshot());
            Restore(undo.Pop());
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.Push(Snapshot());
            Restore(redo.Pop());
        }

        void Restore(MapState s)
        {
            Flags = s.Flags;
            Npcs = s.Npcs;
            UpdateDirty();
        }

        public void UpdateDirty() =>
            SpawnDirty = !Spp.ToBytes(Flags).SequenceEqual(savedSpp) || !Spc.ToBytes(Npcs).SequenceEqual(savedSpc);

        // ----- editing -----

        public SpawnFlag AddFlag(double x, double z)
        {
            int slot = Spp.NextFreeSlot(Flags);
            if (slot < 0) return null;
            var f = new SpawnFlag { Slot = slot, State = 1, X = (int)Math.Round(x), Z = (int)Math.Round(z) };
            Flags.Add(f);
            Flags.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return f;
        }

        /// <summary>A new NPC starts as a copy of a valid record: most of a record is a stats dump which the game fills in.</summary>
        public NpcRecord AddNpc(double x, double z, string name, string model, string npcFile, double degrees = 0)
        {
            int slot = Spc.NextFreeSlot(Npcs);
            if (slot < 0) return null;
            byte[] template = FindTemplate(npcFile);
            if (template == null) throw new InvalidOperationException("No existing NPC record was found on this server to base a new one on.");
            var n = new NpcRecord(slot, (byte[])template.Clone());
            n.Active = true;
            n.Name = name ?? "";
            if (!string.IsNullOrEmpty(model)) n.Model = model;
            n.NpcFile = npcFile ?? "";
            n.Serial = NextSerial();
            n.X = x;
            n.Z = z;
            n.Y = GroundOr(x, z, Npcs.Count > 0 ? Npcs[0].Y : 0);
            n.Degrees = degrees;
            Npcs.Add(n);
            Npcs.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return n;
        }

        byte[] FindTemplate(string npcFile)
        {
            byte[] sameFile = null, any = null;
            foreach (NpcRecord n in Npcs)
            {
                if (string.Equals(n.NpcFile, npcFile, StringComparison.OrdinalIgnoreCase)) return n.Raw;
                if (any == null && n.Active) any = n.Raw;
            }
            if (Directory.Exists(Cat.GameFieldRoot))
                foreach (string f in Directory.EnumerateFiles(Cat.GameFieldRoot, "*.spc"))
                {
                    SpcFile s;
                    try { s = SpcFile.Load(f); } catch (IOException) { continue; }
                    foreach (NpcRecord n in s.Npcs)
                    {
                        if (string.Equals(n.NpcFile, npcFile, StringComparison.OrdinalIgnoreCase)) sameFile = sameFile ?? n.Raw;
                        if (any == null && n.Active) any = n.Raw;
                    }
                    if (sameFile != null) break;
                }
            return sameFile ?? any;
        }

        int NextSerial()
        {
            int max = 8000;
            foreach (NpcRecord n in Npcs) if (n.Serial > max && n.Serial < 1000000) max = n.Serial;
            if (Directory.Exists(Cat.GameFieldRoot))
                foreach (string f in Directory.EnumerateFiles(Cat.GameFieldRoot, "*.spc"))
                {
                    try
                    {
                        foreach (NpcRecord n in SpcFile.Load(f).Npcs)
                            if (n.Serial > max && n.Serial < 1000000) max = n.Serial;
                    }
                    catch (IOException) { }
                }
            return max + 1;
        }

        public double GroundOr(double x, double z, double fallback) => Mesh?.GroundAt(x, z) ?? fallback;

        /// <summary>Move all flags and/or NPCs by a fixed offset.</summary>
        public void ShiftAll(int dx, int dz, bool flags, bool npcs)
        {
            if (flags) foreach (SpawnFlag f in Flags) { f.X += dx; f.Z += dz; }
            if (npcs) foreach (NpcRecord n in Npcs) { n.RawX += dx * 256; n.RawZ += dz * 256; }
        }

        /// <summary>Bring another map's flags and NPCs here at an offset, replacing or adding to what's here.</summary>
        public int ImportFrom(MapEntry src, int dx, int dz, bool flags, bool npcs, bool monsters, bool replace)
        {
            int added = 0;
            if (flags && src.SppPath != null)
            {
                if (replace) Flags.Clear();
                foreach (SpawnFlag f in SppFile.Load(src.SppPath).Flags)
                {
                    int slot = Spp.NextFreeSlot(Flags);
                    if (slot < 0) break;
                    Flags.Add(new SpawnFlag { Slot = slot, State = f.State, X = f.X + dx, Z = f.Z + dz });
                    added++;
                }
                Flags.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            }
            if (npcs && src.SpcPath != null)
            {
                if (replace) Npcs.Clear();
                foreach (NpcRecord n in SpcFile.Load(src.SpcPath).Npcs)
                {
                    int slot = Spc.NextFreeSlot(Npcs);
                    if (slot < 0) break;
                    var c = new NpcRecord(slot, (byte[])n.Raw.Clone());
                    c.RawX += dx * 256;
                    c.RawZ += dz * 256;
                    // an older copy of a map can differ: keep the NPC on the surface nearest its old height
                    List<double> surfaces = Mesh?.SurfacesAt(c.X, c.Z);
                    if (surfaces != null && surfaces.Count > 0)
                        c.Y = surfaces.OrderBy(h => Math.Abs(h - c.Y)).First();
                    Npcs.Add(c);
                    added++;
                }
                Npcs.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            }
            if (monsters && src.SpmPath != null)
            {
                string path = Entry.SpmPath ?? Cat.NewSpawnPath(Entry, "spm");
                Spm = new SpmDoc { Path = path };
                Spm.Parse(File.ReadAllBytes(src.SpmPath));
                Spm.Dirty = true;
            }
            return added;
        }

        // ----- saving -----

        /// <summary>Write what changed. Returns the files written.</summary>
        public List<string> Save()
        {
            var written = new List<string>();
            byte[] spp = Spp.ToBytes(Flags);
            if (!spp.SequenceEqual(savedSpp) || (Entry.SppPath == null && Flags.Count > 0))
            {
                if (SafeWrite.Save(Spp.Path, spp)) written.Add(Spp.Path);
                Spp.Saved(spp);
                savedSpp = spp;
                Entry.SppPath = Spp.Path;
            }
            byte[] spc = Spc.ToBytes(Npcs);
            if (!spc.SequenceEqual(savedSpc) || (Entry.SpcPath == null && Npcs.Count > 0))
            {
                if (SafeWrite.Save(Spc.Path, spc)) written.Add(Spc.Path);
                Spc.Saved(spc);
                savedSpc = spc;
                Entry.SpcPath = Spc.Path;
            }
            if (Spm != null && Spm.Dirty)
            {
                if (SafeWrite.Save(Spm.Path, Spm.ToBytes())) written.Add(Spm.Path);
                Spm.MarkSaved();
                Entry.SpmPath = Spm.Path;
            }
            SpawnDirty = false;
            Cat.RefreshCounts(Entry);
            return written;
        }

        /// <summary>Other programs may have changed these files since they were opened.</summary>
        public List<string> ChangedOnDisk()
        {
            var r = new List<string>();
            if (Spm != null && Spm.ChangedOnDisk()) r.Add(Spm.Path);
            if (Entry.SppPath != null && File.Exists(Entry.SppPath) && !File.ReadAllBytes(Entry.SppPath).SequenceEqual(savedSpp) && savedSpp.Length > 0) r.Add(Entry.SppPath);
            if (Entry.SpcPath != null && File.Exists(Entry.SpcPath) && !File.ReadAllBytes(Entry.SpcPath).SequenceEqual(savedSpc)) r.Add(Entry.SpcPath);
            return r;
        }
    }
}
