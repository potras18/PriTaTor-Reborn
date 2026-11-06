using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PriTaTor.Core;

namespace PriTaTor.UI
{
    public sealed class MainForm : Form
    {
        public readonly string ServerRoot;
        readonly string fieldOverride;
        readonly MenuStrip menu = new MenuStrip();
        public readonly TabControl Tabs = new TabControl { Dock = DockStyle.Fill };
        readonly StatusStrip status = new StatusStrip();
        readonly ToolStripStatusLabel statusText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolStripProgressBar progress = new ToolStripProgressBar { Visible = false, Width = 180 };

        public Catalog Cat { get; private set; }
        public ServerData Data { get; private set; }
        public MapEditorPanel Maps { get; private set; }
        public DataFilePanel Monsters, Npcs, Items, Names;
        public TextFilesPanel TextFiles { get; private set; }
        readonly List<IEditorPanel> panels = new List<IEditorPanel>();
        public event Action Ready;
        Dictionary<string, List<string>> monsterMaps;
        int lastTab;

        public MainForm(string serverRoot, string fieldOverride)
        {
            ServerRoot = serverRoot;
            this.fieldOverride = fieldOverride;
            Text = "PriTaTor Reborn";
            Font = Ui.BaseFont;
            ClientSize = new Size(1500, 900);
            MinimumSize = new Size(1000, 600);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            status.Items.Add(statusText);
            status.Items.Add(progress);
            BuildMenu();
            Controls.Add(Tabs);
            Controls.Add(status);
            Controls.Add(menu);
            MainMenuStrip = menu;
            Shown += (s, e) => StartScan(false);
            FormClosing += (s, e) => { if (panels.Any(p => !p.ConfirmLeave())) e.Cancel = true; };
            Tabs.Selecting += TabSelecting;
        }

        void BuildMenu()
        {
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(new ToolStripMenuItem("&Save", null, (s, e) => CurrentPanel()?.SaveCurrent()) { ShortcutKeys = Keys.Control | Keys.S });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("&Rebuild server settings", null, (s, e) => Rebuild()));
            file.DropDownItems.Add(new ToolStripMenuItem("Use a different &Field folder…", null, (s, e) => ChooseField()));
            file.DropDownItems.Add(new ToolStripMenuItem("Open &backups folder", null, (s, e) => OpenBackups()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (s, e) => Close()));
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&Map editor keys", null, (s, e) => ShowKeys()));
            help.DropDownItems.Add(new ToolStripMenuItem("&About", null, (s, e) => ShowAbout()));
            menu.Items.Add(file);
            menu.Items.Add(help);
        }

        IEditorPanel CurrentPanel() => Tabs.SelectedTab?.Controls.Count > 0 ? Tabs.SelectedTab.Controls[0] as IEditorPanel : null;

        void TabSelecting(object s, TabControlCancelEventArgs e)
        {
            if (e.TabPage?.Controls.Count > 0 && e.TabPage.Controls[0] is DataFilePanel d) d.EnsureLoaded();
            lastTab = e.TabPageIndex;
        }

        // ----- start up -----

        void StartScan(bool force, string newField = null)
        {
            SetStatus(force ? "Rebuilding server settings…" : "Reading the server folder…");
            progress.Visible = true;
            progress.Style = ProgressBarStyle.Marquee;
            UseWaitCursor = true;
            string fieldArg = newField ?? fieldOverride;
            Task.Run(() =>
            {
                Catalog c = null;
                ServerData d = null;
                Exception err = null;
                try
                {
                    c = Catalog.Scan(ServerRoot, fieldArg, force, (msg, n, total) => BeginInvoke((Action)(() =>
                    {
                        statusText.Text = msg;
                        if (total > 0)
                        {
                            progress.Style = ProgressBarStyle.Continuous;
                            progress.Maximum = total;
                            progress.Value = Math.Min(n, total);
                        }
                    })));
                    d = ServerData.Load(ServerRoot);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { err = ex; }
                BeginInvoke((Action)(() => ScanDone(c, d, err)));
            });
        }

        void ScanDone(Catalog c, ServerData d, Exception err)
        {
            UseWaitCursor = false;
            progress.Visible = false;
            if (err != null)
            {
                Ui.Error(this, "Couldn't read the server folder:\n" + err.Message);
                SetStatus("Couldn't read the server folder.");
                return;
            }
            bool first = Cat == null;
            Cat = c;
            Data = d;
            monsterMaps = null;
            Text = "PriTaTor Reborn - " + ServerRoot + (string.Equals(c.FieldRoot, Path.Combine(ServerRoot, "Field"), StringComparison.OrdinalIgnoreCase) ? "" : "  (Field: " + c.FieldRoot + ")");
            if (first) BuildTabs();
            else Maps.RefreshCatalog(c);
            int linked = c.Maps.Count(m => m.Twins.Count > 0);
            SetStatus(string.Format("{0} {1} maps, {2} with spawn data, {3} matched to a moved copy of themselves.",
                c.GeneratedThisRun ? "Settings generated for this server:" : "Settings loaded:",
                c.Maps.Count(m => m.HasGeometry), c.Maps.Count(m => m.HasSpawnData), linked));
            Ready?.Invoke();
        }

        void BuildTabs()
        {
            Maps = new MapEditorPanel(this) { Dock = DockStyle.Fill };
            Maps.Bind(Cat, Data);
            string g = Path.Combine(ServerRoot, "GameServer");
            Monsters = new DataFilePanel(this, Schema.Monster, new[] { Path.Combine(g, "Monster") }, ServerData.MonsterExt, "*레벨", "Level", MonsterUsage) { Dock = DockStyle.Fill };
            Npcs = new DataFilePanel(this, Schema.Npc, new[] { Path.Combine(g, "NPC") }, ServerData.NpcExt, null, null, null) { Dock = DockStyle.Fill };
            Items = new DataFilePanel(this, Schema.Item, new[] { Path.Combine(g, "OpenItem") }, ServerData.ItemExt, "*코드", "Code", null) { Dock = DockStyle.Fill };
            Names = new DataFilePanel(this, Schema.Zhoon, new[] { Path.Combine(g, "OpenItem", "name"), Path.Combine(g, "Monster", "name"), Path.Combine(g, "NPC", "name") }, ServerData.ZhoonExt, null, null, null) { Dock = DockStyle.Fill };
            TextFiles = new TextFilesPanel(this) { Dock = DockStyle.Fill };
            TextFiles.Bind(ServerRoot);
            AddTab("Maps", Maps, Maps);
            AddTab("Monsters", Monsters, Monsters);
            AddTab("NPCs", Npcs, Npcs);
            AddTab("Items", Items, Items);
            AddTab("Names", Names, Names);
            AddTab("Text files", TextFiles, TextFiles);
        }

        void AddTab(string title, Control c, IEditorPanel p)
        {
            var t = new TabPage(title);
            t.Controls.Add(c);
            Tabs.TabPages.Add(t);
            panels.Add(p);
        }

        string MonsterUsage(string name)
        {
            if (monsterMaps == null)
            {
                monsterMaps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (MapEntry m in Cat.Maps.Where(x => x.SpmPath != null))
                {
                    try
                    {
                        SpmDoc d = SpmDoc.Load(m.SpmPath);
                        foreach (string n in d.Actors().Select(a => a.Monster).Concat(d.Bosses().SelectMany(b => new[] { b.Boss, b.Guard })))
                        {
                            if (!monsterMaps.TryGetValue(n, out List<string> list)) monsterMaps[n] = list = new List<string>();
                            if (!list.Contains(m.Stem)) list.Add(m.Stem);
                        }
                    }
                    catch (IOException) { }
                }
            }
            return monsterMaps.TryGetValue(name ?? "", out List<string> l) ? "Spawns on: " + string.Join(", ", l) : "Not in any map's monster list.";
        }

        // ----- commands -----

        void Rebuild()
        {
            if (panels.Any(p => !p.ConfirmLeave())) return;
            StartScan(true, Cat?.FieldRoot);
        }

        void ChooseField()
        {
            if (panels.Any(p => !p.ConfirmLeave())) return;
            using (var d = new FolderBrowserDialog { Description = "Choose the Field folder holding the map files", SelectedPath = Cat?.FieldRoot })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                StartScan(true, d.SelectedPath);
            }
        }

        void OpenBackups()
        {
            string p = Path.Combine(ServerRoot, SafeWrite.BackupFolderName);
            if (!Directory.Exists(p)) { Ui.Error(this, "Nothing has been backed up yet."); return; }
            Process.Start("explorer.exe", "\"" + p + "\"");
        }

        void ShowKeys() => MessageBox.Show(this,
            "Left click: select. Ctrl+click: add to selection.\n" +
            "Drag a flag or NPC: move it. Drag empty ground: box select.\n" +
            "Drag the white handle on a selected NPC: turn it. Hold Shift to snap to 15°.\n" +
            "Right drag or middle drag: pan. Wheel: zoom. Right click: menu.\n\n" +
            "Arrow keys: move selection by 10 (with Shift: 100).\n" +
            "Q / E: turn selected NPCs by 5° (with Shift: 45°).\n" +
            "Del: delete. Ctrl+D: duplicate. Ctrl+A: select all flags.\n" +
            "Ctrl+Z / Ctrl+Y: undo and redo. Ctrl+S: save. F: fit. Esc: back to Select.",
            "Map editor keys");

        void ShowAbout() => MessageBox.Show(this,
            "PriTaTor Reborn " + Application.ProductVersion + "\nPriston Tale server manager and map editor.\n\n" +
            "Map positions are measured from this server's own map files and kept in PriTaTor.ini.\n" +
            "The server reads its files at start-up: reload it after saving.\n\n" +
            "Credits\n" +
            "Sumomo created PriTaTor in 2008 and wrote every version up to PriTaTor 2.\n" +
            "bobsobol kept the 1.8 downloads and source available and helped with the game's text encoding.\n" +
            "MysteryPT made the map references that shipped with PriTaTor 2.", "About PriTaTor Reborn");

        public void OpenInTextTab(string path)
        {
            foreach (TabPage t in Tabs.TabPages)
                if (t.Controls[0] == TextFiles) Tabs.SelectedTab = t;
            if (TextFiles.ConfirmLeave()) TextFiles.ShowFile(path);
        }

        public void OpenNpcByFile(string fileName)
        {
            foreach (TabPage t in Tabs.TabPages)
                if (t.Controls[0] == Npcs) Tabs.SelectedTab = t;
            if (!Npcs.SelectByFile(fileName)) SetStatus("No .npc file called " + fileName + " in GameServer\\NPC.");
        }

        /// <summary>A name was edited: read them again so everywhere shows the new one.</summary>
        public void NamesChanged(DataFilePanel from)
        {
            string root = ServerRoot;
            Task.Run(() => ServerData.Load(root)).ContinueWith(t =>
            {
                if (t.IsFaulted || IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    Data = t.Result;
                    Maps.DataChanged(Data);
                    foreach (DataFilePanel p in new[] { Monsters, Npcs, Items, Names })
                        if (p != from) p.ReloadNames();
                }));
            });
        }

        public void SetStatus(string s) => statusText.Text = s;

        public void SetUnsavedMark()
        {
            foreach (TabPage t in Tabs.TabPages)
            {
                if (!(t.Controls.Count > 0 && t.Controls[0] is IEditorPanel p)) continue;
                string baseText = t.Text.TrimEnd(' ', '*');
                string want = p.HasUnsaved ? baseText + " *" : baseText;
                if (t.Text != want) t.Text = want;
            }
        }
    }
}
