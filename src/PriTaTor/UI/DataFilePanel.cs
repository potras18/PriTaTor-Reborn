// Monsters, NPCs, Items and Names: a searchable list of files, each shown as labelled fields or raw text.

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
    public sealed class DataFilePanel : UserControl, IEditorPanel
    {
        readonly MainForm host;
        readonly Schema schema;
        readonly string[] dirs;
        readonly string[] exts;
        readonly string extraKey;
        readonly string extraTitle;
        readonly Func<string, string> usedBy;

        readonly TextBox filter = new TextBox { Dock = DockStyle.Top };
        readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        readonly CodeEditor raw = new CodeEditor { Dock = DockStyle.Fill };
        readonly TabControl views = new TabControl { Dock = DockStyle.Fill };
        readonly Label pathLabel = new Label { Dock = DockStyle.Top, Height = 20, Padding = new Padding(3, 3, 0, 0), AutoEllipsis = true };
        readonly Label usedLabel = new Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(3, 3, 0, 0), AutoEllipsis = true };
        readonly ComboBox addKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        Button saveBtn;
        ShopEditor shop;
        TabPage shopPage;

        List<DataItem> items = new List<DataItem>();
        TextDoc doc;
        bool rawChanged, syncing, shopDriven;
        ListViewItem currentItem;
        public DataItem Current => currentItem?.Tag as DataItem;
        bool loaded;

        public DataFilePanel(MainForm host, Schema schema, string[] dirs, string[] exts, string extraKey, string extraTitle, Func<string, string> usedBy = null)
        {
            this.host = host;
            this.schema = schema;
            this.dirs = dirs;
            this.exts = exts;
            this.extraKey = extraKey;
            this.extraTitle = extraTitle;
            this.usedBy = usedBy;

            list.Columns.Add("File", 170);
            list.Columns.Add("Name", 140);
            list.Columns.Add("Korean", 110);
            if (extraTitle != null) list.Columns.Add(extraTitle, 70);

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "group", HeaderText = "Group", ReadOnly = true, Width = 100, DefaultCellStyle = { ForeColor = Color.Gray } });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "label", HeaderText = "Field", ReadOnly = true, Width = 170 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "key", HeaderText = "Key", ReadOnly = true, Width = 120, DefaultCellStyle = { ForeColor = Color.Gray } });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DefaultCellStyle = { Font = Ui.MonoFont } });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "help", HeaderText = "What it does", ReadOnly = true, Width = 260, DefaultCellStyle = { ForeColor = Color.Gray } });
            grid.CellEndEdit += GridEdited;
            raw.TextEdited += (s, e) => { rawChanged = true; UpdateButtons(); };

            var tf = new TabPage("Fields");
            tf.Controls.Add(grid);
            var tr = new TabPage("Raw text");
            tr.Controls.Add(raw);
            views.TabPages.Add(tf);
            views.TabPages.Add(tr);
            if (schema == Schema.Npc)
            {
                shop = new ShopEditor(() => host.Data) { Dock = DockStyle.Fill };
                shop.Changed += ShopChanged;
                shopPage = new TabPage("Shop");
                shopPage.Controls.Add(shop);
                views.TabPages.Add(shopPage);
            }
            views.Selecting += (s, e) => { if (!SyncViews()) e.Cancel = true; };

            var top = Ui.Row();
            saveBtn = Ui.Button("Save", (s, e) => SaveCurrent());
            top.Controls.Add(saveBtn);
            top.Controls.Add(Ui.Button("Revert", (s, e) => Revert()));
            top.Controls.Add(Ui.Label("Add field:"));
            top.Controls.Add(addKey);
            top.Controls.Add(Ui.Button("Add", (s, e) => AddField()));
            top.Controls.Add(Ui.Button("Remove row", (s, e) => RemoveRow()));
            top.Controls.Add(Ui.Button("Open name file", (s, e) => OpenLinked()));
            top.Controls.Add(Ui.Button("New copy…", (s, e) => NewCopy()));
            top.Controls.Add(Ui.Button("Delete file", (s, e) => DeleteFile()));

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(views);
            right.Controls.Add(usedLabel);
            right.Controls.Add(top);
            right.Controls.Add(pathLabel);

            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(list);
            left.Controls.Add(filter);
            left.Controls.Add(new Label { Text = "Search name or file", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2) });

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            Controls.Add(split);
            Ui.SplitWhenSized(split, w => 470);

            filter.TextChanged += (s, e) => FillList();
            list.SelectedIndexChanged += ListSelected;
            UpdateButtons();
        }

        /// <summary>Read the file list the first time the tab is shown.</summary>
        public void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            Cursor = Cursors.WaitCursor;
            Task.Run(() =>
            {
                var all = new List<DataItem>();
                foreach (string d in dirs) all.AddRange(ServerData.ReadFolder(d, exts, extraKey));
                BeginInvoke((Action)(() =>
                {
                    items = all;
                    FillList();
                    Cursor = Cursors.Default;
                }));
            });
        }

        void FillList()
        {
            string q = filter.Text.Trim();
            string currentPath = Current?.Path;
            syncing = true;
            list.BeginUpdate();
            list.Items.Clear();
            currentItem = null;
            foreach (DataItem d in items)
            {
                if (q.Length > 0 && (d.FileName + " " + d.Name + " " + d.LocalName + " " + d.Extra).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var row = new List<string> { d.FileName, d.LocalName ?? "", d.Name ?? "" };
                if (extraTitle != null) row.Add(d.Extra ?? "");
                var it = new ListViewItem(row.ToArray()) { Tag = d };
                list.Items.Add(it);
                if (d.Path == currentPath) { it.Selected = true; currentItem = it; }
            }
            list.EndUpdate();
            syncing = false;
        }

        void ListSelected(object s, EventArgs e)
        {
            if (syncing || list.SelectedItems.Count == 0) return;
            ListViewItem it = list.SelectedItems[0];
            if (it == currentItem) return;
            if (!ConfirmLeave())
            {
                syncing = true;
                it.Selected = false;
                if (currentItem != null) currentItem.Selected = true;
                syncing = false;
                return;
            }
            currentItem = it;
            OpenFile(((DataItem)it.Tag).Path);
        }

        public bool SelectByFile(string fileName)
        {
            EnsureLoadedNow();
            filter.Text = "";
            foreach (ListViewItem it in list.Items)
                if (string.Equals(((DataItem)it.Tag).FileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    it.Selected = true;
                    it.EnsureVisible();
                    return true;
                }
            return false;
        }

        void EnsureLoadedNow()
        {
            if (items.Count > 0) return;
            loaded = true;
            items = new List<DataItem>();
            foreach (string d in dirs) items.AddRange(ServerData.ReadFolder(d, exts, extraKey));
            FillList();
        }

        void OpenFile(string path)
        {
            try { doc = TextDoc.Load(path); }
            catch (IOException ex) { Ui.Error(this, ex.Message); doc = null; }
            rawChanged = false;
            pathLabel.Text = path;
            usedLabel.Text = usedBy != null && Current != null ? usedBy(Current.Name ?? Current.FileName) : "";
            FillFromDoc();
        }

        void ShopChanged()
        {
            shopDriven = true;
            FillFromDoc();
            shopDriven = false;
        }

        /// <summary>Another window saved this file: show it, unless there are edits here to keep.</summary>
        public void FileSavedElsewhere(string path)
        {
            if (doc != null && !HasUnsaved && string.Equals(doc.Path, path, StringComparison.OrdinalIgnoreCase)) OpenFile(path);
        }

        /// <summary>Read the list again so it shows changed names.</summary>
        public void ReloadNames()
        {
            if (!loaded || HasUnsaved) return;
            Task.Run(() =>
            {
                var all = new List<DataItem>();
                foreach (string d in dirs) all.AddRange(ServerData.ReadFolder(d, exts, extraKey));
                BeginInvoke((Action)(() =>
                {
                    items = all;
                    FillList();
                }));
            });
            shop?.ResetCatalog();
        }

        void FillFromDoc()
        {
            syncing = true;
            grid.Rows.Clear();
            addKey.Items.Clear();
            if (doc != null)
            {
                raw.SetText(doc.GetAllText());
                rawChanged = false;
                List<DocEntry> entries = doc.Entries();
                var ordered = entries.Select((e, i) => new { e, i }).OrderBy(x => schema.OrderOf(x.e.Key)).ThenBy(x => x.i).Select(x => x.e);
                foreach (DocEntry e in ordered)
                {
                    SchemaField f = schema.Lookup(e.Key);
                    int r = grid.Rows.Add(Loc.T(f?.Group ?? "Other"), f != null ? Loc.T(f.Label) : e.Key, e.Key, Shown(e.Value), f != null ? Loc.T(f.Help) : "");
                    grid.Rows[r].Tag = e.Line;
                }
                var present = new HashSet<string>(entries.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
                foreach (SchemaField f in schema.Fields)
                    if (!present.Contains(f.Key)) addKey.Items.Add(new KeyChoice(f));
                // repeatable ones can always be added again
                foreach (SchemaField f in schema.Fields.Where(x => x.Help.Contains("Can repeat") && present.Contains(x.Key)))
                    addKey.Items.Add(new KeyChoice(f));
                if (addKey.Items.Count > 0) addKey.SelectedIndex = 0;
            }
            syncing = false;
            if (shop != null && !shopDriven) shop.Bind(doc);
            UpdateButtons();
        }

        static string Shown(string value) => value.Replace("\t", "  ");

        sealed class KeyChoice
        {
            public readonly SchemaField F;
            public KeyChoice(SchemaField f) { F = f; }
            public override string ToString() => Loc.T(F.Label) + "  (" + F.Key + ")";
        }

        /// <summary>The language changed: show the field names and help again in it.</summary>
        public void Relabel()
        {
            if (doc == null) return;
            SyncViews();
            FillFromDoc();
        }

        void GridEdited(object s, DataGridViewCellEventArgs e)
        {
            if (syncing || doc == null || grid.Columns[e.ColumnIndex].Name != "value") return;
            int line = (int)grid.Rows[e.RowIndex].Tag;
            string typed = grid.Rows[e.RowIndex].Cells["value"].Value as string ?? "";
            // clicking through a cell without changing it must leave the file's tabs alone
            DocEntry was = doc.Entries().FirstOrDefault(x => x.Line == line);
            if (was != null && Shown(was.Value) == typed.Trim()) return;
            doc.SetValue(line, typed);
            syncing = true;
            raw.SetText(doc.GetAllText());
            rawChanged = false;
            syncing = false;
            UpdateButtons();
        }

        /// <summary>Carry raw-text edits over before anything reads the document.</summary>
        bool SyncViews()
        {
            if (rawChanged && doc != null)
            {
                doc.SetAllText(raw.EditorText);
                rawChanged = false;
                FillFromDoc();
            }
            grid.EndEdit();
            return true;
        }

        void AddField()
        {
            if (doc == null || !(addKey.SelectedItem is KeyChoice c)) return;
            SyncViews();
            doc.AddEntry(c.F.Key, "");
            FillFromDoc();
            foreach (DataGridViewRow r in grid.Rows)
                if ((r.Cells["key"].Value as string) == c.F.Key)
                {
                    grid.CurrentCell = r.Cells["value"];
                    grid.BeginEdit(true);
                }
        }

        void RemoveRow()
        {
            if (doc == null || grid.CurrentRow == null) return;
            SyncViews();
            doc.RemoveLine((int)grid.CurrentRow.Tag);
            FillFromDoc();
        }

        void OpenLinked()
        {
            if (doc == null) return;
            SyncViews();
            string link = Tokens.Unquote(doc.First("*연결파일"));
            if (link.Length == 0) { Ui.Error(this, Loc.T("This file doesn't link to a name file.")); return; }
            string folder = Path.GetDirectoryName(doc.Path);
            string full = Path.Combine(folder, link);
            if (!File.Exists(full)) { Ui.Error(this, Loc.F("The linked file isn't there:\n{0}", full)); return; }
            host.OpenInTextTab(full);
        }

        void NewCopy()
        {
            if (doc == null) { Ui.Error(this, Loc.T("Pick a file to copy first.")); return; }
            SyncViews();
            string baseName = Path.GetFileNameWithoutExtension(doc.Path);
            string ext = Path.GetExtension(doc.Path);
            using (var d = new SaveFileDialog { InitialDirectory = Path.GetDirectoryName(doc.Path), FileName = baseName + "_copy" + ext, Filter = Loc.F("This type|*{0}", ext), Title = Loc.F("New copy of {0}", Path.GetFileName(doc.Path)) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    SafeWrite.Save(d.FileName, doc.ToBytes());
                    AddToListAndOpen(d.FileName);
                }
                catch (IOException ex) { Ui.Error(this, ex.Message); }
            }
        }

        void AddToListAndOpen(string path)
        {
            TextDoc nd = TextDoc.Load(path);
            var item = new DataItem
            {
                Path = path,
                FileName = Path.GetFileName(path),
                Name = Tokens.Unquote(nd.First("*이름")),
                LocalName = LocalName.For(path, nd),
                Extra = extraKey != null ? Tokens.Unquote(nd.First(extraKey)) : ""
            };
            items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            items.Add(item);
            items.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
            filter.Text = "";
            currentItem = null;
            doc = null;
            FillList();
            SelectByFile(item.FileName);
        }

        void DeleteFile()
        {
            if (doc == null) return;
            if (MessageBox.Show(this, Loc.F("Delete {0}?\n\nA copy goes in {1}.", Path.GetFileName(doc.Path), SafeWrite.BackupFolderName), "PriTaTor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                SafeWrite.Delete(doc.Path);
                string path = doc.Path;
                items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
                doc = null;
                currentItem = null;
                FillList();
                FillFromDoc();
                pathLabel.Text = "";
                host.SetStatus(Loc.F("Deleted {0}. Reload the server for this to take effect.", Path.GetFileName(path)));
            }
            catch (IOException ex) { Ui.Error(this, ex.Message); }
        }

        void Revert()
        {
            if (doc == null) return;
            OpenFile(doc.Path);
        }

        void UpdateButtons()
        {
            if (saveBtn != null) saveBtn.Enabled = HasUnsaved;
            host?.SetUnsavedMark();
        }

        public bool HasUnsaved => doc != null && (doc.Dirty || rawChanged);

        public void SaveCurrent()
        {
            if (doc == null) return;
            SyncViews();
            if (!doc.Dirty) return;
            try
            {
                if (doc.ChangedOnDisk() && MessageBox.Show(this, Loc.F("{0} changed on disk since you opened it. Overwrite?", Path.GetFileName(doc.Path)), "PriTaTor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                bool wrote = SafeWrite.Save(doc.Path, doc.ToBytes());
                doc.MarkSaved();
                bool namesChanged = false;
                DataItem cur = Current;
                if (cur != null)
                {
                    cur.Name = Tokens.Unquote(doc.First("*이름"));
                    string oldLocal = cur.LocalName;
                    cur.LocalName = LocalName.For(doc.Path, doc);
                    namesChanged = schema == Schema.Zhoon || oldLocal != cur.LocalName;
                    cur.Extra = extraKey != null ? Tokens.Unquote(doc.First(extraKey)) : "";
                    currentItem.SubItems[1].Text = cur.LocalName ?? "";
                    currentItem.SubItems[2].Text = cur.Name ?? "";
                    if (extraTitle != null) currentItem.SubItems[3].Text = cur.Extra ?? "";
                }
                host.SetStatus(wrote ? Loc.F("Saved {0}. Reload the server for this to take effect.", Path.GetFileName(doc.Path)) : Loc.T("Nothing needed saving."));
                UpdateButtons();
                if (namesChanged) host.NamesChanged(this);
            }
            catch (IOException ex) { Ui.Error(this, Loc.T("Saving failed:") + "\n" + ex.Message); }
            catch (UnauthorizedAccessException ex) { Ui.Error(this, Loc.T("Saving failed:") + "\n" + ex.Message); }
        }

        public bool ConfirmLeave()
        {
            grid.EndEdit();
            if (!HasUnsaved) return true;
            DialogResult r = Ui.AskSave(this, Path.GetFileName(doc.Path));
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) { SaveCurrent(); return !HasUnsaved; }
            rawChanged = false;
            doc.Dirty = false;
            return true;
        }

        // for automated checks
        public DataGridView Grid => grid;
        public ShopEditor Shop => shop;
        public void TestShowShop() => views.SelectedTab = shopPage;
        public TextDoc Doc => doc;
    }
}
