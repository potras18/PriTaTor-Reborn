// Name lists from the server's data files: what the map editor offers as monsters and NPCs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PriTaTor.Core
{
    public sealed class DataItem
    {
        public string Path, FileName;
        public string Name, English, Extra;
        public override string ToString() =>
            !string.IsNullOrEmpty(English) && !string.IsNullOrEmpty(Name) && English != Name ? Name + "  (" + English + ")" : (Name ?? English ?? FileName);
    }

    public sealed class ServerData
    {
        public string ServerRoot;
        public string MonsterDir, NpcDir, ItemDir;
        public List<DataItem> Monsters = new List<DataItem>();
        public List<DataItem> Npcs = new List<DataItem>();

        public static readonly string[] MonsterExt = { ".inf" };
        public static readonly string[] NpcExt = { ".npc" };
        public static readonly string[] ItemExt = { ".txt" };
        public static readonly string[] ZhoonExt = { ".zhoon" };

        public static ServerData Load(string root)
        {
            var d = new ServerData
            {
                ServerRoot = root,
                MonsterDir = System.IO.Path.Combine(root, "GameServer", "Monster"),
                NpcDir = System.IO.Path.Combine(root, "GameServer", "NPC"),
                ItemDir = System.IO.Path.Combine(root, "GameServer", "OpenItem")
            };
            d.Monsters = ReadFolder(d.MonsterDir, MonsterExt, "*레벨");
            d.Npcs = ReadFolder(d.NpcDir, NpcExt, "*모양파일");
            return d;
        }

        public static List<DataItem> ReadFolder(string dir, string[] exts, string extraKey)
        {
            var r = new List<DataItem>();
            if (!Directory.Exists(dir)) return r;
            foreach (string f in Directory.EnumerateFiles(dir))
            {
                if (!exts.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)) continue;
                try
                {
                    TextDoc doc = TextDoc.Load(f);
                    r.Add(new DataItem
                    {
                        Path = f,
                        FileName = System.IO.Path.GetFileName(f),
                        Name = Tokens.Unquote(doc.First("*이름")),
                        English = Tokens.Unquote(doc.First("*Name", "*NAME", "*E_NAME")),
                        Extra = extraKey != null ? Tokens.Unquote(doc.First(extraKey)) : ""
                    });
                }
                catch (IOException) { }
            }
            r.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
            return r;
        }

        public string RelativeNpcPath(string path)
        {
            // the stored form is GameServer\npc\<file>
            return "GameServer\\npc\\" + System.IO.Path.GetFileName(path);
        }
    }
}
