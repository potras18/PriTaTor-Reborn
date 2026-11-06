// The Text Files tab: browse the whole server folder and edit any text settings file.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PriTaTor.Core;

namespace PriTaTor.UI
{
    public sealed class TextFilesPanel : UserControl, IEditorPanel
    {
        static readonly HashSet<string> NotText = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".smd", ".smb", ".inx", ".tga", ".bmp", ".exe", ".dll", ".rar", ".zip", ".db", ".psd", ".max",
            ".jpg", ".jpeg", ".png", ".spp", ".spc", ".wav", ".mp3", ".pdb", ".ase"
        };

        readonly MainForm host;
        readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
        readonly CodeEditor editor = new CodeEditor { Dock = DockStyle.Fill };
        readonly Label pathLabel = new Label { Dock = DockStyle.Top, Height = 20, Padding = new Padding(3, 3, 0, 0), AutoEllipsis = true };
        readonly Button saveBtn;
        string root;
        TextDoc doc;
        bool edited, selecting;

        public TextFilesPanel(MainForm host)
        {
            this.host = host;
            saveBtn = Ui.Button("Save", (s, e) => SaveCurrent());
            var top = Ui.Row();
            top.Controls.Add(saveBtn);
            top.Controls.Add(Ui.Button("Revert", (s, e) => { if (doc != null) OpenFile(doc.Path); }));
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(editor);
            right.Controls.Add(top);
            right.Controls.Add(pathLabel);
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(tree);
            split.Panel2.Controls.Add(right);
            Controls.Add(split);
            Ui.SplitWhenSized(split, w => 300);
            editor.TextEdited += (s, e) => { edited = true; UpdateButtons(); };
            tree.BeforeExpand += (s, e) => Expand(e.Node);
            tree.BeforeSelect += (s, e) =>
            {
                if (selecting) return;
                if (e.Node.Tag is string p && File.Exists(p) && !string.Equals(p, doc?.Path, StringComparison.OrdinalIgnoreCase) && !ConfirmLeave()) e.Cancel = true;
            };
            tree.AfterSelect += (s, e) =>
            {
                if (!selecting && e.Node.Tag is string p && File.Exists(p)) OpenFile(p);
            };
            UpdateButtons();
        }

        public void Bind(string serverRoot)
        {
            root = serverRoot;
            tree.Nodes.Clear();
            var n = new TreeNode(Path.GetFileName(root.TrimEnd('\\'))) { Tag = root };
            n.Nodes.Add(new TreeNode("…"));
            tree.Nodes.Add(n);
            n.Expand();
        }

        void Expand(TreeNode n)
        {
            if (n.Nodes.Count != 1 || n.Nodes[0].Tag != null || !(n.Tag is string dir)) return;
            tree.BeginUpdate();
            n.Nodes.Clear();
            try
            {
                foreach (string d in Directory.EnumerateDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string folder = Path.GetFileName(d);
                    if (string.Equals(folder, SafeWrite.BackupFolderName, StringComparison.OrdinalIgnoreCase) || string.Equals(folder, TerrainCache.FolderName, StringComparison.OrdinalIgnoreCase)) continue;
                    var c = new TreeNode(Path.GetFileName(d)) { Tag = d };
                    c.Nodes.Add(new TreeNode("…"));
                    n.Nodes.Add(c);
                }
                foreach (string f in Directory.EnumerateFiles(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (NotText.Contains(Path.GetExtension(f))) continue;
                    n.Nodes.Add(new TreeNode(Path.GetFileName(f)) { Tag = f, ForeColor = Color.FromArgb(0, 70, 140) });
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            tree.EndUpdate();
        }

        public void OpenFile(string path)
        {
            byte[] head;
            try
            {
                using (var f = File.OpenRead(path))
                {
                    head = new byte[Math.Min(8192, f.Length)];
                    f.Read(head, 0, head.Length);
                }
            }
            catch (IOException ex) { Ui.Error(this, ex.Message); return; }
            if (head.Contains((byte)0))
            {
                doc = null;
                pathLabel.Text = path;
                editor.SetText("This file holds binary data, so it can't be edited as text.");
                editor.ReadOnly = true;
                edited = false;
                UpdateButtons();
                return;
            }
            try { doc = TextDoc.Load(path); }
            catch (IOException ex) { Ui.Error(this, ex.Message); return; }
            editor.ReadOnly = false;
            editor.SetText(doc.GetAllText());
            edited = false;
            pathLabel.Text = path;
            UpdateButtons();
        }

        public void ShowFile(string path)
        {
            OpenFile(path);
            selecting = true;
            string full = Path.GetFullPath(path);
            string rootFull = Path.GetFullPath(root).TrimEnd('\\');
            if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && tree.Nodes.Count > 0)
            {
                TreeNode cur = tree.Nodes[0];
                string rest = full.Substring(rootFull.Length).Trim('\\');
                foreach (string part in rest.Split('\\'))
                {
                    Expand(cur);
                    cur.Expand();
                    TreeNode next = cur.Nodes.Cast<TreeNode>().FirstOrDefault(n => string.Equals(n.Text, part, StringComparison.OrdinalIgnoreCase));
                    if (next == null) break;
                    cur = next;
                }
                tree.SelectedNode = cur;
                cur.EnsureVisible();
            }
            selecting = false;
        }

        void UpdateButtons()
        {
            saveBtn.Enabled = HasUnsaved;
            host?.SetUnsavedMark();
        }

        public bool HasUnsaved => doc != null && edited;

        public void SaveCurrent()
        {
            if (!HasUnsaved) return;
            try
            {
                if (doc.ChangedOnDisk() && MessageBox.Show(this, Path.GetFileName(doc.Path) + " changed on disk since you opened it. Overwrite?", "PriTaTor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                doc.SetAllText(editor.EditorText);
                bool wrote = SafeWrite.Save(doc.Path, doc.ToBytes());
                doc.MarkSaved();
                edited = false;
                host.SetStatus(wrote ? "Saved " + Path.GetFileName(doc.Path) + ". Reload the server for this to take effect." : "Nothing needed saving.");
                UpdateButtons();
            }
            catch (IOException ex) { Ui.Error(this, "Saving failed:\n" + ex.Message); }
            catch (UnauthorizedAccessException ex) { Ui.Error(this, "Saving failed:\n" + ex.Message); }
        }

        public bool ConfirmLeave()
        {
            if (!HasUnsaved) return true;
            DialogResult r = Ui.AskSave(this, Path.GetFileName(doc.Path));
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) { SaveCurrent(); return !HasUnsaved; }
            edited = false;
            return true;
        }
    }
}
