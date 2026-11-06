// Drives the real window through its real input handlers and saves screenshots: PriTaTor --uitest <folder>.
// Only ever point this at a copy of a server. It edits spawn and data files.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using PriTaTor.Core;

namespace PriTaTor.UI
{
    public static class UiTest
    {
        public static bool Running;
        public static int ExitCode;
        static string outDir;
        static MainForm form;
        static StringBuilder log = new StringBuilder();
        static int fails;

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_MOUSEMOVE = 0x200;

        static void Pump()
        {
            for (int i = 0; i < 5; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }

        /// <summary>Sends a key through the Windows message queue, the same route a real key press takes.</summary>
        static void PostKey(Control c, Keys k)
        {
            PostMessage(c.Handle, WM_KEYDOWN, (IntPtr)(int)k, IntPtr.Zero);
            PostMessage(c.Handle, WM_KEYUP, (IntPtr)(int)k, IntPtr.Zero);
            Pump();
        }

        static IntPtr MouseL(Point p) => (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));

        static void PostDrag(Control c, Point from, Point to)
        {
            PostMessage(c.Handle, WM_MOUSEMOVE, IntPtr.Zero, MouseL(from));
            PostMessage(c.Handle, WM_LBUTTONDOWN, (IntPtr)1, MouseL(from));
            Pump();
            for (int i = 1; i <= 4; i++)
                PostMessage(c.Handle, WM_MOUSEMOVE, (IntPtr)1, MouseL(new Point(from.X + (to.X - from.X) * i / 4, from.Y + (to.Y - from.Y) * i / 4)));
            Pump();
            PostMessage(c.Handle, WM_LBUTTONUP, IntPtr.Zero, MouseL(to));
            Pump();
        }

        static void ShotOf(Form dlg, string name)
        {
            dlg.Show(form);
            Pump();
            using (var bmp = new Bitmap(dlg.Width, dlg.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr dc = g.GetHdc();
                    PrintWindow(dlg.Handle, dc, 2);
                    g.ReleaseHdc(dc);
                }
                bmp.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }
            dlg.Close();
            dlg.Dispose();
        }

        public static void Attach(MainForm f, string dir)
        {
            if (Path.GetFullPath(f.ServerRoot).StartsWith(@"X:\pristontale", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to run UI checks on an original server folder.");
            Running = true;
            form = f;
            outDir = dir;
            Directory.CreateDirectory(dir);
            bool started = false;
            f.Ready += () =>
            {
                if (started) return;
                started = true;
                f.BeginInvoke((Action)(async () =>
                {
                    try { await Script(); }
                    catch (Exception ex) { Fail(ex); }
                    Finish();
                }));
            };
        }

        public static void Fail(Exception ex)
        {
            fails++;
            Log("EXCEPTION " + ex);
        }

        static void Log(string s)
        {
            log.AppendLine(s);
            File.WriteAllText(Path.Combine(outDir, "uitest.log"), log.ToString(), Encoding.UTF8);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) fails++;
            Log((ok ? "  ok    " : "  FAIL  ") + what);
        }

        static void Finish()
        {
            Log("failures: " + fails);
            ExitCode = fails == 0 ? 0 : 1;
            foreach (Form o in Application.OpenForms.Cast<Form>().ToList()) if (o != form) o.Close();
            form.FormClosing += (s, e) => e.Cancel = false;
            Application.Exit();
        }

        static void Shot(string name)
        {
            Application.DoEvents();
            using (var bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr dc = g.GetHdc();
                    PrintWindow(form.Handle, dc, 2);
                    g.ReleaseHdc(dc);
                }
                bmp.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }
        }

        static Task OpenMap(MapEditorPanel p, string stem)
        {
            var t = new TaskCompletionSource<bool>();
            Action done = null;
            done = () => { p.MapLoaded -= done; t.SetResult(true); };
            p.MapLoaded += done;
            p.SelectMap(stem);
            return t.Task;
        }

        static void Drag(MapCanvas c, Point from, Point to, MouseButtons b = MouseButtons.Left)
        {
            c.TestMouseDown(b, from);
            for (int i = 1; i <= 5; i++)
                c.TestMouseMove(b, new Point(from.X + (to.X - from.X) * i / 5, from.Y + (to.Y - from.Y) * i / 5));
            c.TestMouseUp(b, to);
            Application.DoEvents();
        }

        static Point P(PointF p) => Point.Round(p);

        static async Task Script()
        {
            MapEditorPanel maps = form.Maps;
            MapCanvas c = maps.Canvas;
            form.WindowState = FormWindowState.Normal;
            form.Activate();

            Log("== opening maps");
            await OpenMap(maps, "fore-2");
            Application.DoEvents();
            Check(maps.Session.Entry.Stem.Equals("fore-2", StringComparison.OrdinalIgnoreCase), "fore-2 opened");
            Check(maps.Session.Flags.Count > 0, "fore-2 has flags: " + maps.Session.Flags.Count);
            Shot("01_fore-2");

            await OpenMap(maps, "village-2");
            c.ShowNames = true;
            c.InvalidateLayers();
            Application.DoEvents();
            Shot("02_village-2_names");
            c.ShowNames = false;

            Log("== real Windows messages");
            await OpenMap(maps, "dun-1");
            Pump();
            c.Focus();
            SpawnFlag wm = maps.Session.Flags[2];
            int wx0 = wm.X, wz0 = wm.Z;
            Point wmAt = P(c.ToScreen(wm.X, wm.Z));
            PostDrag(c, wmAt, new Point(wmAt.X + 30, wmAt.Y));
            wm = maps.Session.Flags.First(x => x.Slot == 2);
            // while the mouse is captured Windows also reports the real cursor, so the end point varies: only the move itself is checked
            Check(wm.X != wx0 || wm.Z != wz0, $"a drag sent as mouse messages moved the flag: {wx0},{wz0} -> {wm.X},{wm.Z}");
            int wx1 = wm.X;
            PostKey(c, Keys.Right);
            wm = maps.Session.Flags.First(x => x.Slot == 2);
            Check(wm.X == wx1 + 10, "the Right arrow key nudged it by 10");
            int before0 = maps.Session.Flags.Count;
            PostKey(c, Keys.Delete);
            Check(maps.Session.Flags.Count == before0 - 1, "the Delete key deleted it");
            PostKey(c, Keys.Escape);
            // the real cursor can add a move while the mouse is captured, so undo however many steps there are
            while (maps.Session.CanUndo) maps.Session.Undo();
            Check(!maps.HasUnsaved, "undoing everything leaves nothing to save");
            NpcRecord qn = maps.Session.Npcs.FirstOrDefault();
            if (qn == null)
            {
                await OpenMap(maps, "fore-2");
                Pump();
                c.Focus();
                qn = maps.Session.Npcs.First();
            }
            Point qAt = P(c.ToScreen(qn.X, qn.Z));
            PostDrag(c, qAt, qAt);
            double qd = qn.Degrees;
            Check(c.SelNpcs.Contains(qn.Slot), "a click sent as mouse messages selected an NPC");
            PostKey(c, Keys.E);
            qn = maps.Session.Npcs.First(x => x.Slot == qn.Slot);
            Check(Math.Abs(((qn.Degrees - qd + 360) % 360) - 5) < 0.2, "the E key turned it by 5 degrees");
            while (maps.Session.CanUndo) maps.Session.Undo();
            Check(!maps.HasUnsaved, "undo leaves nothing to save");

            Log("== moving a flag by dragging");
            await OpenMap(maps, "de-4");
            SpawnFlag f = maps.Session.Flags[5];
            int fx = f.X, fz = f.Z;
            Point at = P(c.ToScreen(f.X, f.Z));
            Drag(c, at, new Point(at.X + 40, at.Y - 30));
            f = maps.Session.Flags.First(x => x.Slot == 5);
            Check(f.X > fx && f.Z > fz, $"flag moved right and up: {fx},{fz} -> {f.X},{f.Z}");
            Check(c.SelFlags.Contains(5) && c.SelFlags.Count == 1, "dragged flag is selected");
            Check(maps.HasUnsaved, "map shows unsaved");
            c.TestKey(Keys.Z | Keys.Control);
            Log("  note: Ctrl+Z routed to the map view");
            f = maps.Session.Flags.First(x => x.Slot == 5);
            Check(f.X == fx && f.Z == fz, "Ctrl+Z puts it back");
            Check(!maps.HasUnsaved, "clean again after undo");
            c.TestKey(Keys.Y | Keys.Control);
            maps.SaveCurrent();
            SpawnFlag disk = SppFile.Load(maps.Session.Entry.SppPath).Flags.First(x => x.Slot == 5);
            Check(disk.X != fx && disk.Z != fz, "Save wrote the new position to the .spp");
            Check(!maps.HasUnsaved, "clean after save");
            Shot("03_de-4_flag_moved");

            Log("== box select, delete, undo");
            int before = maps.Session.Flags.Count;
            Drag(c, new Point(30, 30), new Point(c.Width / 2, c.Height / 2));
            int selected = c.SelFlags.Count;
            Check(selected > 0, "box selected " + selected + " flags");
            Shot("04_box_select");
            c.TestKey(Keys.Delete);
            Check(maps.Session.Flags.Count == before - selected, "Del removed them");
            c.TestKey(Keys.Z | Keys.Control);
            Check(maps.Session.Flags.Count == before, "undo restored them");
            maps.Session.UpdateDirty();

            Log("== adding a flag with the tool");
            await OpenMap(maps, "fore-2");
            int nf = maps.Session.Flags.Count;
            maps.SetTool(CanvasTool.AddFlag);
            Point spot = new Point(c.Width / 2 + 60, c.Height / 2 + 40);
            c.ToWorld(spot, out double wx, out double wz);
            c.TestMouseDown(MouseButtons.Left, spot);
            c.TestMouseUp(MouseButtons.Left, spot);
            Check(maps.Session.Flags.Count == nf + 1, "clicking placed a flag");
            SpawnFlag added = maps.Session.Flags.First(x => c.SelFlags.Contains(x.Slot));
            Check(Math.Abs(added.X - wx) < 2 && Math.Abs(added.Z - wz) < 2, "at the clicked spot");
            maps.SetTool(CanvasTool.Select);
            maps.SaveCurrent();
            Check(SppFile.Load(maps.Session.Entry.SppPath).Flags.Count == nf + 1, "saved");
            c.TestKey(Keys.Delete);
            maps.SaveCurrent();
            Check(SppFile.Load(maps.Session.Entry.SppPath).Flags.Count == nf, "delete and save removes it from disk");

            Log("== NPC: move, turn with the handle, add, delete");
            await OpenMap(maps, "village-2");
            NpcRecord n = maps.Session.Npcs.First(x => x.Active);
            int slot = n.Slot;
            double oldX = n.X, oldY = n.Y;
            Point np = P(c.ToScreen(n.X, n.Z));
            c.TestMouseDown(MouseButtons.Left, np);
            c.TestMouseUp(MouseButtons.Left, np);
            Check(c.SelNpcs.Contains(slot), "clicking an NPC selects it");
            Application.DoEvents();
            Shot("05_npc_selected");
            // grab the rotation handle and swing it round to due east (screen right)
            double a0 = n.Degrees * Math.PI / 180;
            Point handle = new Point(np.X + (int)Math.Round(Math.Sin(a0) * 46), np.Y - (int)Math.Round(Math.Cos(a0) * 46));
            Drag(c, handle, new Point(np.X + 46, np.Y));
            n = maps.Session.Npcs.First(x => x.Slot == slot);
            Check(Math.Abs(n.Degrees - 90) < 1.5, "dragging the handle to the right makes it face 90 degrees: " + n.Degrees.ToString("0.0"));
            Check(n.X == oldX, "turning didn't move it");
            Drag(c, np, new Point(np.X + 25, np.Y + 25));
            n = maps.Session.Npcs.First(x => x.Slot == slot);
            Check(n.X > oldX, "dragging moved it");
            double? ground = maps.Session.Mesh.GroundAt(n.X, n.Z);
            Check(ground.HasValue && Math.Abs(n.Y - ground.Value) < 0.01, "and set its height to the ground: " + n.Y.ToString("0.0"));
            maps.SaveCurrent();
            NpcRecord dn = SpcFile.Load(maps.Session.Entry.SpcPath).Npcs.First(x => x.Slot == slot);
            Check(Math.Abs(dn.Degrees - n.Degrees) < 0.01 && Math.Abs(dn.X - n.X) < 0.01, "saved to the .spc");
            int nc = maps.Session.Npcs.Count;
            DataItem pick = form.Data.Npcs.First(x => x.FileName.StartsWith("ricarden-store", StringComparison.OrdinalIgnoreCase));
            NpcRecord made = maps.AddNpcFromFile(pick, n.X + 300, n.Z, 180);
            Check(made != null && maps.Session.Npcs.Count == nc + 1, "added an NPC from a .npc file: " + made?.Name);
            Check(Math.Abs(made.Degrees - 180) < 0.1, "new NPC faces 180");
            maps.SaveCurrent();
            NpcRecord dm = SpcFile.Load(maps.Session.Entry.SpcPath).Npcs.First(x => x.Slot == made.Slot);
            Check(dm.Name == made.Name && dm.NpcFile == made.NpcFile, "new NPC saved");
            Shot("06_npc_added");
            c.TestKey(Keys.Delete);
            maps.SaveCurrent();
            Check(SpcFile.Load(maps.Session.Entry.SpcPath).Npcs.Count == nc, "delete and save removes it");

            Log("== moved copies");
            MapEntry of1 = form.Cat.Find("o_fore-1");
            if (of1 != null)
            {
                await OpenMap(maps, "o_fore-1");
                Application.DoEvents();
                Check(maps.Session.Flags.Count == 0, "o_fore-1 has no flags yet");
                Check(of1.MinimapPath != null, "o_fore-1 has its minimap");
                Shot("07_o_fore-1_empty");
                TwinInfo t = of1.Twins.First();
                MapEntry src = form.Cat.Find(t.Stem);
                int count = maps.DoImport(src, t.DX, t.DZ, true, true, true, true);
                Check(count > 0, "imported " + count + " flags and NPCs from " + t.Stem);
                int outside = maps.Session.Flags.Count(x => Catalog.Outside(of1, x.X, x.Z));
                Check(outside == 0, "all imported flags land on the moved map, outside: " + outside);
                Application.DoEvents();
                Shot("08_o_fore-1_imported");
                maps.SaveCurrent();
                Check(of1.SppPath != null && File.Exists(of1.SppPath), "created " + Path.GetFileName(of1.SppPath));
                Check(File.Exists(of1.SpmPath), "created " + Path.GetFileName(of1.SpmPath));
                Check(new FileInfo(of1.SppPath).Length == 2400, "the new .spp is the standard size");
                form.Cat.RefreshCounts(of1);
                Check(of1.FlagCount + of1.FlagsOff > 0 && of1.OutsideCount == 0, "catalog counts them: " + (of1.FlagCount + of1.FlagsOff));

                await OpenMap(maps, "o_village-2");
                Application.DoEvents();
                Shot("09_o_village-2");
            }

            Log("== dialogs");
            using (var picker = new NpcPickerDialog(form.Data.Npcs))
            {
                Check(picker.Chosen == null || picker.Chosen.FileName != null, "NPC picker opens");
                ShotOf(picker, "20_dialog_npc_picker");
            }
            if (form.Cat.Find("o_fore-3") != null)
            {
                var imp = new ImportDialog(form.Cat, form.Cat.Find("o_fore-3"));
                Check(imp.Chosen != null && imp.Chosen.Stem.Equals("fore-3", StringComparison.OrdinalIgnoreCase), "import offers the map's original first");
                Check(imp.DX.Value == -131072 && imp.DZ.Value == 0, "with the detected offset already filled in");
                ShotOf(imp, "21_dialog_import");
            }
            ShotOf(new ShiftDialog(65536, -4096, "Most of this map's flags and NPCs are off the map. These amounts would bring them back: the nearest round move to the map's middle."), "22_dialog_shift");

            Log("== moved map with old coordinates");
            await OpenMap(maps, "ice_2");
            Application.DoEvents();
            Shot("10_ice_2");

            Log("== data tabs");
            SelectTab(form.Items);
            await WaitForList(form.Items);
            form.Items.SelectByFile("2_WA108.txt");
            Application.DoEvents();
            DataGridViewRow dur = form.Items.Grid.Rows.Cast<DataGridViewRow>().First(r => (string)r.Cells["key"].Value == "*내구력");
            Check(((string)dur.Cells["value"].Value).Contains(" "), "values separated by tabs read as separate: " + dur.Cells["value"].Value);
            form.Items.Grid.CurrentCell = dur.Cells["value"];
            form.Items.Grid.BeginEdit(false);
            form.Items.Grid.EndEdit();
            Check(!form.Items.HasUnsaved, "clicking through a cell changes nothing");
            SelectTab(form.Monsters);
            await WaitForList(form.Monsters);
            Check(form.Monsters.SelectByFile("100_Fury.inf"), "monster file found");
            Application.DoEvents();
            Shot("11_monsters");
            DataGridView g = form.Monsters.Grid;
            Check(g.Rows.Count > 30, "monster file shows labelled rows: " + g.Rows.Count);
            DataGridViewRow lv = g.Rows.Cast<DataGridViewRow>().First(r => (string)r.Cells["key"].Value == "*레벨");
            Check((string)lv.Cells["label"].Value == "Level", "keys have English labels");
            string oldLevel = (string)lv.Cells["value"].Value;
            g.CurrentCell = lv.Cells["value"];
            g.BeginEdit(false);
            g.CurrentCell.Value = "123";
            g.EndEdit();
            form.Monsters.SaveCurrent();
            Check(TextDoc.Load(form.Monsters.Doc.Path).First("*레벨") == "123", "editing a field and saving changes the file");

            SelectTab(form.Npcs);
            await WaitForList(form.Npcs);
            form.Npcs.SelectByFile("ricarden-equip1.NPC");
            Application.DoEvents();
            Shot("12_npcs");

            SelectTab(form.Items);
            await WaitForList(form.Items);
            form.Items.SelectByFile("2_WA108.txt");
            Application.DoEvents();
            Shot("13_items");

            SelectTab(form.Names);
            await WaitForList(form.Names);
            Application.DoEvents();
            Shot("14_names");

            SelectTab(form.TextFiles);
            form.TextFiles.ShowFile(Path.Combine(form.ServerRoot, "hotuk.ini"));
            Application.DoEvents();
            Shot("15_text_hotuk");
            form.TextFiles.ShowFile(Path.Combine(form.ServerRoot, "GameServer", "Field", "De-4.ase.spm"));
            Application.DoEvents();
            Shot("16_text_spm");

            SelectTab(form.Maps);
            await OpenMap(maps, "De-4");
            Application.DoEvents();
            var tabs = FindSide(maps);
            tabs.SelectedIndex = 2;
            Application.DoEvents();
            Shot("17_monster_list");
            tabs.SelectedIndex = 3;
            Application.DoEvents();
            Shot("18_map_tab");
            if (form.Cat.Find("o_fore-2") != null)
            {
                await OpenMap(maps, "o_fore-2");
                Application.DoEvents();
                Shot("19_o_fore-2_maptab");
            }
        }

        static TabControl FindSide(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is TabControl t && t.TabPages.Count == 4 && t.TabPages[0].Text == "Flags") return t;
                TabControl r = FindSide(c);
                if (r != null) return r;
            }
            return null;
        }

        static void SelectTab(Control panel)
        {
            foreach (TabPage t in form.Tabs.TabPages)
                if (t.Controls[0] == panel) form.Tabs.SelectedTab = t;
            Application.DoEvents();
        }

        static async Task WaitForList(DataFilePanel p)
        {
            p.EnsureLoaded();
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                if (p.Controls.Count > 0 && FindList(p)?.Items.Count > 0) return;
            }
        }

        static ListView FindList(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ListView l) return l;
                ListView r = FindList(c);
                if (r != null) return r;
            }
            return null;
        }
    }
}
