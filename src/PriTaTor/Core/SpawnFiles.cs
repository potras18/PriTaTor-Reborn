// Binary spawn files in GameServer\Field.
//
//   <map>.ase.spp   12-byte records: int state, int x, int z   (whole world units; state 0 = off)
//   <map>.ase.spc   504-byte records, 100 slots: the game's player-info packet saved to disk
//
//   SPC record                     (empty slots are all zero)
//     +0    int   record size (504)
//     +4    int   packet code; 1212612720 = NPC is active, 0 = switched off
//     +8    char  name[32]         CP949
//     +40   char  model[64]        e.g. CHAR\NPC\TN-005\TN-005.INI
//     +104  char  npc file[64]     e.g. GameServer\npc\ricarden-store.NPC
//     +472  int   object serial
//     +476  int   x, y, z          fixed point, world unit * 256
//     +488  int   angle x, y, z    4096 = full turn; y is the facing, as a compass bearing:
//                                  forward = (sin a, cos a) in (x, z)
//     everything else is a character-stats dump which is kept byte for byte

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PriTaTor.Core
{
    public sealed class SpawnFlag
    {
        public int Slot;
        public int State;
        public int X, Z;
        public SpawnFlag Clone() => (SpawnFlag)MemberwiseClone();
    }

    public sealed class SppFile
    {
        public const int RecordSize = 12;
        public const int StandardSlots = 200;

        public string Path;
        byte[] data = new byte[0];
        public List<SpawnFlag> Flags = new List<SpawnFlag>();

        public static SppFile Load(string path)
        {
            var f = new SppFile { Path = path };
            f.data = File.ReadAllBytes(path);
            int n = f.data.Length / RecordSize;
            for (int i = 0; i < n; i++)
            {
                int o = i * RecordSize;
                int st = BitConverter.ToInt32(f.data, o), x = BitConverter.ToInt32(f.data, o + 4), z = BitConverter.ToInt32(f.data, o + 8);
                if (st != 0 || x != 0 || z != 0)
                    f.Flags.Add(new SpawnFlag { Slot = i, State = st, X = x, Z = z });
            }
            return f;
        }

        public static SppFile CreateEmpty(string path) => new SppFile { Path = path, data = new byte[StandardSlots * RecordSize] };

        /// <summary>Slots available. A short file grows to the standard size when it needs to.</summary>
        public int Capacity => Math.Max(StandardSlots, data.Length / RecordSize);

        public int NextFreeSlot(IEnumerable<SpawnFlag> flags)
        {
            var used = new HashSet<int>(flags.Select(f => f.Slot));
            for (int i = 0; i < Capacity; i++) if (!used.Contains(i)) return i;
            return -1;
        }

        public byte[] ToBytes(IEnumerable<SpawnFlag> flags)
        {
            List<SpawnFlag> list = flags.ToList();
            int maxSlot = list.Count == 0 ? -1 : list.Max(f => f.Slot);
            int len = Math.Max(data.Length, (maxSlot + 1) * RecordSize);
            if (len > data.Length && len < StandardSlots * RecordSize) len = StandardSlots * RecordSize;
            var o = new byte[len];
            Buffer.BlockCopy(data, 0, o, 0, data.Length);
            for (int i = 0; i + RecordSize <= len; i += RecordSize) Array.Clear(o, i, RecordSize);
            foreach (SpawnFlag f in list)
            {
                int at = f.Slot * RecordSize;
                Buffer.BlockCopy(BitConverter.GetBytes(f.State), 0, o, at, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(f.X), 0, o, at + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(f.Z), 0, o, at + 8, 4);
            }
            return o;
        }

        public void Saved(byte[] bytes) => data = bytes;
    }

    public sealed class NpcRecord
    {
        public const int ActiveCode = 1212612720;
        public const int AngleFull = 4096;

        public int Slot;
        public byte[] Raw;   // always 504 bytes

        public NpcRecord(int slot, byte[] raw) { Slot = slot; Raw = raw; }
        public NpcRecord Clone() => new NpcRecord(Slot, (byte[])Raw.Clone());

        int GetI(int o) => BitConverter.ToInt32(Raw, o);
        void SetI(int o, int v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, Raw, o, 4);

        string GetS(int o, int max)
        {
            int n = 0;
            while (n < max && Raw[o + n] != 0) n++;
            return Cp949.Decode(Raw, o, n);
        }

        void SetS(int o, int max, string s)
        {
            if (GetS(o, max) == (s ?? "")) return;
            byte[] b = Cp949.Encode(s);
            if (b.Length >= max) throw new ArgumentException(string.Format("Text is too long: {0} bytes, limit {1}.", b.Length, max - 1));
            Array.Clear(Raw, o, max);
            Buffer.BlockCopy(b, 0, Raw, o, b.Length);
        }

        public bool Active { get => GetI(4) == ActiveCode; set => SetI(4, value ? ActiveCode : 0); }
        public string Name { get => GetS(8, 32); set => SetS(8, 32, value); }
        public string Model { get => GetS(40, 64); set => SetS(40, 64, value); }
        public string NpcFile { get => GetS(104, 64); set => SetS(104, 64, value); }
        public int Serial { get => GetI(472); set => SetI(472, value); }
        public int RawX { get => GetI(476); set => SetI(476, value); }
        public int RawY { get => GetI(480); set => SetI(480, value); }
        public int RawZ { get => GetI(484); set => SetI(484, value); }
        public double X { get => RawX / 256.0; set => RawX = (int)Math.Round(value * 256); }
        public double Y { get => RawY / 256.0; set => RawY = (int)Math.Round(value * 256); }
        public double Z { get => RawZ / 256.0; set => RawZ = (int)Math.Round(value * 256); }

        /// <summary>Stored facing value. Saved files hold values above 4095 too; the game wraps them.</summary>
        public int AngleRaw { get => GetI(492); set => SetI(492, value); }

        /// <summary>Compass bearing in degrees, 0 = +Z (up on the map), clockwise.</summary>
        public double Degrees
        {
            get => Wrap(AngleRaw) * 360.0 / AngleFull;
            set => AngleRaw = Wrap((int)Math.Round(value * AngleFull / 360.0));
        }

        public static int Wrap(int a) => ((a % AngleFull) + AngleFull) % AngleFull;
    }

    public sealed class SpcFile
    {
        public const int RecordSize = 504;
        public const int StandardSlots = 100;

        public string Path;
        byte[] data = new byte[StandardSlots * RecordSize];
        public List<NpcRecord> Npcs = new List<NpcRecord>();

        public static SpcFile Load(string path)
        {
            var f = new SpcFile { Path = path };
            f.data = File.ReadAllBytes(path);
            int n = f.data.Length / RecordSize;
            for (int i = 0; i < n; i++)
            {
                int o = i * RecordSize;
                if (BitConverter.ToInt32(f.data, o) != RecordSize) continue;
                var raw = new byte[RecordSize];
                Buffer.BlockCopy(f.data, o, raw, 0, RecordSize);
                f.Npcs.Add(new NpcRecord(i, raw));
            }
            return f;
        }

        public static SpcFile CreateEmpty(string path) => new SpcFile { Path = path };

        public int Capacity => Math.Max(StandardSlots, data.Length / RecordSize);

        public int NextFreeSlot(IEnumerable<NpcRecord> npcs)
        {
            var used = new HashSet<int>(npcs.Select(f => f.Slot));
            for (int i = 0; i < Capacity; i++) if (!used.Contains(i)) return i;
            return -1;
        }

        public byte[] ToBytes(IEnumerable<NpcRecord> npcs)
        {
            int len = Math.Max(data.Length, StandardSlots * RecordSize);
            var o = new byte[len];
            Buffer.BlockCopy(data, 0, o, 0, data.Length);
            for (int i = 0; i + RecordSize <= len; i += RecordSize)
            {
                // only slots holding a record are ours to clear: unknown slot contents stay
                if (BitConverter.ToInt32(o, i) == RecordSize) Array.Clear(o, i, RecordSize);
            }
            foreach (NpcRecord n in npcs)
                Buffer.BlockCopy(n.Raw, 0, o, n.Slot * RecordSize, RecordSize);
            return o;
        }

        public void Saved(byte[] bytes) => data = bytes;
    }
}
