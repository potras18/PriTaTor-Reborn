// Every item on the server with the name players see, and what an NPC's shop sells.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PriTaTor.Core
{
    public sealed class ShopItem
    {
        public string Code;          // lower case
        public string Name, Korean, FileName;
        public int Level, Price;
        public string Group, Category;
        public override string ToString() => Name + " (" + Code + ")";
    }

    public sealed class ItemCatalog
    {
        public List<ShopItem> Items = new List<ShopItem>();
        public Dictionary<string, ShopItem> ByCode = new Dictionary<string, ShopItem>(StringComparer.OrdinalIgnoreCase);

        // Longest prefix wins. Worked out from the item names on a real server; unknown prefixes land in Other.
        static readonly string[][] Table =
        {
            new[] { "Weapons", "Axes", "wa" }, new[] { "Weapons", "Bows", "ws1", "ws3" }, new[] { "Weapons", "Claws", "wc" },
            new[] { "Weapons", "Daggers", "wd" }, new[] { "Weapons", "Hammers", "wh" }, new[] { "Weapons", "Javelins", "wt" },
            new[] { "Weapons", "Phantoms", "wn" }, new[] { "Weapons", "Scythes and spears", "wp" }, new[] { "Weapons", "Staffs and wands", "wm" },
            new[] { "Weapons", "Swords", "ws2", "ws4" }, new[] { "Weapons", "Vambraces", "wv" },
            new[] { "Armour", "Armour", "da1", "da3" }, new[] { "Armour", "Robes", "da2", "da4" }, new[] { "Armour", "Boots", "db" },
            new[] { "Armour", "Gauntlets", "dg" }, new[] { "Armour", "Shields", "ds" }, new[] { "Armour", "Bracelets", "oa2" }, new[] { "Armour", "Orbs", "om" },
            new[] { "Accessories", "Amulets", "oa1" }, new[] { "Accessories", "Rings", "or" }, new[] { "Accessories", "Earrings", "oe" },
            new[] { "Costumes", "Costumes", "ca" },
            new[] { "Potions", "Life", "pl" }, new[] { "Potions", "Mana", "pm" }, new[] { "Potions", "Stamina", "ps" },
            new[] { "Upgrades", "Sheltoms", "os" }, new[] { "Upgrades", "Force orbs", "fo", "of" }, new[] { "Upgrades", "Bellatra stones", "se" },
            new[] { "Crystals", "Monster crystals", "gp" },
            new[] { "Crafting", "Ores", "pr1" }, new[] { "Crafting", "Crystals", "pr2" }, new[] { "Crafting", "Runes", "pr3", "pr4" }, new[] { "Crafting", "Runestones", "pr5" },
            new[] { "Crafting", "Recipes", "dr", "wr" }, new[] { "Crafting", "Ingredients", "ma" },
            new[] { "Premium", "Premium items", "bi" }, new[] { "Premium", "Boosters and stones", "bc" }, new[] { "Premium", "Cores", "ec" },
            new[] { "Quest and event", "Quest items", "qt" }, new[] { "Quest and event", "Wings", "qw" }, new[] { "Quest and event", "Puzzle pieces", "pz" },
            new[] { "Quest and event", "Seals", "sd" }, new[] { "Quest and event", "Gifts", "gf" }, new[] { "Quest and event", "Events", "sp" }, new[] { "Quest and event", "Sweets", "ch" }
        };

        public static readonly string[] GroupOrder = { "Weapons", "Armour", "Accessories", "Costumes", "Potions", "Upgrades", "Crystals", "Crafting", "Premium", "Quest and event", "Other" };

        public static void Classify(string code, out string group, out string category)
        {
            string c = (code ?? "").ToLowerInvariant();
            int best = 0;
            group = "Other";
            category = c.Length >= 2 ? c.Substring(0, 2).ToUpperInvariant() : "?";
            foreach (string[] row in Table)
                for (int i = 2; i < row.Length; i++)
                    if (row[i].Length > best && c.StartsWith(row[i], StringComparison.Ordinal))
                    {
                        best = row[i].Length;
                        group = row[0];
                        category = row[1];
                    }
        }

        static int FirstInt(string value)
        {
            List<string> t = Tokens.Split(value ?? "");
            return t.Count > 0 && int.TryParse(t[0], out int v) ? v : 0;
        }

        public static ItemCatalog Load(string dir)
        {
            var c = new ItemCatalog();
            if (!Directory.Exists(dir)) return c;
            foreach (string f in Directory.EnumerateFiles(dir, "*.txt").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                TextDoc doc;
                try { doc = TextDoc.Load(f); }
                catch (IOException) { continue; }
                string code = Tokens.Unquote(doc.First("*코드")).ToLowerInvariant();
                if (code.Length == 0) continue;
                bool ownFile = string.Equals(Path.GetFileNameWithoutExtension(f), code, StringComparison.OrdinalIgnoreCase);
                // some codes have a second file, such as 2_WA108.txt: the plainly named file wins
                if (c.ByCode.ContainsKey(code) && !ownFile) continue;
                Classify(code, out string group, out string cat);
                string korean = Tokens.Unquote(doc.First("*이름"));
                string name = LocalName.For(f, doc);
                var item = new ShopItem
                {
                    Code = code,
                    Korean = korean,
                    Name = name.Length > 0 ? name : korean.Length > 0 ? korean : code,
                    FileName = Path.GetFileName(f),
                    Level = FirstInt(doc.First("*레벨")),
                    Price = FirstInt(doc.First("*가격")),
                    Group = group,
                    Category = cat
                };
                if (c.ByCode.TryGetValue(code, out ShopItem old)) c.Items.Remove(old);
                c.ByCode[code] = item;
                c.Items.Add(item);
            }
            c.Items.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return c;
        }
    }

    /// <summary>
    /// An NPC's shop: three "*key code code..." sections, each a tab in the game's shop window. Any item can
    /// go in any section. "없음" means empty. Keeps the file's own spacing and only touches codes that change.
    /// </summary>
    public static class NpcShop
    {
        public static readonly string[] Keys = { "*무기판매", "*방어구판매", "*잡화판매" };
        public static readonly string[] Titles = { "Weapons", "Armour", "Goods" };
        public const string Nothing = "없음";
        /// <summary>The largest shop section on the test server holds 31, which suggests a limit of 32. Unconfirmed in game.</summary>
        public const int SuggestedLimit = 32;

        public static List<string> Read(TextDoc doc, string key)
        {
            var r = new List<string>();
            foreach (DocEntry e in doc.Entries())
                if (e.Key == key)
                    r.AddRange(Tokens.Split(e.Value).Where(t => t != Nothing));
            return r;
        }

        public static void Write(TextDoc doc, string key, IList<string> codes)
        {
            if (Read(doc, key).SequenceEqual(codes, StringComparer.OrdinalIgnoreCase)) return;
            List<DocEntry> entries = doc.Entries().Where(e => e.Key == key).ToList();
            if (entries.Count == 0)
            {
                if (codes.Count > 0) doc.AddEntry(key, string.Join(" ", codes));
                return;
            }
            // walk the existing codes: whatever still comes next in the new list stays where it is, with its spacing
            int next = 0;
            var bodies = new string[entries.Count];
            var tails = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                string v = entries[i].Value;
                int comment = v.IndexOf("//", StringComparison.Ordinal);
                tails[i] = comment >= 0 ? "  " + v.Substring(comment) : "";
                string body = comment >= 0 ? v.Substring(0, comment) : v;
                var sb = new StringBuilder();
                foreach (Match m in Regex.Matches(body, @"(\s*)(\S+)"))
                {
                    string token = m.Groups[2].Value;
                    if (token == Nothing) continue;
                    if (next < codes.Count && string.Equals(token, codes[next], StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(m.Groups[1].Value).Append(token);
                        next++;
                    }
                }
                bodies[i] = sb.ToString().Trim();
            }
            // anything left is new or moved, and goes on the end
            if (next < codes.Count)
            {
                int last = entries.Count - 1;
                string extra = string.Join(" ", codes.Skip(next));
                bodies[last] = bodies[last].Length > 0 ? bodies[last] + " " + extra : extra;
            }
            bool anything = bodies.Any(b => b.Length > 0);
            if (!anything) bodies[0] = Nothing;
            for (int i = 0; i < entries.Count; i++)
                if (bodies[i].Length > 0) doc.SetValue(entries[i].Line, bodies[i] + tails[i]);
            for (int i = entries.Count - 1; i >= 0; i--)
                if (bodies[i].Length == 0) doc.RemoveLine(entries[i].Line);
        }
    }
}
