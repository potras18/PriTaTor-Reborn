// <map>.ase.spm: who spawns on a map. Plain text; comments and spacing are kept.

using System;
using System.Collections.Generic;
using System.Linq;

namespace PriTaTor.Core
{
    public sealed class SpmActor
    {
        public string Monster = "";
        public string Percent = "";
    }

    public sealed class SpmBoss
    {
        public string Boss = "";
        public string Guard = "";
        public string GuardCount = "";
        public string Hours = "";
    }

    public sealed class SpmDoc : TextDoc
    {
        public static readonly string[] KeysMax = { "*최대동시출현수", "*MAX_ACTOR_POS" };
        public static readonly string[] KeysInterval = { "*출현간격" };
        public static readonly string[] KeysCount = { "*출현수", "*MAX_ACTOR" };
        public static readonly string[] KeysActor = { "*출연자", "*ACTOR" };
        public static readonly string[] KeysBoss = { "*출연자두목", "*BOSS_ACTOR" };

        public static readonly string Template =
            "//몬스터 출현 비율\r\n\r\n*최대동시출현수\t\t100\r\n\r\n//출현간격 범위 9-1 1차이당 2배씩 빨라짐\r\n" +
            "*출현간격\t\t5\t17\r\n*출현수\t\t\t2\r\n\r\n//\t\t몬스터 이름          출현빈도\r\n";

        public static new SpmDoc Load(string path)
        {
            var d = new SpmDoc();
            d.LoadFrom(path);
            return d;
        }

        public static SpmDoc CreateNew(string path)
        {
            var d = new SpmDoc { Path = path };
            d.Parse(Cp949.Encode(Template));
            d.Dirty = true;
            return d;
        }

        static bool Is(string key, string[] keys) => keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

        public string Get(string[] keys) => First(keys) ?? "";

        public void Set(string[] keys, string value)
        {
            foreach (DocEntry e in Entries())
                if (Is(e.Key, keys)) { SetValue(e.Line, value); return; }
            if (value.Trim().Length == 0) return;
            int at = 0;
            List<DocEntry> all = Entries();
            DocEntry firstActor = all.FirstOrDefault(e => Is(e.Key, KeysActor) || Is(e.Key, KeysBoss));
            at = firstActor != null ? firstActor.Line : Lines.Count;
            InsertLine(at, keys[0] + "\t\t" + value.Trim());
        }

        public List<SpmActor> Actors()
        {
            var r = new List<SpmActor>();
            foreach (DocEntry e in Entries())
                if (Is(e.Key, KeysActor))
                {
                    List<string> t = Tokens.Split(e.Value);
                    r.Add(new SpmActor { Monster = t.Count > 0 ? t[0] : "", Percent = t.Count > 1 ? t[1] : "" });
                }
            return r;
        }

        public List<SpmBoss> Bosses()
        {
            var r = new List<SpmBoss>();
            foreach (DocEntry e in Entries())
                if (Is(e.Key, KeysBoss))
                {
                    List<string> t = Tokens.Split(e.Value);
                    r.Add(new SpmBoss
                    {
                        Boss = t.Count > 0 ? t[0] : "",
                        Guard = t.Count > 1 ? t[1] : "",
                        GuardCount = t.Count > 2 ? t[2] : "",
                        Hours = t.Count > 3 ? string.Join(" ", t.Skip(3)) : ""
                    });
                }
            return r;
        }

        static string FormatActor(SpmActor a) => "\"" + a.Monster + "\"\t\t" + a.Percent;
        static string FormatBoss(SpmBoss b) => ("\"" + b.Boss + "\"\t\t\"" + b.Guard + "\"\t\t" + b.GuardCount + "\t" + b.Hours).TrimEnd();

        public void SetActors(IList<SpmActor> list) =>
            SetList(KeysActor, list.Select(FormatActor).ToList(), list.Select(a => Tuple.Create(a.Monster, a.Percent, "", "")).ToList(), Tok2);

        public void SetBosses(IList<SpmBoss> list) =>
            SetList(KeysBoss, list.Select(FormatBoss).ToList(), list.Select(b => Tuple.Create(b.Boss, b.Guard, b.GuardCount, b.Hours)).ToList(), Tok4);

        static Tuple<string, string, string, string> Tok2(string v)
        {
            List<string> t = Tokens.Split(v);
            return Tuple.Create(t.Count > 0 ? t[0] : "", t.Count > 1 ? t[1] : "", "", "");
        }

        static Tuple<string, string, string, string> Tok4(string v)
        {
            List<string> t = Tokens.Split(v);
            return Tuple.Create(t.Count > 0 ? t[0] : "", t.Count > 1 ? t[1] : "", t.Count > 2 ? t[2] : "", t.Count > 3 ? string.Join(" ", t.Skip(3)) : "");
        }

        /// <summary>Update rows in place: same position keeps its line unless its values differ.</summary>
        void SetList(string[] keys, List<string> formatted, List<Tuple<string, string, string, string>> wanted, Func<string, Tuple<string, string, string, string>> parse)
        {
            List<DocEntry> existing = Entries().Where(e => Is(e.Key, keys)).ToList();
            int common = Math.Min(existing.Count, formatted.Count);
            for (int i = 0; i < common; i++)
                if (!parse(existing[i].Value).Equals(wanted[i]))
                    SetValue(existing[i].Line, formatted[i]);
            for (int i = existing.Count - 1; i >= common; i--)
                RemoveLine(existing[i].Line);
            for (int i = common; i < formatted.Count; i++)
                AddEntry(keys[0], formatted[i], "");
        }
    }
}
