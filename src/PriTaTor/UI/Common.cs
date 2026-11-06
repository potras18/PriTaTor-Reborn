// Shared UI pieces: layout helpers and the text editor with Korean-safe highlighting.

using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using PriTaTor.Core;

namespace PriTaTor.UI
{
    public interface IEditorPanel
    {
        bool HasUnsaved { get; }
        /// <summary>Returns false when the user cancelled or a save failed.</summary>
        bool ConfirmLeave();
        void SaveCurrent();
    }

    public static class Ui
    {
        public static Font MonoFont = new Font("Consolas", 10f);
        public static Font BaseFont = new Font("Segoe UI", 9f);
        public static Font BoldFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        public static Label Label(string text, bool bold = false) =>
            new Label { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3), Font = bold ? BoldFont : BaseFont };

        public static Button Button(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(3), FlatStyle = FlatStyle.System };
            b.Click += click;
            return b;
        }

        public static TableLayoutPanel Table(int cols)
        {
            var t = new TableLayoutPanel { ColumnCount = cols, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            for (int i = 0; i < cols; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        public static FlowLayoutPanel Row() =>
            new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Padding = new Padding(0) };

        /// <summary>A splitter can only be placed once the control has real size.</summary>
        public static void SplitWhenSized(SplitContainer s, Func<int, int> distance)
        {
            EventHandler h = null;
            h = (o, e) =>
            {
                int size = s.Orientation == Orientation.Vertical ? s.Width : s.Height;
                if (size < 400) return;
                int d = distance(size);
                if (d < s.Panel1MinSize || d > size - s.Panel2MinSize - s.SplitterWidth) return;
                s.SizeChanged -= h;
                s.SplitterDistance = d;
            };
            s.SizeChanged += h;
        }

        public static void Error(IWin32Window owner, string message) =>
            MessageBox.Show(owner, message, "PriTaTor", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        public static DialogResult AskSave(IWin32Window owner, string what) =>
            MessageBox.Show(owner, Loc.F("Save your changes to {0}?", what), "PriTaTor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
    }

    /// <summary>Text box with a find bar. Colours keys, comments and quoted text when a file opens.</summary>
    public sealed class CodeEditor : UserControl
    {
        readonly RichTextBox box;
        readonly TextBox find;
        public event EventHandler TextEdited;
        bool loading;

        public CodeEditor()
        {
            box = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = Ui.MonoFont,
                WordWrap = false,
                DetectUrls = false,
                AcceptsTab = true,
                HideSelection = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                BorderStyle = BorderStyle.None
            };
            box.TextChanged += (s, e) => { if (!loading) TextEdited?.Invoke(this, EventArgs.Empty); };
            box.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.F) { find.Focus(); find.SelectAll(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.F3) { FindNext(); e.SuppressKeyPress = true; }
                // pasted text keeps our font, not its source's
                if (e.Control && e.KeyCode == Keys.V) { box.Paste(DataFormats.GetFormat(DataFormats.UnicodeText)); e.SuppressKeyPress = true; }
            };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            find = new TextBox { Dock = DockStyle.Fill };
            find.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { FindNext(); e.SuppressKeyPress = true; } };
            var lab = new Label { Text = "Find:", Dock = DockStyle.Left, AutoSize = true, Padding = new Padding(4, 5, 4, 0) };
            var next = new Button { Text = "Next", Dock = DockStyle.Right, Width = 60, FlatStyle = FlatStyle.System };
            next.Click += (s, e) => FindNext();
            bar.Controls.Add(find);
            bar.Controls.Add(next);
            bar.Controls.Add(lab);
            Controls.Add(box);
            Controls.Add(bar);
        }

        public string EditorText => box.Text;
        public bool ReadOnly { get => box.ReadOnly; set => box.ReadOnly = value; }

        void FindNext()
        {
            string q = find.Text;
            if (q.Length == 0) return;
            int from = box.SelectionStart + box.SelectionLength;
            int at = box.Find(q, from, RichTextBoxFinds.None);
            if (at < 0 && from > 0) at = box.Find(q, 0, RichTextBoxFinds.None);
            if (at >= 0) box.ScrollToCaret();
        }

        public void SetText(string text)
        {
            loading = true;
            try
            {
                if (text.Length > 800000) { box.Rtf = null; box.Text = text; }
                else box.Rtf = ToRtf(text);
                box.SelectionStart = 0;
                box.SelectionLength = 0;
                box.ClearUndo();
                box.Modified = false;
            }
            finally { loading = false; }
        }

        static void AppendEscaped(StringBuilder sb, string s, int from, int to)
        {
            bool korean = false;
            for (int i = from; i < to; i++)
            {
                char c = s[i];
                if (c >= 128 && !korean) { sb.Append(@"\f1 "); korean = true; }
                else if (c < 128 && korean && c != ' ') { sb.Append(@"\f0 "); korean = false; }
                if (c == '\\' || c == '{' || c == '}') { sb.Append('\\').Append(c); }
                else if (c == '\t') sb.Append("\\tab ");
                else if (c < 128) sb.Append(c);
                else sb.Append("\\u").Append((short)c).Append('?');
            }
            if (korean) sb.Append(@"\f0 ");
        }

        static string ToRtf(string text)
        {
            var sb = new StringBuilder(text.Length * 2);
            sb.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fmodern\fcharset0 Consolas;}{\f1\fnil\fcharset129 Malgun Gothic;}}");
            sb.Append(@"{\colortbl;\red30\green30\blue30;\red0\green128\blue64;\red0\green70\blue170;\red160\green70\blue20;}\f0\fs20 ");
            string[] lines = text.Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string l = lines[n];
                int i = 0;
                while (i < l.Length && (l[i] == ' ' || l[i] == '\t')) i++;
                if (i + 1 < l.Length && l[i] == '/' && l[i + 1] == '/')
                {
                    sb.Append(@"\cf2 ");
                    AppendEscaped(sb, l, 0, l.Length);
                }
                else if (i < l.Length && l[i] == '*')
                {
                    int k = i;
                    while (k < l.Length && l[k] != ' ' && l[k] != '\t' && l[k] != '"') k++;
                    sb.Append(@"\cf3 ");
                    AppendEscaped(sb, l, 0, k);
                    sb.Append(@"\cf4 ");
                    AppendEscaped(sb, l, k, l.Length);
                }
                else
                {
                    sb.Append(@"\cf1 ");
                    AppendEscaped(sb, l, 0, l.Length);
                }
                if (n < lines.Length - 1) sb.Append(@"\par ");
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
