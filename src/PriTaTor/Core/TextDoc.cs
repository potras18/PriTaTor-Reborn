// Line-preserving text documents for Priston Tale's settings files.
//
// The game's files are CP949 (Korean) with mixed line endings and sometimes invalid bytes.
// Every line keeps its original bytes, and only lines that were actually edited are re-encoded, so
// saving a file changes nothing except what the user changed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PriTaTor.Core
{
    public static class Cp949
    {
        public static readonly Encoding Enc = Encoding.GetEncoding(949);
        public static string Decode(byte[] b, int off, int count) => Enc.GetString(b, off, count);
        public static byte[] Encode(string s) => Enc.GetBytes(s ?? "");
    }

    public sealed class TextLine
    {
        public string Text;      // decoded, no line ending
        public string Eol;       // "\r\n", "\n", "\r" or "" (last line)
        public byte[] Raw;       // original bytes including the ending, null when edited
    }

    /// <summary>A "*KEY value" line.</summary>
    public sealed class DocEntry
    {
        public int Line;
        public string Key;
        public string Value;
    }

    public class TextDoc
    {
        public string Path;
        public List<TextLine> Lines = new List<TextLine>();
        public bool Dirty;
        public DateTime LoadedWriteUtc;
        public long LoadedLength;
        protected string DefaultEol = "\r\n";

        public static TextDoc Load(string path)
        {
            var d = new TextDoc();
            d.LoadFrom(path);
            return d;
        }

        public void LoadFrom(string path)
        {
            Path = path;
            byte[] b = File.ReadAllBytes(path);
            var fi = new FileInfo(path);
            LoadedWriteUtc = fi.LastWriteTimeUtc;
            LoadedLength = fi.Length;
            Parse(b);
        }

        public void Parse(byte[] b)
        {
            Lines.Clear();
            int crlf = 0, lf = 0, start = 0;
            for (int i = 0; i <= b.Length; i++)
            {
                if (i == b.Length)
                {
                    if (i > start) Lines.Add(MakeLine(b, start, i, i));
                    break;
                }
                if (b[i] == (byte)'\n')
                {
                    bool cr = i > start && b[i - 1] == (byte)'\r';
                    if (cr) crlf++; else lf++;
                    Lines.Add(MakeLine(b, start, cr ? i - 1 : i, i + 1));
                    start = i + 1;
                }
            }
            DefaultEol = lf > crlf ? "\n" : "\r\n";
            Dirty = false;
        }

        static TextLine MakeLine(byte[] b, int start, int textEnd, int end)
        {
            var raw = new byte[end - start];
            Buffer.BlockCopy(b, start, raw, 0, raw.Length);
            int eolLen = end - textEnd;
            return new TextLine
            {
                Text = Cp949.Decode(b, start, textEnd - start),
                Eol = eolLen == 2 ? "\r\n" : eolLen == 1 ? "\n" : "",
                Raw = raw
            };
        }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            {
                for (int i = 0; i < Lines.Count; i++)
                {
                    TextLine l = Lines[i];
                    byte[] data = l.Raw ?? Cp949.Encode(l.Text + l.Eol);
                    ms.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
        }

        public string GetAllText() => string.Join("\n", Lines.Select(l => l.Text)) + (Lines.Count > 0 && Lines[Lines.Count - 1].Eol.Length > 0 ? "\n" : "");

        /// <summary>Replace the whole text. Lines whose text is unchanged keep their original bytes.</summary>
        public void SetAllText(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var pool = new Dictionary<string, Queue<TextLine>>();
            foreach (TextLine l in Lines)
            {
                if (!pool.TryGetValue(l.Text, out var q)) pool[l.Text] = q = new Queue<TextLine>();
                q.Enqueue(l);
            }
            string[] parts = text.Split('\n');
            bool endsWithNewline = parts.Length > 0 && parts[parts.Length - 1].Length == 0;
            int count = endsWithNewline ? parts.Length - 1 : parts.Length;
            var result = new List<TextLine>(count);
            for (int i = 0; i < count; i++)
            {
                bool last = i == count - 1;
                string wantEol = last && !endsWithNewline ? "" : DefaultEol;
                if (pool.TryGetValue(parts[i], out var q) && q.Count > 0)
                {
                    TextLine old = q.Dequeue();
                    if (old.Eol.Length > 0 != wantEol.Length > 0)
                        result.Add(new TextLine { Text = old.Text, Eol = wantEol });
                    else
                        result.Add(old);
                }
                else
                    result.Add(new TextLine { Text = parts[i], Eol = wantEol });
            }
            Lines = result;
            Dirty = true;
        }

        // ----- *KEY value access -----

        public static bool TryParseKeyLine(string text, out string key, out string value)
        {
            key = value = null;
            int i = 0;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            if (i >= text.Length || text[i] != '*') return false;
            int k = i;
            while (k < text.Length && text[k] != ' ' && text[k] != '\t' && text[k] != '"') k++;
            key = text.Substring(i, k - i);
            value = text.Substring(k).Trim();
            return key.Length > 1;
        }

        public List<DocEntry> Entries()
        {
            var r = new List<DocEntry>();
            for (int i = 0; i < Lines.Count; i++)
                if (TryParseKeyLine(Lines[i].Text, out string k, out string v))
                    r.Add(new DocEntry { Line = i, Key = k, Value = v });
            return r;
        }

        public string First(params string[] keys)
        {
            foreach (DocEntry e in Entries())
                foreach (string k in keys)
                    if (string.Equals(e.Key, k, StringComparison.OrdinalIgnoreCase))
                        return e.Value;
            return null;
        }

        /// <summary>Change a line's value, keeping its indentation and the spacing after the key.</summary>
        public void SetValue(int line, string value)
        {
            string text = Lines[line].Text;
            if (!TryParseKeyLine(text, out string key, out string oldValue)) throw new InvalidOperationException("Not a key line");
            int keyAt = text.IndexOf(key, StringComparison.Ordinal);
            int after = keyAt + key.Length;
            int v = after;
            while (v < text.Length && (text[v] == ' ' || text[v] == '\t')) v++;
            string sep = text.Substring(after, v - after);
            if (sep.Length == 0) sep = "\t\t";
            if (oldValue == value.Trim()) return;
            ReplaceLine(line, text.Substring(0, keyAt) + key + sep + value.Trim());
        }

        public void ReplaceLine(int line, string text)
        {
            TextLine old = Lines[line];
            if (old.Text == text) return;
            Lines[line] = new TextLine { Text = text, Eol = old.Eol.Length > 0 ? old.Eol : (line == Lines.Count - 1 ? "" : DefaultEol) };
            Dirty = true;
        }

        public void RemoveLine(int line)
        {
            bool wasLast = line == Lines.Count - 1;
            string eol = Lines[line].Eol;
            Lines.RemoveAt(line);
            if (wasLast && eol.Length == 0 && Lines.Count > 0)
            {
                // the file had no final newline: keep it that way
                TextLine last = Lines[Lines.Count - 1];
                Lines[Lines.Count - 1] = new TextLine { Text = last.Text, Eol = "" };
            }
            Dirty = true;
        }

        public void InsertLine(int index, string text)
        {
            index = Math.Max(0, Math.Min(index, Lines.Count));
            if (index == Lines.Count && Lines.Count > 0 && Lines[Lines.Count - 1].Eol.Length == 0)
            {
                // previous last line had no ending and is now in the middle
                TextLine last = Lines[Lines.Count - 1];
                Lines[Lines.Count - 1] = new TextLine { Text = last.Text, Eol = DefaultEol };
            }
            Lines.Insert(index, new TextLine { Text = text, Eol = DefaultEol });
            Dirty = true;
        }

        /// <summary>Add "*key value" after the last line with the same key, otherwise before the link line that ends most files.</summary>
        public int AddEntry(string key, string value, string beforeKey = "*연결파일")
        {
            List<DocEntry> all = Entries();
            int at = -1;
            foreach (DocEntry e in all)
                if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) at = e.Line + 1;
            if (at < 0)
                foreach (DocEntry e in all)
                    if (string.Equals(e.Key, beforeKey, StringComparison.Ordinal)) { at = e.Line; break; }
            if (at < 0) at = Lines.Count;
            InsertLine(at, key + "\t\t" + (value ?? "").Trim());
            return at;
        }

        public bool ChangedOnDisk()
        {
            if (Path == null || !File.Exists(Path)) return false;
            var fi = new FileInfo(Path);
            return fi.LastWriteTimeUtc != LoadedWriteUtc || fi.Length != LoadedLength;
        }

        public void MarkSaved()
        {
            byte[] data = ToBytes();
            Parse(data);
            var fi = new FileInfo(Path);
            LoadedWriteUtc = fi.LastWriteTimeUtc;
            LoadedLength = fi.Length;
        }
    }

    /// <summary>Splits a value into tokens, keeping "quoted text" together.</summary>
    public static class Tokens
    {
        public static List<string> Split(string s)
        {
            var r = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t') { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') break;
                if (c == '"')
                {
                    int e = s.IndexOf('"', i + 1);
                    if (e < 0) e = s.Length;
                    r.Add(s.Substring(i + 1, e - i - 1));
                    i = e + 1;
                    continue;
                }
                int k = i;
                while (k < s.Length && s[k] != ' ' && s[k] != '\t') k++;
                r.Add(s.Substring(i, k - i));
                i = k;
            }
            return r;
        }

        public static string Unquote(string s)
        {
            if (s == null) return "";
            var t = Split(s);
            return t.Count > 0 ? t[0].Trim() : "";
        }
    }
}
