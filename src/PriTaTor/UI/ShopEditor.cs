// Pick what an NPC sells by category and name instead of typing item codes.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PriTaTor.Core;

namespace PriTaTor.UI
{
    public sealed class ShopEditor : UserControl
    {
        readonly Func<ServerData> data;
        readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, FullRowSelect = true };
        readonly TextBox search = new TextBox { Dock = DockStyle.Top };
        readonly ListView picker = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
        readonly TabControl sections = new TabControl { Dock = DockStyle.Fill };
        readonly ListView[] lists = new ListView[NpcShop.Keys.Length];
        readonly Label info = new Label { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(3, 3, 3, 0) };
        readonly Label loadingNote = new Label { Dock = DockStyle.Top, Height = 20, Text = "Loading items…", ForeColor = Color.Gray, Padding = new Padding(2) };

        TextDoc doc;
        ItemCatalog catalog;
        string group, category;   // null = all
        int sortColumn = 2;
        bool sortDown;
        bool loadingCatalog, filling;
        public event Action Changed;
        public event Action CatalogReady;

        public ShopEditor(Func<ServerData> data)
        {
            this.data = data;

            picker.Columns.Add("Item", 210);
            picker.Columns.Add("Code", 60);
            picker.Columns.Add("Level", 46, HorizontalAlignment.Right);
            picker.Columns.Add("Price", 66, HorizontalAlignment.Right);
            picker.ColumnClick += (s, e) =>
            {
                if (sortColumn == e.Column) sortDown = !sortDown;
                else { sortColumn = e.Column; sortDown = false; }
                FillPicker();
            };
            picker.DoubleClick += (s, e) => AddSelected();
            picker.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddSelected(); e.SuppressKeyPress = true; } };
            search.TextChanged += (s, e) => FillPicker();
            tree.AfterSelect += (s, e) =>
            {
                var tag = e.Node.Tag as string[];
                group = tag?[0];
                category = tag?[1];
                FillPicker();
            };

            for (int i = 0; i < lists.Length; i++)
            {
                var l = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
                l.Columns.Add("Item", 200);
                l.Columns.Add("Code", 60);
                l.Columns.Add("Level", 44, HorizontalAlignment.Right);
                l.DoubleClick += (s, e) => RemoveSelected();
                l.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.SuppressKeyPress = true; } };
                lists[i] = l;
                var page = new TabPage(NpcShop.Titles[i]);
                page.Controls.Add(l);
                sections.TabPages.Add(page);
            }
            sections.SelectedIndexChanged += (s, e) => { FillPicker(); UpdateInfo(); };

            var addBtn = new Button { Text = "Add to shop  ▶", Dock = DockStyle.Bottom, Height = 32, FlatStyle = FlatStyle.System };
            addBtn.Click += (s, e) => AddSelected();
            var middle = new Panel { Dock = DockStyle.Fill };
            middle.Controls.Add(picker);
            middle.Controls.Add(search);
            middle.Controls.Add(new Label { Text = "Search by name or code", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2) });
            middle.Controls.Add(addBtn);

            var buttons = Ui.Row();
            buttons.Dock = DockStyle.Bottom;
            buttons.Controls.Add(Ui.Button("Remove", (s, e) => RemoveSelected()));
            buttons.Controls.Add(Ui.Button("Move up", (s, e) => Shift(-1)));
            buttons.Controls.Add(Ui.Button("Move down", (s, e) => Shift(1)));
            buttons.Controls.Add(Ui.Button("Move to tab…", (s, e) => ShowMoveMenu((Control)s)));
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(sections);
            right.Controls.Add(new Label { Text = "What this NPC sells", Dock = DockStyle.Top, Height = 20, Font = Ui.BoldFont, Padding = new Padding(2) });
            right.Controls.Add(buttons);
            right.Controls.Add(info);

            var inner = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            inner.Panel1.Controls.Add(middle);
            inner.Panel2.Controls.Add(right);
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(tree);
            left.Controls.Add(loadingNote);
            var outer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            outer.Panel1.Controls.Add(left);
            outer.Panel2.Controls.Add(inner);
            Controls.Add(outer);
            Ui.SplitWhenSized(outer, w => 190);
            Ui.SplitWhenSized(inner, w => Math.Max(200, w - 370));
        }

        public void Bind(TextDoc d)
        {
            doc = d;
            StartCatalog();
            FillLists();
            FillPicker();
            UpdateInfo();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            StartCatalog();
        }

        void StartCatalog()
        {
            if (catalog != null || loadingCatalog || !IsHandleCreated) return;
            loadingCatalog = true;
            ServerData d = data();
            Task.Run(() => d.Items.Value).ContinueWith(t =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    loadingCatalog = false;
                    loadingNote.Visible = false;
                    catalog = t.IsFaulted ? new ItemCatalog() : t.Result;
                    FillTree();
                    FillLists();
                    FillPicker();
                    UpdateInfo();
                    CatalogReady?.Invoke();
                }));
            });
        }

        /// <summary>The server's items may have changed: read them again next time.</summary>
        public void ResetCatalog()
        {
            catalog = null;
            if (IsHandleCreated && doc != null) StartCatalog();
        }

        void FillTree()
        {
            filling = true;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            TreeNode all = tree.Nodes.Add("All items (" + catalog.Items.Count + ")");
            foreach (string g in ItemCatalog.GroupOrder)
            {
                List<ShopItem> inGroup = catalog.Items.Where(i => i.Group == g).ToList();
                if (inGroup.Count == 0) continue;
                var cats = inGroup.GroupBy(i => i.Category).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
                var node = new TreeNode(g + " (" + inGroup.Count + ")") { Tag = new[] { g, null } };
                if (cats.Count > 1)
                    foreach (var c in cats)
                        node.Nodes.Add(new TreeNode(c.Key + " (" + c.Count() + ")") { Tag = new[] { g, c.Key } });
                tree.Nodes.Add(node);
            }
            tree.ExpandAll();
            tree.EndUpdate();
            tree.SelectedNode = all;
            all.EnsureVisible();
            filling = false;
        }

        IEnumerable<ShopItem> Filtered()
        {
            if (catalog == null) return Enumerable.Empty<ShopItem>();
            IEnumerable<ShopItem> q = catalog.Items;
            if (group != null) q = q.Where(i => i.Group == group);
            if (category != null) q = q.Where(i => i.Category == category);
            string term = search.Text.Trim();
            if (term.Length > 0)
                q = q.Where(i => i.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || i.Code.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || (i.Korean ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            switch (sortColumn)
            {
                case 0: q = q.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase); break;
                case 1: q = q.OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase); break;
                case 3: q = q.OrderBy(i => i.Price); break;
                default: q = q.OrderBy(i => i.Level).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase); break;
            }
            return sortDown ? q.Reverse() : q;
        }

        void FillPicker()
        {
            if (filling) return;
            var inShop = new HashSet<string>(CurrentCodes(), StringComparer.OrdinalIgnoreCase);
            var selected = new HashSet<string>(picker.SelectedItems.Cast<ListViewItem>().Select(x => ((ShopItem)x.Tag).Code));
            picker.BeginUpdate();
            picker.Items.Clear();
            foreach (ShopItem i in Filtered())
            {
                var it = new ListViewItem(new[] { i.Name, i.Code, i.Level > 0 ? i.Level.ToString() : "", i.Price > 0 ? i.Price.ToString("N0") : "" }) { Tag = i };
                if (inShop.Contains(i.Code)) it.ForeColor = Color.Gray;
                if (selected.Contains(i.Code)) it.Selected = true;
                picker.Items.Add(it);
            }
            picker.EndUpdate();
        }

        int Active => Math.Max(0, sections.SelectedIndex);
        List<string> CurrentCodes() => doc != null ? NpcShop.Read(doc, NpcShop.Keys[Active]) : new List<string>();

        void FillLists(IEnumerable<string> keepSelected = null)
        {
            var keep = keepSelected != null ? new HashSet<string>(keepSelected, StringComparer.OrdinalIgnoreCase) : null;
            for (int s = 0; s < lists.Length; s++)
            {
                List<string> codes = doc != null ? NpcShop.Read(doc, NpcShop.Keys[s]) : new List<string>();
                ListView l = lists[s];
                l.BeginUpdate();
                l.Items.Clear();
                foreach (string code in codes)
                {
                    ShopItem item = null;
                    catalog?.ByCode.TryGetValue(code, out item);
                    var it = new ListViewItem(new[] { item?.Name ?? (catalog != null ? "(no item file)" : ""), code, item != null && item.Level > 0 ? item.Level.ToString() : "" }) { Tag = code };
                    if (catalog != null && item == null) it.ForeColor = Color.Red;
                    if (keep != null && s == Active && keep.Contains(code)) it.Selected = true;
                    l.Items.Add(it);
                }
                l.EndUpdate();
                sections.TabPages[s].Text = NpcShop.Titles[s] + " (" + codes.Count + ")";
            }
        }

        void UpdateInfo()
        {
            int n = CurrentCodes().Count;
            bool over = n > NpcShop.SuggestedLimit;
            info.ForeColor = over ? Color.OrangeRed : SystemColors.GrayText;
            info.Text = over
                ? n + " items on this tab. The biggest tab on the test server holds 31, so more than " + NpcShop.SuggestedLimit + " may not show in game."
                : "Each section is a tab in the NPC's shop, and any item can go on any tab. Items already on this tab are grey on the left.";
        }

        void Commit(List<string> codes, int section, IEnumerable<string> keep = null)
        {
            NpcShop.Write(doc, NpcShop.Keys[section], codes);
            AfterChange(keep);
        }

        void AfterChange(IEnumerable<string> keep)
        {
            FillLists(keep);
            FillPicker();
            UpdateInfo();
            Changed?.Invoke();
        }

        public void AddSelected()
        {
            if (doc == null || picker.SelectedItems.Count == 0) return;
            List<string> codes = CurrentCodes();
            var added = new List<string>();
            foreach (ListViewItem it in picker.SelectedItems)
            {
                string code = ((ShopItem)it.Tag).Code;
                if (codes.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;
                codes.Add(code);
                added.Add(code);
            }
            if (added.Count == 0) return;
            Commit(codes, Active, added);
            ListView l = lists[Active];
            if (l.SelectedItems.Count > 0) l.SelectedItems[l.SelectedItems.Count - 1].EnsureVisible();
        }

        public void RemoveSelected()
        {
            ListView l = lists[Active];
            if (doc == null || l.SelectedItems.Count == 0) return;
            var gone = new HashSet<int>(l.SelectedIndices.Cast<int>());
            List<string> codes = CurrentCodes().Where((c, i) => !gone.Contains(i)).ToList();
            Commit(codes, Active);
        }

        void Shift(int by)
        {
            ListView l = lists[Active];
            if (doc == null || l.SelectedIndices.Count == 0) return;
            List<string> codes = CurrentCodes();
            var picked = l.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
            var keep = picked.Select(i => codes[i]).ToList();
            if (by < 0)
            {
                if (picked[0] == 0) return;
                foreach (int i in picked) { string t = codes[i - 1]; codes[i - 1] = codes[i]; codes[i] = t; }
            }
            else
            {
                if (picked[picked.Count - 1] == codes.Count - 1) return;
                foreach (int i in Enumerable.Reverse(picked)) { string t = codes[i + 1]; codes[i + 1] = codes[i]; codes[i] = t; }
            }
            Commit(codes, Active, keep);
        }

        void ShowMoveMenu(Control under)
        {
            if (lists[Active].SelectedItems.Count == 0) return;
            var menu = new ContextMenuStrip();
            for (int s = 0; s < lists.Length; s++)
            {
                if (s == Active) continue;
                int target = s;
                menu.Items.Add(NpcShop.Titles[s], null, (x, e) => MoveTo(target));
            }
            menu.Closed += (x, e) => BeginInvoke((Action)menu.Dispose);
            menu.Show(under, new Point(0, under.Height));
        }

        public void MoveTo(int target)
        {
            ListView l = lists[Active];
            if (doc == null || l.SelectedItems.Count == 0 || target == Active) return;
            int from = Active;
            var gone = new HashSet<int>(l.SelectedIndices.Cast<int>());
            List<string> source = CurrentCodes();
            List<string> moving = source.Where((c, i) => gone.Contains(i)).ToList();
            List<string> dest = NpcShop.Read(doc, NpcShop.Keys[target]);
            foreach (string c in moving)
                if (!dest.Contains(c, StringComparer.OrdinalIgnoreCase)) dest.Add(c);
            NpcShop.Write(doc, NpcShop.Keys[from], source.Where((c, i) => !gone.Contains(i)).ToList());
            NpcShop.Write(doc, NpcShop.Keys[target], dest);
            sections.SelectedIndex = target;
            AfterChange(moving);
        }

        // for automated checks
        public bool Ready => catalog != null;
        public int PickerCount => picker.Items.Count;
        public ItemCatalog Catalog => catalog;
        public void TestSection(int s) => sections.SelectedIndex = s;
        public void TestSearch(string s) => search.Text = s;
        public void TestCategory(string name)
        {
            foreach (TreeNode g in tree.Nodes)
            {
                if (g.Text.StartsWith(name + " (", StringComparison.Ordinal)) { tree.SelectedNode = g; return; }
                foreach (TreeNode c in g.Nodes)
                    if (c.Text.StartsWith(name + " (", StringComparison.Ordinal)) { tree.SelectedNode = c; return; }
            }
        }
        public void TestPick(int index)
        {
            foreach (ListViewItem it in picker.SelectedItems) it.Selected = false;
            picker.Items[index].Selected = true;
        }
        public void TestPickInShop(int index)
        {
            foreach (ListViewItem it in lists[Active].SelectedItems) it.Selected = false;
            lists[Active].Items[index].Selected = true;
        }
    }

    /// <summary>The shop editor in its own window, for an NPC picked on the map.</summary>
    public sealed class ShopDialog : Form
    {
        readonly TextDoc doc;
        public readonly ShopEditor Editor;
        public bool Saved { get; private set; }

        public ShopDialog(string path, Func<ServerData> data, string title)
        {
            doc = TextDoc.Load(path);
            Text = "Shop: " + title;
            Font = Ui.BaseFont;
            ClientSize = new Size(1040, 640);
            MinimumSize = new Size(800, 420);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            Editor = new ShopEditor(data) { Dock = DockStyle.Fill };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var save = new Button { Text = "Save", AutoSize = true };
            save.Click += (s, e) => { if (SaveDoc()) { DialogResult = DialogResult.OK; Close(); } };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(save);
            Controls.Add(Editor);
            Controls.Add(bottom);
            CancelButton = cancel;
            Shown += (s, e) => Editor.Bind(doc);
            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK || !doc.Dirty) return;
                DialogResult r = Ui.AskSave(this, Path.GetFileName(doc.Path));
                if (r == DialogResult.Cancel) e.Cancel = true;
                else if (r == DialogResult.Yes && !SaveDoc()) e.Cancel = true;
            };
        }

        public bool SaveDoc()
        {
            if (!doc.Dirty) return true;
            try
            {
                if (doc.ChangedOnDisk() && MessageBox.Show(this, Path.GetFileName(doc.Path) + " changed on disk since you opened it. Overwrite?", "PriTaTor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
                SafeWrite.Save(doc.Path, doc.ToBytes());
                doc.MarkSaved();
                Saved = true;
                return true;
            }
            catch (IOException ex) { Ui.Error(this, "Saving failed:\n" + ex.Message); }
            catch (UnauthorizedAccessException ex) { Ui.Error(this, "Saving failed:\n" + ex.Message); }
            return false;
        }

        public TextDoc Doc => doc;
    }
}
