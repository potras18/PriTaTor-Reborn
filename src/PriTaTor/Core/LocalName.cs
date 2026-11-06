// The name players see comes from a .zhoon file, which is linked from each data file by *연결파일:
//     *E_NAME  "Traveler Dela"
// The letter before _NAME is the language and varies between servers (E, J, B, T...). What matters is
// the text in the quotes, so any letter is accepted. Falls back to *Name in the data file.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PriTaTor.Core
{
    public static class LocalName
    {
        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        static readonly Regex NameLine = new Regex(@"^\s*\*([A-Za-z]+)_NAME(?![A-Za-z0-9_])\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>The first name in a .zhoon file, or "" when it has none.</summary>
        public static string FromZhoon(byte[] bytes)
        {
            // Latin-1 keeps one character per byte, so the name's own bytes can be decoded afterwards
            foreach (string raw in Latin1.GetString(bytes).Split('\n'))
            {
                Match m = NameLine.Match(raw.TrimEnd('\r'));
                if (!m.Success) continue;
                string v = m.Groups[2].Value;
                if (v.StartsWith("\"", StringComparison.Ordinal))
                {
                    int end = v.IndexOf('"', 1);
                    v = end > 0 ? v.Substring(1, end - 1) : v.Substring(1);
                }
                string name = Decode(Latin1.GetBytes(v), m.Groups[1].Value).Trim();
                if (name.Length > 0) return name;
            }
            return "";
        }

        /// <summary>Each language's text is saved in that language's own code page.</summary>
        static string Decode(byte[] b, string language)
        {
            if (b.All(x => x < 128)) return Encoding.ASCII.GetString(b);
            try { return StrictUtf8.GetString(b); }
            catch (ArgumentException) { }
            int page;
            switch (char.ToUpperInvariant(language[0]))
            {
                case 'J': page = 932; break;   // Japanese
                case 'T': page = 950; break;   // Traditional Chinese
                case 'C': page = 936; break;   // Simplified Chinese
                case 'K': page = 949; break;   // Korean
                case 'E':
                case 'B':
                case 'P':
                case 'S':
                case 'G':
                case 'F': page = 1252; break;  // English, Brazilian, Portuguese, Spanish, German, French
                default: page = 949; break;
            }
            return Encoding.GetEncoding(page).GetString(b);
        }

        /// <summary>The name players see for a data file: from its .zhoon link, otherwise *Name. "" when neither has one.</summary>
        public static string For(string path, TextDoc doc)
        {
            try
            {
                if (string.Equals(Path.GetExtension(path), ".zhoon", StringComparison.OrdinalIgnoreCase))
                    return FromZhoon(doc.ToBytes());
                string link = Tokens.Unquote(doc.First("*연결파일"));
                if (link.Length > 0)
                {
                    string zhoon = Path.Combine(Path.GetDirectoryName(path), link);
                    if (File.Exists(zhoon))
                    {
                        string name = FromZhoon(File.ReadAllBytes(zhoon));
                        if (name.Length > 0) return name;
                    }
                }
            }
            catch (IOException) { }
            catch (ArgumentException) { }   // a link holding characters a path can't have
            return Tokens.Unquote(doc.First("*Name", "*NAME"));
        }
    }
}
