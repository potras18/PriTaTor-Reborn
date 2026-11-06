// The Maps tab: map list on the left, the map in the middle, flags / NPCs / monsters / map details on the right.

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
    public sealed class MapEditorPanel : UserControl, IEditorPanel
    {
        readonly MainForm host;
        Catalog cat;
        ServerData data;
        public MapSession Session { get; private set; }
        public readonly MapCanvas Canvas = new MapCanvas { Dock = DockStyle.Fill };

        readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowLines = false };
        readonly TextBox treeFilter = new TextBox { Dock = DockStyle.Top };
        readonly ToolStrip bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        readonly Label statusLine = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0), BackColor = SystemColors.ControlLight };
        ToolStripButton btnSave, btnUndo, btnRedo, btnSelect, btnAddFlag, btnAddNpc, btnAlign;
        readonly TabControl side = new TabControl { Dock = DockStyle.Fill };

        // flags tab
        readonly ListView flagList = MakeList(("#", 44), ("X", 74), ("Z", 74), ("On", 36));
        readonly TextBox flagX = new TextBox { Width = 80 }, flagZ = new TextBox { Width = 80 };
        readonly CheckBox flagOn = new CheckBox { Text = "Active", AutoSize = true };
        readonly Label flagInfo = Ui.Label("");

        // NPC tab
        readonly ListView npcList = MakeList(("#", 34), ("Name", 150), ("File", 110), ("On", 30));
        readonly TextBox npcName = new TextBox { Width = 230 }, npcModel = new TextBox { Width = 230 };
        readonly Label npcShown = Ui.Label("");
        readonly ComboBox npcFile = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        readonly TextBox npcX = new TextBox { Width = 80 }, npcY = new TextBox { Width = 80 }, npcZ = new TextBox { Width = 80 };
        readonly NumericUpDown npcAngle = new NumericUpDown { Minimum = -1, Maximum = 360, DecimalPlaces = 1, Width = 70, Increment = 5 };
        readonly Label npcRaw = Ui.Label("");
        readonly CheckBox npcActive = new CheckBox { Text = "Active", AutoSize = true };
        readonly Panel npcEditor = new Panel { Dock = DockStyle.Bottom, Height = 250 };

        // monsters tab
        readonly TextBox spmMax = new TextBox { Width = 60 }, spmMin = new TextBox { Width = 50 }, spmMaxI = new TextBox { Width = 50 }, spmCount = new TextBox { Width = 50 };
        readonly DataGridView actorGrid = MakeGrid(), bossGrid = MakeGrid();
        readonly Label spmTotal = Ui.Label("");
        readonly Panel spmBody = new Panel { Dock = DockStyle.Fill };
        readonly Button spmCreate;

        // map tab
        readonly Label mapInfo = new Label { AutoSize = true, MaximumSize = new Size(340, 0), Padding = new Padding(4), Font = Ui.BaseFont };
        readonly ListBox twinList = new ListBox { Height = 100, Dock = DockStyle.Top, IntegralHeight = false };
        readonly Label warn = new Label { AutoSize = true, MaximumSize = new Size(340, 0), Padding = new Padding(4), ForeColor = Color.DarkRed, Font = Ui.BoldFont };

        bool syncing;
        CheckBox minimapCheck;
        bool settingMinimap, minimapChosen;
        int loadToken;
        double lastWX, lastWZ;
        public event Action MapLoaded;

        static ListView MakeList(params (string, int)[] cols)
        {
            var l = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, GridLines = true };
            foreach (var c in cols) l.Columns.Add(c.Item1, c.Item2);
            return l;
        }

        static DataGridView MakeGrid() => new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToResizeRows = false,
            RowHeadersWidth = 24,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None
        };

        public MapEditorPanel(MainForm host)
        {
            this.host = host;
            spmCreate = Ui.Button("Create a monster list for this map", (s, e) => CreateSpm());
            BuildToolbar();
            BuildSide();

            var center = new Panel { Dock = DockStyle.Fill };
            center.Controls.Add(Canvas);
            center.Controls.Add(statusLine);
            center.Controls.Add(bar);

            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(tree);
            left.Controls.Add(treeFilter);
            var hint = new Label { Text = "Filter maps", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2) };
            left.Controls.Add(hint);

            var splitRight = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            splitRight.Panel1.Controls.Add(center);
            splitRight.Panel2.Controls.Add(side);
            var splitLeft = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            splitLeft.Panel1.Controls.Add(left);
            splitLeft.Panel2.Controls.Add(splitRight);
            Controls.Add(splitLeft);
            Ui.SplitWhenSized(splitLeft, w => 250);
            Ui.SplitWhenSized(splitRight, w => w - 370);

            treeFilter.TextChanged += (s, e) => FillTree();
            tree.BeforeSelect += (s, e) =>
            {
                if (syncing) return;
                if (e.Node.Tag is MapEntry m && m != Session?.Entry && !ConfirmLeave()) e.Cancel = true;
            };
            tree.AfterSelect += (s, e) => { if (!syncing && e.Node.Tag is MapEntry m) OpenMap(m); };

            Canvas.SelectionChangedByUser += SyncListsFromCanvas;
            Canvas.EditCommitted += () => { RefreshLists(); UpdateButtons(); };
            Canvas.CursorWorld += (x, z) => { lastWX = x; lastWZ = z; UpdateStatus(); };
            Canvas.RequestAddFlag += PlaceFlag;
            Canvas.RequestAddNpc += PlaceNpc;
            Canvas.RequestMenu += ShowCanvasMenu;
            Canvas.MinimapMoved += () => { TrySaveSettings(); FillMapTab(); };
            Canvas.KeyDown += CanvasKey;
            Canvas.NpcLabel = NpcDisplayName;
            UpdateButtons();
        }

        // ----- build -----

        ToolStripButton AddBar(string text, string tip, Action a)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += (s, e) => a();
            bar.Items.Add(b);
            return b;
        }

        CheckBox AddLayerCheck(string text, bool on, Action<bool> set)
        {
            var c = new CheckBox { Text = text, Checked = on, AutoSize = true, BackColor = Color.Transparent };
            c.CheckedChanged += (s, e) => { set(c.Checked); Canvas.InvalidateLayers(); };
            bar.Items.Add(new ToolStripControlHost(c));
            return c;
        }

        void BuildToolbar()
        {
            btnSave = AddBar("Save", "Save this map's files (Ctrl+S)", SaveCurrent);
            btnUndo = AddBar("Undo", "Undo (Ctrl+Z)", DoUndo);
            btnRedo = AddBar("Redo", "Redo (Ctrl+Y)", DoRedo);
            bar.Items.Add(new ToolStripSeparator());
            btnSelect = AddBar("Select", "Click and drag flags and NPCs. Drag empty ground to box-select.", () => SetTool(CanvasTool.Select));
            btnAddFlag = AddBar("Add flag", "Click on the map to place monster flags", () => SetTool(CanvasTool.AddFlag));
            btnAddNpc = AddBar("Add NPC", "Click on the map to place an NPC", () => SetTool(CanvasTool.AddNpc));
            AddBar("Delete", "Delete what's selected (Del)", DeleteSelected);
            bar.Items.Add(new ToolStripSeparator());
            AddBar("Fit", "Show the whole map (F)", () => Canvas.FitAll());
            AddLayerCheck("Terrain", true, v => Canvas.ShowTerrain = v);
            minimapCheck = AddLayerCheck("Minimap", true, v => { Canvas.ShowMinimap = v; if (!settingMinimap) minimapChosen = true; });
            var op = new TrackBar { Minimum = 10, Maximum = 100, Value = 85, TickStyle = TickStyle.None, AutoSize = false, Width = 80, Height = 22 };
            op.ValueChanged += (s, e) => { Canvas.MinimapOpacity = op.Value / 100f; Canvas.InvalidateLayers(); };
            bar.Items.Add(new ToolStripControlHost(op) { ToolTipText = "Minimap opacity" });
            AddLayerCheck("Flags", true, v => Canvas.ShowFlags = v);
            AddLayerCheck("NPCs", true, v => Canvas.ShowNpcs = v);
            AddLayerCheck("Names", false, v => Canvas.ShowNames = v);
            bar.Items.Add(new ToolStripSeparator());
            btnAlign = AddBar("Align minimap", "Drag the minimap by hand. Flags and NPCs don't move.", () => SetTool(Canvas.Tool == CanvasTool.AlignMinimap ? CanvasTool.Select : CanvasTool.AlignMinimap));
        }

        void BuildSide()
        {
            // --- flags ---
            var tf = new TabPage("Flags");
            var fTop = Ui.Table(4);
            fTop.Controls.Add(Ui.Label("X"));
            fTop.Controls.Add(flagX);
            fTop.Controls.Add(Ui.Label("Z"));
            fTop.Controls.Add(flagZ);
            fTop.Controls.Add(flagOn);
            fTop.SetColumnSpan(flagOn, 2);
            var fBtns = Ui.Row();
            fBtns.Controls.Add(Ui.Button("Add", (s, e) => SetTool(CanvasTool.AddFlag)));
            fBtns.Controls.Add(Ui.Button("Duplicate", (s, e) => DuplicateSelected()));
            fBtns.Controls.Add(Ui.Button("Delete", (s, e) => DeleteSelected()));
            fBtns.Controls.Add(flagInfo);
            var fBottom = new Panel { Dock = DockStyle.Bottom, Height = 104 };
            fBottom.Controls.Add(fBtns);
            fBottom.Controls.Add(fTop);
            tf.Controls.Add(flagList);
            tf.Controls.Add(fBottom);
            flagList.SelectedIndexChanged += (s, e) => { if (!syncing) CanvasFromFlagList(); };
            flagList.DoubleClick += (s, e) => CenterOnSelection();
            flagX.Leave += (s, e) => ApplyFlagEdit();
            flagZ.Leave += (s, e) => ApplyFlagEdit();
            flagX.KeyDown += EnterApplies;
            flagZ.KeyDown += EnterApplies;
            flagOn.CheckedChanged += (s, e) => { if (!syncing) ApplyFlagEdit(); };

            // --- NPCs ---
            var tn = new TabPage("NPCs");
            var nT = Ui.Table(2);
            nT.Controls.Add(Ui.Label("Shown as"));
            nT.Controls.Add(npcShown);
            nT.Controls.Add(Ui.Label("Name"));
            nT.Controls.Add(npcName);
            nT.Controls.Add(Ui.Label("NPC file"));
            nT.Controls.Add(npcFile);
            nT.Controls.Add(Ui.Label("Model"));
            nT.Controls.Add(npcModel);
            var pos = Ui.Row();
            pos.Controls.Add(Ui.Label("X")); pos.Controls.Add(npcX);
            pos.Controls.Add(Ui.Label("Z")); pos.Controls.Add(npcZ);
            nT.Controls.Add(Ui.Label("Position"));
            nT.Controls.Add(pos);
            var h = Ui.Row();
            h.Controls.Add(npcY);
            h.Controls.Add(Ui.Button("Snap to ground", (s, e) => SnapNpc()));
            nT.Controls.Add(Ui.Label("Height"));
            nT.Controls.Add(h);
            var ang = Ui.Row();
            ang.Controls.Add(npcAngle);
            ang.Controls.Add(Ui.Label("° (0 = up the map)"));
            ang.Controls.Add(npcRaw);
            nT.Controls.Add(Ui.Label("Facing"));
            nT.Controls.Add(ang);
            nT.Controls.Add(new Label());
            nT.Controls.Add(npcActive);
            var nBtns = Ui.Row();
            nBtns.Controls.Add(Ui.Button("Add", (s, e) => SetTool(CanvasTool.AddNpc)));
            nBtns.Controls.Add(Ui.Button("Duplicate", (s, e) => DuplicateSelected()));
            nBtns.Controls.Add(Ui.Button("Delete", (s, e) => DeleteSelected()));
            nBtns.Controls.Add(Ui.Button("Open .npc", (s, e) => OpenNpcFile()));
            nBtns.Controls.Add(Ui.Button("Edit shop…", (s, e) => OpenShop()));
            npcEditor.Height = 330;
            npcEditor.AutoScroll = true;
            nBtns.Dock = DockStyle.Top;
            nT.Dock = DockStyle.Top;
            npcEditor.Controls.Add(nBtns);
            npcEditor.Controls.Add(nT);
            tn.Controls.Add(npcList);
            tn.Controls.Add(npcEditor);
            npcList.SelectedIndexChanged += (s, e) => { if (!syncing) CanvasFromNpcList(); };
            npcList.DoubleClick += (s, e) => CenterOnSelection();
            foreach (Control c in new Control[] { npcName, npcModel, npcFile, npcX, npcY, npcZ })
            {
                c.Leave += (s, e) => ApplyNpcEdit();
                c.KeyDown += EnterApplies;
            }
            npcFile.SelectedIndexChanged += (s, e) => { if (!syncing) ApplyNpcFileChoice(); };
            npcAngle.ValueChanged += (s, e) => { if (!syncing) ApplyNpcEdit(); };
            npcActive.CheckedChanged += (s, e) => { if (!syncing) ApplyNpcEdit(); };

            // --- monsters ---
            var tm = new TabPage("Monsters");
            var top = Ui.Table(4);
            top.Controls.Add(Ui.Label("Max alive at once")); top.Controls.Add(spmMax);
            top.Controls.Add(Ui.Label("Spawned each time")); top.Controls.Add(spmCount);
            top.Controls.Add(Ui.Label("Interval min")); top.Controls.Add(spmMin);
            top.Controls.Add(Ui.Label("Interval max")); top.Controls.Add(spmMaxI);
            foreach (TextBox t in new[] { spmMax, spmCount, spmMin, spmMaxI })
            {
                t.Leave += (s, e) => ApplySpmNumbers();
                t.KeyDown += EnterApplies;
            }
            actorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "m", HeaderText = "Monster", FillWeight = 55 });
            actorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "e", HeaderText = "Shown as", ReadOnly = true, FillWeight = 45, DefaultCellStyle = { ForeColor = Color.Gray } });
            actorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "p", HeaderText = "Rate", FillWeight = 18 });
            bossGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "b", HeaderText = "Boss", FillWeight = 40 });
            bossGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "g", HeaderText = "Guards", FillWeight = 40 });
            bossGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "c", HeaderText = "No.", FillWeight = 14 });
            bossGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "h", HeaderText = "Hours", FillWeight = 40 });
            actorGrid.CellEndEdit += (s, e) => ApplyActors();
            actorGrid.UserDeletedRow += (s, e) => ApplyActors();
            actorGrid.EditingControlShowing += GridAutoComplete;
            bossGrid.CellEndEdit += (s, e) => ApplyBosses();
            bossGrid.UserDeletedRow += (s, e) => ApplyBosses();
            bossGrid.EditingControlShowing += GridAutoComplete;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(actorGrid);
            var actorHead = new Label { Text = "Monsters", Dock = DockStyle.Top, Font = Ui.BoldFont, Height = 20 };
            split.Panel1.Controls.Add(actorHead);
            split.Panel1.Controls.Add(spmTotal);
            spmTotal.Dock = DockStyle.Bottom;
            split.Panel2.Controls.Add(bossGrid);
            split.Panel2.Controls.Add(new Label { Text = "Bosses (hours when they appear)", Dock = DockStyle.Top, Font = Ui.BoldFont, Height = 20 });
            spmBody.Controls.Add(split);
            spmBody.Controls.Add(top);
            spmCreate.Dock = DockStyle.Top;
            tm.Controls.Add(spmBody);
            tm.Controls.Add(spmCreate);

            // --- map ---
            var tp = new TabPage("Map");
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            flow.Controls.Add(warn);
            flow.Controls.Add(mapInfo);
            flow.Controls.Add(Ui.Label("Same map elsewhere (moved copies)", true));
            twinList.Width = 330;
            twinList.Dock = DockStyle.None;
            flow.Controls.Add(twinList);
            flow.Controls.Add(Ui.Button("Import flags, NPCs and monsters…", (s, e) => ImportDialogShow()));
            flow.Controls.Add(Ui.Button("Shift everything on this map…", (s, e) => ShiftDialogShow()));
            flow.Controls.Add(Ui.Button("Choose a different minimap…", (s, e) => ChooseMinimap()));
            flow.Controls.Add(Ui.Button("Reset minimap position", (s, e) => ResetMinimap()));
            scroll.Controls.Add(flow);
            tp.Controls.Add(scroll);

            side.TabPages.Add(tf);
            side.TabPages.Add(tn);
            side.TabPages.Add(tm);
            side.TabPages.Add(tp);
        }

        void EnterApplies(object s, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (s == flagX || s == flagZ) ApplyFlagEdit();
            else if (s == spmMax || s == spmMin || s == spmMaxI || s == spmCount) ApplySpmNumbers();
            else ApplyNpcEdit();
        }

        void GridAutoComplete(object s, DataGridViewEditingControlShowingEventArgs e)
        {
            if (!(e.Control is TextBox t) || data == null) return;
            var grid = (DataGridView)s;
            string col = grid.Columns[grid.CurrentCell.ColumnIndex].Name;
            if (col == "m" || col == "b" || col == "g")
            {
                t.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                t.AutoCompleteSource = AutoCompleteSource.CustomSource;
                var src = new AutoCompleteStringCollection();
                src.AddRange(data.Monsters.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToArray());
                t.AutoCompleteCustomSource = src;
            }
            else t.AutoCompleteMode = AutoCompleteMode.None;
        }

        // ----- data -----

        public void Bind(Catalog c, ServerData d)
        {
            cat = c;
            data = d;
            syncing = true;
            npcFile.Items.Clear();
            foreach (DataItem i in d.Npcs) npcFile.Items.Add(d.RelativeNpcPath(i.Path));
            syncing = false;
            FillTree();
        }

        public void RefreshCatalog(Catalog c)
        {
            string open = Session?.Entry.Stem;
            cat = c;
            Session = null;
            FillTree();
            if (open != null && cat.Find(open) != null) OpenMap(cat.Find(open));
        }

        static string NodeText(MapEntry m)
        {
            string t = m.Stem;
            var parts = new List<string>();
            if (m.FlagCount + m.FlagsOff > 0) parts.Add((m.FlagCount + m.FlagsOff) + "f");
            if (m.NpcCount + m.NpcsOff > 0) parts.Add((m.NpcCount + m.NpcsOff) + "n");
            if (parts.Count > 0) t += "  [" + string.Join(" ", parts) + "]";
            return t;
        }

        void FillTree()
        {
            if (cat == null) return;
            syncing = true;
            string q = treeFilter.Text.Trim();
            tree.BeginUpdate();
            tree.Nodes.Clear();
            var groups = new SortedDictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            foreach (MapEntry m in cat.Maps)
            {
                if (q.Length > 0 && (m.Stem + " " + m.RelPath).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string g = m.HasGeometry ? (m.BaseFolder.Length > 0 ? m.BaseFolder : "(top)") : "(no map file)";
                if (!groups.TryGetValue(g, out TreeNode parent)) groups[g] = parent = new TreeNode(g);
                var n = new TreeNode(NodeText(m)) { Tag = m };
                if (!m.HasGeometry) n.ForeColor = Color.Gray;
                if (m.OutsideCount > 0 && m.OutsideCount * 2 > m.FlagCount + m.FlagsOff + m.NpcCount + m.NpcsOff) n.ForeColor = Color.OrangeRed;
                else if (m.InServerExe == false) n.ForeColor = Color.SteelBlue;
                parent.Nodes.Add(n);
                if (m == Session?.Entry) tree.SelectedNode = n;
            }
            foreach (TreeNode p in groups.Values) tree.Nodes.Add(p);
            tree.ExpandAll();
            tree.EndUpdate();
            tree.SelectedNode?.EnsureVisible();
            syncing = false;
        }

        void UpdateTreeText()
        {
            foreach (TreeNode p in tree.Nodes)
                foreach (TreeNode n in p.Nodes)
                    if (n.Tag == Session?.Entry) n.Text = NodeText((MapEntry)n.Tag);
        }

        public void SelectMap(string stem)
        {
            MapEntry m = cat?.Find(stem);
            if (m != null) OpenMap(m);
        }

        public void OpenMap(MapEntry m)
        {
            int token = ++loadToken;
            Cursor = Cursors.WaitCursor;
            Canvas.Notice = "Opening " + m.Stem + "…";
            Canvas.Invalidate();
            Canvas.Update();
            Catalog c = cat;
            Task.Run(() =>
            {
                MapSession s = null;
                Bitmap terrain = null, minimap = null;
                Exception err = null;
                bool savedPicture = false;
                try
                {
                    s = new MapSession(c, m);
                    if (s.Mesh != null) terrain = TerrainCache.Load(c, m, s.Mesh, out savedPicture);
                    if (m.MinimapPath != null)
                    {
                        try { minimap = GameImage.Load(m.MinimapPath); }
                        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException) { }
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { err = ex; }
                BeginInvoke((Action)(() =>
                {
                    if (savedPicture) TrySaveSettings();
                    if (token != loadToken) { terrain?.Dispose(); minimap?.Dispose(); return; }
                    Cursor = Cursors.Default;
                    Canvas.Notice = null;
                    Canvas.Invalidate();
                    if (err != null) { Ui.Error(this, "Couldn't open " + m.Stem + ":\n" + err.Message); return; }
                    Canvas.Terrain?.Dispose();
                    Canvas.Minimap?.Dispose();
                    Canvas.Terrain = terrain;
                    Canvas.Minimap = minimap;
                    // a textured picture would sit under the minimap, so it starts off, unless you've chosen otherwise
                    if (!minimapChosen)
                    {
                        settingMinimap = true;
                        minimapCheck.Checked = !m.TerrainTextured;
                        settingMinimap = false;
                    }
                    Session = s;
                    Canvas.Session = s;
                    Canvas.ClearSelection();
                    SetTool(CanvasTool.Select);
                    Canvas.FitAll();
                    RefreshLists();
                    FillSpm();
                    FillMapTab();
                    UpdateButtons();
                    UpdateStatus();
                    syncing = true;
                    foreach (TreeNode p in tree.Nodes)
                        foreach (TreeNode n in p.Nodes)
                            if (n.Tag == m) { tree.SelectedNode = n; n.EnsureVisible(); }
                    syncing = false;
                    MapLoaded?.Invoke();
                }));
            });
        }

        string NpcDisplayName(NpcRecord n)
        {
            string file = Path.GetFileName(n.NpcFile ?? "");
            DataItem d = data?.Npcs.FirstOrDefault(i => string.Equals(i.FileName, file, StringComparison.OrdinalIgnoreCase));
            string local = d?.LocalName;
            return !string.IsNullOrWhiteSpace(local) ? local : n.Name;
        }

        // ----- tool + buttons -----

        public void SetTool(CanvasTool t)
        {
            Canvas.Tool = t;
            btnSelect.Checked = t == CanvasTool.Select;
            btnAddFlag.Checked = t == CanvasTool.AddFlag;
            btnAddNpc.Checked = t == CanvasTool.AddNpc;
            btnAlign.Checked = t == CanvasTool.AlignMinimap;
            Canvas.Cursor = t == CanvasTool.Select ? Cursors.Default : t == CanvasTool.AlignMinimap ? Cursors.SizeAll : Cursors.Cross;
            Canvas.Focus();
            Canvas.Invalidate();
        }

        void UpdateButtons()
        {
            bool on = Session != null;
            btnSave.Enabled = on && Session.IsDirty;
            btnUndo.Enabled = on && Session.CanUndo;
            btnRedo.Enabled = on && Session.CanRedo;
            host?.SetUnsavedMark();
        }

        void UpdateStatus()
        {
            if (Session == null) { statusLine.Text = ""; return; }
            double? g = Session.Mesh?.GroundAt(lastWX, lastWZ);
            statusLine.Text = string.Format("X {0:0}   Z {1:0}{2}      {3} flags, {4} NPCs selected: {5}",
                lastWX, lastWZ, g.HasValue ? string.Format("   height {0:0}", g.Value) : "", Session.Flags.Count, Session.Npcs.Count, Canvas.SelFlags.Count + Canvas.SelNpcs.Count);
        }

        // ----- lists -----

        void RefreshLists()
        {
            syncing = true;
            flagList.BeginUpdate();
            flagList.Items.Clear();
            if (Session != null)
                foreach (SpawnFlag f in Session.Flags)
                {
                    var it = new ListViewItem(new[] { (f.Slot + 1).ToString(), f.X.ToString(), f.Z.ToString(), f.State != 0 ? "✓" : "" }) { Tag = f };
                    if (Session.Entry.HasGeometry && Catalog.Outside(Session.Entry, f.X, f.Z)) it.ForeColor = Color.OrangeRed;
                    else if (f.State == 0) it.ForeColor = Color.Gray;
                    it.Selected = Canvas.SelFlags.Contains(f.Slot);
                    flagList.Items.Add(it);
                }
            flagList.EndUpdate();
            npcList.BeginUpdate();
            npcList.Items.Clear();
            if (Session != null)
                foreach (NpcRecord n in Session.Npcs)
                {
                    var it = new ListViewItem(new[] { (n.Slot + 1).ToString(), NpcDisplayName(n), Path.GetFileName(n.NpcFile), n.Active ? "✓" : "" }) { Tag = n };
                    if (!n.Active) it.ForeColor = Color.Gray;
                    it.Selected = Canvas.SelNpcs.Contains(n.Slot);
                    npcList.Items.Add(it);
                }
            npcList.EndUpdate();
            flagInfo.Text = Session != null ? Session.Flags.Count + " of " + Session.Spp.Capacity + " slots" : "";
            syncing = false;
            FillFlagEditor();
            FillNpcEditor();
            UpdateStatus();
            Canvas.Invalidate();
        }

        void SyncListsFromCanvas()
        {
            syncing = true;
            foreach (ListViewItem it in flagList.Items) it.Selected = Canvas.SelFlags.Contains(((SpawnFlag)it.Tag).Slot);
            foreach (ListViewItem it in npcList.Items) it.Selected = Canvas.SelNpcs.Contains(((NpcRecord)it.Tag).Slot);
            if (flagList.SelectedItems.Count > 0) flagList.SelectedItems[0].EnsureVisible();
            if (npcList.SelectedItems.Count > 0) npcList.SelectedItems[0].EnsureVisible();
            // show the tab of what was picked
            if (Canvas.SelNpcs.Count > 0 && Canvas.SelFlags.Count == 0 && side.SelectedIndex == 0) side.SelectedIndex = 1;
            if (Canvas.SelFlags.Count > 0 && Canvas.SelNpcs.Count == 0 && side.SelectedIndex == 1) side.SelectedIndex = 0;
            syncing = false;
            FillFlagEditor();
            FillNpcEditor();
            UpdateStatus();
        }

        void CanvasFromFlagList()
        {
            Canvas.SelFlags.Clear();
            foreach (ListViewItem it in flagList.SelectedItems) Canvas.SelFlags.Add(((SpawnFlag)it.Tag).Slot);
            Canvas.SelNpcs.Clear();
            syncing = true;
            foreach (ListViewItem it in npcList.SelectedItems) it.Selected = false;
            syncing = false;
            FillFlagEditor();
            FillNpcEditor();
            Canvas.Invalidate();
            UpdateStatus();
        }

        void CanvasFromNpcList()
        {
            Canvas.SelNpcs.Clear();
            foreach (ListViewItem it in npcList.SelectedItems) Canvas.SelNpcs.Add(((NpcRecord)it.Tag).Slot);
            Canvas.SelFlags.Clear();
            syncing = true;
            foreach (ListViewItem it in flagList.SelectedItems) it.Selected = false;
            syncing = false;
            FillFlagEditor();
            FillNpcEditor();
            Canvas.Invalidate();
            UpdateStatus();
        }

        SpawnFlag OneFlag() => Canvas.SelFlags.Count == 1 ? Session?.Flags.FirstOrDefault(f => f.Slot == Canvas.SelFlags.First()) : null;
        NpcRecord OneNpc() => Canvas.SelNpcs.Count == 1 ? Session?.Npcs.FirstOrDefault(n => n.Slot == Canvas.SelNpcs.First()) : null;

        void FillFlagEditor()
        {
            SpawnFlag f = OneFlag();
            syncing = true;
            flagX.Enabled = flagZ.Enabled = flagOn.Enabled = f != null;
            flagX.Text = f?.X.ToString() ?? "";
            flagZ.Text = f?.Z.ToString() ?? "";
            flagOn.Checked = f != null && f.State != 0;
            syncing = false;
        }

        void ApplyFlagEdit()
        {
            SpawnFlag f = OneFlag();
            if (f == null || syncing) return;
            if (!int.TryParse(flagX.Text, out int x) || !int.TryParse(flagZ.Text, out int z)) { FillFlagEditor(); return; }
            int st = flagOn.Checked ? (f.State != 0 ? f.State : 1) : 0;
            if (x == f.X && z == f.Z && st == f.State) return;
            Session.Checkpoint();
            f.X = x; f.Z = z; f.State = st;
            Session.UpdateDirty();
            RefreshLists();
            UpdateButtons();
        }

        void FillNpcEditor()
        {
            NpcRecord n = OneNpc();
            syncing = true;
            foreach (Control c in new Control[] { npcName, npcModel, npcFile, npcX, npcY, npcZ, npcAngle, npcActive }) c.Enabled = n != null;
            npcShown.Text = n != null ? NpcDisplayName(n) : "";
            npcName.Text = n?.Name ?? "";
            npcModel.Text = n?.Model ?? "";
            npcFile.Text = n?.NpcFile ?? "";
            npcX.Text = n != null ? n.X.ToString("0.##") : "";
            npcY.Text = n != null ? n.Y.ToString("0.##") : "";
            npcZ.Text = n != null ? n.Z.ToString("0.##") : "";
            npcAngle.Value = n != null ? (decimal)Math.Round(n.Degrees, 1) : 0;
            npcRaw.Text = n != null ? "raw " + n.AngleRaw : "";
            npcActive.Checked = n != null && n.Active;
            syncing = false;
        }

        void ApplyNpcEdit()
        {
            NpcRecord n = OneNpc();
            if (n == null || syncing) return;
            NpcRecord before = n.Clone();
            NpcRecord test = n.Clone();
            try
            {
                test.Name = npcName.Text;
                test.Model = npcModel.Text;
                test.NpcFile = npcFile.Text;
                if (double.TryParse(npcX.Text, out double x) && Math.Abs(x - n.X) > 0.005) test.X = x;
                if (double.TryParse(npcY.Text, out double y) && Math.Abs(y - n.Y) > 0.005) test.Y = y;
                if (double.TryParse(npcZ.Text, out double z) && Math.Abs(z - n.Z) > 0.005) test.Z = z;
                double deg = (double)npcAngle.Value;
                if (Math.Abs(deg - Math.Round(n.Degrees, 1)) > 0.01) test.Degrees = ((deg % 360) + 360) % 360;
                test.Active = npcActive.Checked;
            }
            catch (ArgumentException ex)
            {
                Ui.Error(this, ex.Message);
                FillNpcEditor();
                return;
            }
            if (test.Raw.SequenceEqual(before.Raw)) return;
            Session.Checkpoint();
            Buffer.BlockCopy(test.Raw, 0, OneNpc().Raw, 0, test.Raw.Length);
            Session.UpdateDirty();
            RefreshLists();
            UpdateButtons();
        }

        void ApplyNpcFileChoice()
        {
            NpcRecord n = OneNpc();
            if (n == null) return;
            string file = Path.GetFileName(npcFile.Text);
            DataItem d = data.Npcs.FirstOrDefault(i => string.Equals(i.FileName, file, StringComparison.OrdinalIgnoreCase));
            if (d != null)
            {
                npcName.Text = FitBytes(d.Name, 31);
                TextDoc doc = TextDoc.Load(d.Path);
                string model = Tokens.Unquote(doc.First("*모양파일"));
                if (model.Length > 0) npcModel.Text = model;
            }
            ApplyNpcEdit();
        }

        static string FitBytes(string s, int max)
        {
            s = s ?? "";
            while (Cp949.Encode(s).Length > max) s = s.Substring(0, s.Length - 1);
            return s;
        }

        void SnapNpc()
        {
            NpcRecord n = OneNpc();
            if (n == null || Session.Mesh == null) return;
            List<double> surfaces = Session.Mesh.SurfacesAt(n.X, n.Z);
            if (surfaces.Count == 0) { Ui.Error(this, "There's no ground under this NPC."); return; }
            // pressing again steps down through bridges and floors
            int i = surfaces.FindIndex(h => Math.Abs(h - n.Y) < 1);
            double next = i >= 0 ? surfaces[(i + 1) % surfaces.Count] : surfaces[0];
            Session.Checkpoint();
            n.Y = next;
            Session.UpdateDirty();
            RefreshLists();
            UpdateButtons();
        }

        void OpenNpcFile()
        {
            NpcRecord n = OneNpc();
            if (n == null) return;
            host.OpenNpcByFile(Path.GetFileName(n.NpcFile));
        }

        void OpenShop()
        {
            NpcRecord n = OneNpc();
            if (n == null) { Ui.Error(this, "Select an NPC first."); return; }
            string path = Path.Combine(cat.ServerRoot, n.NpcFile);
            if (!File.Exists(path)) { Ui.Error(this, "This NPC's file isn't there:\n" + path); return; }
            if (host.Npcs.HasUnsaved && string.Equals(host.Npcs.Doc?.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                Ui.Error(this, "This NPC's file has unsaved changes on the NPCs tab. Save them there first.");
                return;
            }
            using (var d = new ShopDialog(path, () => data, NpcDisplayName(n)))
            {
                d.ShowDialog(this);
                if (d.Saved)
                {
                    host.SetStatus("Saved " + Path.GetFileName(path) + ". Reload the server for this to take effect.");
                    host.Npcs.FileSavedElsewhere(path);
                }
            }
        }

        /// <summary>Names changed on disk: show the new ones.</summary>
        public void DataChanged(ServerData d)
        {
            data = d;
            if (Session == null) return;
            RefreshLists();
            FillSpm();
        }

        public string TestNpcName(NpcRecord n) => NpcDisplayName(n);

        void CenterOnSelection()
        {
            NpcRecord n = OneNpc();
            SpawnFlag f = OneFlag();
            if (n != null) Canvas.CenterOn(n.X, n.Z);
            else if (f != null) Canvas.CenterOn(f.X, f.Z);
        }

        // ----- edits from the canvas -----

        void PlaceFlag(double x, double z)
        {
            Session.Checkpoint();
            SpawnFlag f = Session.AddFlag(x, z);
            if (f == null)
            {
                Session.Undo();
                Ui.Error(this, "This map's flag file is full (" + Session.Spp.Capacity + " slots). Delete a flag first.");
                SetTool(CanvasTool.Select);
                return;
            }
            Canvas.ClearSelection();
            Canvas.SelFlags.Add(f.Slot);
            side.SelectedIndex = 0;
            RefreshLists();
            UpdateButtons();
        }

        void PlaceNpc(double x, double z)
        {
            using (var dlg = new NpcPickerDialog(data.Npcs))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Chosen == null) return;
                AddNpcFromFile(dlg.Chosen, x, z);
            }
        }

        public NpcRecord AddNpcFromFile(DataItem d, double x, double z, double degrees = 0)
        {
            TextDoc doc = TextDoc.Load(d.Path);
            string model = Tokens.Unquote(doc.First("*모양파일"));
            Session.Checkpoint();
            NpcRecord n;
            try { n = Session.AddNpc(x, z, FitBytes(d.Name, 31), model, data.RelativeNpcPath(d.Path), degrees); }
            catch (InvalidOperationException ex)
            {
                Session.Undo();
                Ui.Error(this, ex.Message);
                return null;
            }
            catch (ArgumentException ex)
            {
                Session.Undo();
                Ui.Error(this, ex.Message);
                return null;
            }
            if (n == null)
            {
                Session.Undo();
                Ui.Error(this, "This map's NPC file is full (" + Session.Spc.Capacity + " slots). Delete an NPC first.");
                SetTool(CanvasTool.Select);
                return null;
            }
            Canvas.ClearSelection();
            Canvas.SelNpcs.Add(n.Slot);
            side.SelectedIndex = 1;
            SetTool(CanvasTool.Select);
            RefreshLists();
            UpdateButtons();
            return n;
        }

        public void DeleteSelected()
        {
            if (Session == null || Canvas.SelFlags.Count + Canvas.SelNpcs.Count == 0) return;
            Session.Checkpoint();
            Session.Flags.RemoveAll(f => Canvas.SelFlags.Contains(f.Slot));
            Session.Npcs.RemoveAll(n => Canvas.SelNpcs.Contains(n.Slot));
            Canvas.ClearSelection();
            Session.UpdateDirty();
            RefreshLists();
            UpdateButtons();
        }

        void DuplicateSelected()
        {
            if (Session == null || Canvas.SelFlags.Count + Canvas.SelNpcs.Count == 0) return;
            Session.Checkpoint();
            const int Offset = 150;
            var newFlags = new List<int>();
            var newNpcs = new List<int>();
            foreach (SpawnFlag f in Session.Flags.Where(x => Canvas.SelFlags.Contains(x.Slot)).ToList())
            {
                SpawnFlag c = Session.AddFlag(f.X + Offset, f.Z - Offset);
                if (c == null) break;
                c.State = f.State;
                newFlags.Add(c.Slot);
            }
            foreach (NpcRecord n in Session.Npcs.Where(x => Canvas.SelNpcs.Contains(x.Slot)).ToList())
            {
                NpcRecord c = Session.AddNpc(n.X + Offset, n.Z - Offset, n.Name, n.Model, n.NpcFile, n.Degrees);
                if (c == null) break;
                newNpcs.Add(c.Slot);
            }
            Canvas.ClearSelection();
            newFlags.ForEach(s => Canvas.SelFlags.Add(s));
            newNpcs.ForEach(s => Canvas.SelNpcs.Add(s));
            Session.UpdateDirty();
            RefreshLists();
            UpdateButtons();
        }

        void Nudge(int dx, int dz)
        {
            if (Canvas.SelFlags.Count + Canvas.SelNpcs.Count == 0) return;
            Session.Checkpoint();
            foreach (SpawnFlag f in Session.Flags.Where(x => Canvas.SelFlags.Contains(x.Slot))) { f.X += dx; f.Z += dz; }
            foreach (NpcRecord n in Session.Npcs.Where(x => Canvas.SelNpcs.Contains(x.Slot)))
            {
                n.RawX += dx * 256; n.RawZ += dz * 256;
                n.Y = Session.GroundOr(n.X, n.Z, n.Y);
            }
            Session.UpdateDirty();
            RefreshLists();
            UpdateButtons();
        }

        void Rotate(double by)
        {
            var list = Session.Npcs.Where(x => Canvas.SelNpcs.Contains(x.Slot)).ToList();
            if (list.Count == 0) return;
            Session.Checkpoint();
            foreach (NpcRecord n in list) n.Degrees = ((n.Degrees + by) % 360 + 360) % 360;
            Session.UpdateDirty();
            RefreshLists();
            UpdateButtons();
        }

        void CanvasKey(object s, KeyEventArgs e)
        {
            if (Session == null) return;
            int step = e.Shift ? 100 : 10;
            bool handled = true;
            switch (e.KeyCode)
            {
                case Keys.Delete: DeleteSelected(); break;
                case Keys.Escape:
                    if (Canvas.Tool != CanvasTool.Select) SetTool(CanvasTool.Select);
                    else { Canvas.ClearSelection(); SyncListsFromCanvas(); Canvas.Invalidate(); }
                    break;
                case Keys.F: Canvas.FitAll(); break;
                case Keys.Left: Nudge(-step, 0); break;
                case Keys.Right: Nudge(step, 0); break;
                case Keys.Up: Nudge(0, step); break;
                case Keys.Down: Nudge(0, -step); break;
                case Keys.Q: Rotate(e.Shift ? -45 : -5); break;
                case Keys.E: Rotate(e.Shift ? 45 : 5); break;
                case Keys.Z when e.Control: DoUndo(); break;
                case Keys.Y when e.Control: DoRedo(); break;
                case Keys.D when e.Control: DuplicateSelected(); break;
                case Keys.A when e.Control:
                    foreach (SpawnFlag f in Session.Flags) Canvas.SelFlags.Add(f.Slot);
                    SyncListsFromCanvas();
                    Canvas.Invalidate();
                    break;
                default: handled = false; break;
            }
            e.Handled = handled;
        }

        void ShowCanvasMenu(Point at, double x, double z)
        {
            var menu = new ContextMenuStrip();
            object hit = Canvas.HitTest(at);
            if (hit != null && !(hit is SpawnFlag hf && Canvas.SelFlags.Contains(hf.Slot)) && !(hit is NpcRecord hn && Canvas.SelNpcs.Contains(hn.Slot)))
            {
                Canvas.ClearSelection();
                if (hit is SpawnFlag f) Canvas.SelFlags.Add(f.Slot);
                if (hit is NpcRecord n) Canvas.SelNpcs.Add(n.Slot);
                SyncListsFromCanvas();
                Canvas.Invalidate();
            }
            menu.Items.Add("Add monster flag here", null, (s, e) => PlaceFlag(x, z));
            menu.Items.Add("Add NPC here…", null, (s, e) => PlaceNpc(x, z));
            if (Canvas.SelNpcs.Count > 0)
                menu.Items.Add("Turn selected NPC to face here", null, (s, e) =>
                {
                    Session.Checkpoint();
                    foreach (NpcRecord n in Session.Npcs.Where(q => Canvas.SelNpcs.Contains(q.Slot)))
                    {
                        double deg = Math.Atan2(x - n.X, z - n.Z) * 180 / Math.PI;
                        n.Degrees = deg < 0 ? deg + 360 : deg;
                    }
                    Session.UpdateDirty();
                    RefreshLists();
                    UpdateButtons();
                });
            if (Canvas.SelFlags.Count + Canvas.SelNpcs.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Duplicate", null, (s, e) => DuplicateSelected());
                menu.Items.Add("Delete", null, (s, e) => DeleteSelected());
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(string.Format("Copy position ({0:0}, {1:0})", x, z), null, (s, e) => Clipboard.SetText(string.Format("{0:0} {1:0}", x, z)));
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            menu.Show(Canvas, at);
        }

        void DoUndo()
        {
            if (Session == null || !Session.CanUndo) return;
            Session.Undo();
            RefreshLists();
            UpdateButtons();
        }

        void DoRedo()
        {
            if (Session == null || !Session.CanRedo) return;
            Session.Redo();
            RefreshLists();
            UpdateButtons();
        }

        // ----- monster list -----

        void FillSpm()
        {
            syncing = true;
            bool has = Session?.Spm != null;
            spmBody.Visible = has;
            spmCreate.Visible = Session != null && !has;
            actorGrid.Rows.Clear();
            bossGrid.Rows.Clear();
            if (has)
            {
                SpmDoc d = Session.Spm;
                spmMax.Text = Tokens.Split(d.Get(SpmDoc.KeysMax)).FirstOrDefault() ?? "";
                spmCount.Text = Tokens.Split(d.Get(SpmDoc.KeysCount)).FirstOrDefault() ?? "";
                List<string> iv = Tokens.Split(d.Get(SpmDoc.KeysInterval));
                spmMin.Text = iv.Count > 0 ? iv[0] : "";
                spmMaxI.Text = iv.Count > 1 ? iv[1] : "";
                foreach (SpmActor a in d.Actors()) actorGrid.Rows.Add(a.Monster, EnglishOf(a.Monster), a.Percent);
                foreach (SpmBoss b in d.Bosses()) bossGrid.Rows.Add(b.Boss, b.Guard, b.GuardCount, b.Hours);
                MarkUnknownMonsters();
                UpdateSpmTotal();
            }
            syncing = false;
        }

        string EnglishOf(string name)
        {
            DataItem m = data?.Monsters.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            return m?.LocalName ?? "";
        }

        bool KnownMonster(string name) => data == null || string.IsNullOrEmpty(name) || data.Monsters.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

        void MarkUnknownMonsters()
        {
            foreach (DataGridViewRow r in actorGrid.Rows)
                if (!r.IsNewRow) r.Cells["m"].Style.ForeColor = KnownMonster(r.Cells["m"].Value as string) ? Color.Empty : Color.Red;
            foreach (DataGridViewRow r in bossGrid.Rows)
                if (!r.IsNewRow)
                {
                    r.Cells["b"].Style.ForeColor = KnownMonster(r.Cells["b"].Value as string) ? Color.Empty : Color.Red;
                    r.Cells["g"].Style.ForeColor = KnownMonster(r.Cells["g"].Value as string) ? Color.Empty : Color.Red;
                }
        }

        void UpdateSpmTotal()
        {
            double total = 0;
            foreach (DataGridViewRow r in actorGrid.Rows)
                if (!r.IsNewRow && double.TryParse(r.Cells["p"].Value as string, out double p)) total += p;
            spmTotal.Text = "Rates add up to " + total.ToString("0.##") + ". Red names have no monster file.";
        }

        void ApplySpmNumbers()
        {
            if (syncing || Session?.Spm == null) return;
            SpmDoc d = Session.Spm;
            d.Set(SpmDoc.KeysMax, spmMax.Text);
            d.Set(SpmDoc.KeysCount, spmCount.Text);
            string iv = (spmMin.Text + "\t" + spmMaxI.Text).Trim();
            if (Tokens.Split(d.Get(SpmDoc.KeysInterval)).SequenceEqual(Tokens.Split(iv))) { UpdateButtons(); return; }
            d.Set(SpmDoc.KeysInterval, iv);
            UpdateButtons();
        }

        void ApplyActors()
        {
            if (syncing || Session?.Spm == null) return;
            var list = new List<SpmActor>();
            foreach (DataGridViewRow r in actorGrid.Rows)
            {
                if (r.IsNewRow) continue;
                string name = (r.Cells["m"].Value as string ?? "").Trim();
                if (name.Length == 0) continue;
                list.Add(new SpmActor { Monster = name, Percent = (r.Cells["p"].Value as string ?? "").Trim() });
                r.Cells["e"].Value = EnglishOf(name);
            }
            Session.Spm.SetActors(list);
            MarkUnknownMonsters();
            UpdateSpmTotal();
            UpdateButtons();
        }

        void ApplyBosses()
        {
            if (syncing || Session?.Spm == null) return;
            var list = new List<SpmBoss>();
            foreach (DataGridViewRow r in bossGrid.Rows)
            {
                if (r.IsNewRow) continue;
                string name = (r.Cells["b"].Value as string ?? "").Trim();
                if (name.Length == 0) continue;
                list.Add(new SpmBoss
                {
                    Boss = name,
                    Guard = (r.Cells["g"].Value as string ?? "").Trim(),
                    GuardCount = (r.Cells["c"].Value as string ?? "").Trim(),
                    Hours = (r.Cells["h"].Value as string ?? "").Trim()
                });
            }
            Session.Spm.SetBosses(list);
            MarkUnknownMonsters();
            UpdateButtons();
        }

        void CreateSpm()
        {
            if (Session == null) return;
            Session.Spm = SpmDoc.CreateNew(cat.NewSpawnPath(Session.Entry, "spm"));
            FillSpm();
            UpdateButtons();
        }

        // ----- map tab -----

        void FillMapTab()
        {
            if (Session == null) return;
            MapEntry m = Session.Entry;
            var s = new System.Text.StringBuilder();
            s.AppendLine(m.Stem);
            if (m.HasGeometry)
            {
                s.AppendLine("File: Field\\" + m.RelPath);
                s.AppendLine(string.Format("World X {0:0} to {1:0}, Z {2:0} to {3:0}", m.MinX, m.MaxX, m.MinZ, m.MaxZ));
                s.AppendLine(string.Format("Centre {0:0}, {1:0}. Size {2:0} x {3:0}", (m.MinX + m.MaxX) / 2, (m.MinZ + m.MaxZ) / 2, m.MaxX - m.MinX, m.MaxZ - m.MinZ));
                s.AppendLine(string.Format("{0:N0} vertices, {1:N0} faces", m.Verts, m.Faces));
            }
            else s.AppendLine("No .smd file for this map in the Field folder.");
            s.AppendLine("Terrain: " + (m.TerrainTextured ? "textured" : "plain. For real textures, see File > Use textures from a client folder"));
            s.AppendLine("Minimap: " + (m.MinimapPath != null ? Path.GetFileName(m.MinimapPath) : "none, showing terrain only"));
            if (m.MinimapPath != null)
                s.AppendLine(m.ManualDX != null ? string.Format("Minimap moved by hand: {0:0}, {1:0}", m.MinimapDX, m.MinimapDZ) : "Minimap stretched to the map's bounds");
            s.AppendLine("Spawn files: " + (m.HasSpawnData ? string.Join(", ", new[] { m.SppPath, m.SpcPath, m.SpmPath }.Where(p => p != null).Select(Path.GetFileName)) : "none yet, they're made when you save"));
            mapInfo.Text = s.ToString();

            var w = new List<string>();
            if (m.OutsideCount > 0) w.Add(m.OutsideCount + " flags or NPCs are outside this map's bounds (shown with an orange ring). If most are, the map may have moved: use Shift.");
            if (m.InServerExe == false) w.Add("The server program doesn't list this map, so it may not load it, even with spawn files.");
            warn.Text = string.Join("\n\n", w);
            warn.Visible = w.Count > 0;

            twinList.Items.Clear();
            foreach (TwinInfo t in m.Twins)
                twinList.Items.Add(string.Format("{0}: this map is X {1:+0;-0;0}, Z {2:+0;-0;0} from it{3}", t.Stem, t.DX, t.DZ, t.Exact ? "" : " (differs in places)"));
            if (m.Twins.Count == 0) twinList.Items.Add("None found.");
        }

        void TrySaveSettings()
        {
            try { cat.SaveSettings(); }
            catch (IOException ex) { Ui.Error(this, "Couldn't save PriTaTor.ini:\n" + ex.Message); }
            catch (UnauthorizedAccessException ex) { Ui.Error(this, "Couldn't save PriTaTor.ini:\n" + ex.Message); }
        }

        void ChooseMinimap()
        {
            if (Session == null) return;
            string dir = Path.Combine(cat.FieldRoot, "map");
            using (var d = new OpenFileDialog { InitialDirectory = dir, Filter = "Minimaps (*.tga;*.bmp)|*.tga;*.bmp", Title = "Choose a minimap" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Bitmap bmp = GameImage.Load(d.FileName);
                    Canvas.Minimap?.Dispose();
                    Canvas.Minimap = bmp;
                    Session.Entry.MinimapPath = d.FileName;
                    Session.Entry.ManualMinimap = Path.GetFileName(d.FileName);
                    TrySaveSettings();
                    FillMapTab();
                    Canvas.InvalidateLayers();
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException)
                {
                    Ui.Error(this, "Couldn't read that image:\n" + ex.Message);
                }
            }
        }

        void ResetMinimap()
        {
            if (Session == null) return;
            Session.Entry.ManualDX = Session.Entry.ManualDZ = null;
            TrySaveSettings();
            FillMapTab();
            Canvas.InvalidateLayers();
        }

        void ImportDialogShow()
        {
            if (Session == null) return;
            using (var d = new ImportDialog(cat, Session.Entry))
            {
                if (d.Source.Items.Count == 0) { Ui.Error(this, "No other map has spawn data to copy."); return; }
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                DoImport(d.Chosen, (int)d.DX.Value, (int)d.DZ.Value, d.Flags.Checked, d.Npcs.Checked, d.Monsters.Checked, d.Replace.Checked);
            }
        }

        public int DoImport(MapEntry source, int dx, int dz, bool flags, bool npcs, bool monsters, bool replace)
        {
            Session.Checkpoint();
            int n = Session.ImportFrom(source, dx, dz, flags, npcs, monsters, replace);
            Canvas.ClearSelection();
            Session.UpdateDirty();
            FillSpm();
            RefreshLists();
            FillMapTab();
            UpdateButtons();
            return n;
        }

        void ShiftDialogShow()
        {
            if (Session == null) return;
            int sx = 0, sz = 0;
            string note = "";
            MapEntry m = Session.Entry;
            if (m.HasGeometry && Session.Flags.Count + Session.Npcs.Count > 0)
            {
                // how far the points' middle is from the map's middle, rounded to a likely move
                var xs = Session.Flags.Select(f => (double)f.X).Concat(Session.Npcs.Select(n => n.X)).ToList();
                var zs = Session.Flags.Select(f => (double)f.Z).Concat(Session.Npcs.Select(n => n.Z)).ToList();
                double dx = (m.MinX + m.MaxX) / 2 - (xs.Min() + xs.Max()) / 2, dz = (m.MinZ + m.MaxZ) / 2 - (zs.Min() + zs.Max()) / 2;
                if (m.OutsideCount * 2 > xs.Count)
                {
                    sx = (int)(Math.Round(dx / 4096) * 4096);
                    sz = (int)(Math.Round(dz / 4096) * 4096);
                    note = "Most of this map's flags and NPCs are off the map. These amounts would bring them back: the nearest round move to the map's middle.";
                }
            }
            using (var d = new ShiftDialog(sx, sz, note))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Session.Checkpoint();
                Session.ShiftAll((int)d.DX.Value, (int)d.DZ.Value, d.Flags.Checked, d.Npcs.Checked);
                Session.UpdateDirty();
                RefreshLists();
                UpdateButtons();
            }
        }

        // ----- saving -----

        public bool HasUnsaved => Session != null && Session.IsDirty;

        public void SaveCurrent()
        {
            if (Session == null) return;
            ApplyPendingEdits();
            if (!Session.IsDirty) return;
            try
            {
                List<string> changed = Session.ChangedOnDisk();
                if (changed.Count > 0 && MessageBox.Show(this,
                        "These files changed on disk since you opened the map, and saving will overwrite them:\n\n" + string.Join("\n", changed.Select(Path.GetFileName)) + "\n\nOverwrite?",
                        "PriTaTor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                List<string> written = Session.Save();
                host.SetStatus(written.Count > 0
                    ? "Saved " + string.Join(", ", written.Select(Path.GetFileName)) + ". Reload the server for these to take effect. A copy of each old file is in " + SafeWrite.BackupFolderName + "."
                    : "Nothing needed saving.");
                UpdateTreeText();
                FillMapTab();
                UpdateButtons();
            }
            catch (IOException ex) { Ui.Error(this, "Saving failed:\n" + ex.Message); }
            catch (UnauthorizedAccessException ex) { Ui.Error(this, "Saving failed:\n" + ex.Message); }
        }

        void ApplyPendingEdits()
        {
            if (OneFlag() != null) ApplyFlagEdit();
            if (OneNpc() != null) ApplyNpcEdit();
            actorGrid.EndEdit();
            bossGrid.EndEdit();
        }

        public bool ConfirmLeave()
        {
            if (!HasUnsaved) return true;
            DialogResult r = Ui.AskSave(this, Session.Entry.Stem);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes)
            {
                SaveCurrent();
                return !HasUnsaved;
            }
            return true;
        }
    }
}
