using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PriTaTor.Core;

namespace PriTaTor.UI
{
    /// <summary>Choose which .npc file a new NPC uses.</summary>
    public sealed class NpcPickerDialog : Form
    {
        readonly List<DataItem> all;
        readonly ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, FormattingEnabled = true };
        readonly TextBox filter = new TextBox { Dock = DockStyle.Top };
        public DataItem Chosen => list.SelectedItem as DataItem;

        public NpcPickerDialog(List<DataItem> npcs)
        {
            all = npcs;
            Text = "Choose an NPC";
            ClientSize = new Size(520, 480);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            Font = Ui.BaseFont;
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            var ok = new Button { Text = "Place NPC", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(ok);
            Controls.Add(list);
            Controls.Add(filter);
            Controls.Add(new Label { Text = "Search name or file", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2) });
            Controls.Add(bottom);
            AcceptButton = ok;
            CancelButton = cancel;
            filter.TextChanged += (s, e) => Fill();
            list.DoubleClick += (s, e) => { if (Chosen != null) { DialogResult = DialogResult.OK; Close(); } };
            list.Format += (s, e) =>
            {
                if (e.ListItem is DataItem d) e.Value = d.FileName + "   " + d;
            };
            Fill();
        }

        void Fill()
        {
            string q = filter.Text.Trim();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (DataItem d in all)
                if (q.Length == 0 || (d.FileName + " " + d.Name + " " + d.LocalName).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    list.Items.Add(d);
            list.EndUpdate();
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
    }

    /// <summary>Move every flag and NPC on a map by a fixed amount.</summary>
    public sealed class ShiftDialog : Form
    {
        public readonly NumericUpDown DX = Num(), DZ = Num();
        public readonly CheckBox Flags = new CheckBox { Text = "Monster flags", Checked = true, AutoSize = true };
        public readonly CheckBox Npcs = new CheckBox { Text = "NPCs", Checked = true, AutoSize = true };

        static NumericUpDown Num() => new NumericUpDown { Minimum = -1000000, Maximum = 1000000, Width = 110 };

        public ShiftDialog(int suggestX, int suggestZ, string note)
        {
            Text = "Shift everything on this map";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            Font = Ui.BaseFont;
            DX.Value = suggestX;
            DZ.Value = suggestZ;
            var t = Ui.Table(2);
            t.Dock = DockStyle.None;
            t.Padding = new Padding(10);
            if (!string.IsNullOrEmpty(note))
            {
                var l = new Label { Text = note, AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(3, 3, 3, 10) };
                t.Controls.Add(l);
                t.SetColumnSpan(l, 2);
            }
            t.Controls.Add(Ui.Label("Move X by"));
            t.Controls.Add(DX);
            t.Controls.Add(Ui.Label("Move Z by"));
            t.Controls.Add(DZ);
            t.Controls.Add(Flags);
            t.Controls.Add(Npcs);
            var ok = new Button { Text = "Shift", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            t.Controls.Add(ok);
            t.Controls.Add(cancel);
            Controls.Add(t);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    /// <summary>Copy flags, NPCs and the monster list from another map, usually the one this map is a moved copy of.</summary>
    public sealed class ImportDialog : Form
    {
        readonly Catalog cat;
        readonly MapEntry target;
        public readonly ComboBox Source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        public readonly NumericUpDown DX = new NumericUpDown { Minimum = -1000000, Maximum = 1000000, Width = 110 };
        public readonly NumericUpDown DZ = new NumericUpDown { Minimum = -1000000, Maximum = 1000000, Width = 110 };
        public readonly CheckBox Flags = new CheckBox { Text = "Monster flags", Checked = true, AutoSize = true };
        public readonly CheckBox Npcs = new CheckBox { Text = "NPCs", Checked = true, AutoSize = true };
        public readonly CheckBox Monsters = new CheckBox { Text = "Monster list", Checked = true, AutoSize = true };
        public readonly RadioButton Replace = new RadioButton { Text = "Replace what's on this map", Checked = true, AutoSize = true };
        public readonly RadioButton Add = new RadioButton { Text = "Add to what's on this map", AutoSize = true };
        readonly Label preview = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 10, 3, 6) };

        public MapEntry Chosen => Source.SelectedItem as MapEntry;

        public ImportDialog(Catalog cat, MapEntry target)
        {
            this.cat = cat;
            this.target = target;
            Text = "Import onto " + target.Stem;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            Font = Ui.BaseFont;

            var twins = target.Twins.Select(t => cat.Find(t.Stem)).Where(m => m != null && m.HasSpawnData).ToList();
            foreach (MapEntry m in twins) Source.Items.Add(m);
            foreach (MapEntry m in cat.Maps.Where(x => x.HasSpawnData && x != target && !twins.Contains(x))) Source.Items.Add(m);
            Source.SelectedIndexChanged += (s, e) => SourceChanged();
            DX.ValueChanged += (s, e) => UpdatePreview();
            DZ.ValueChanged += (s, e) => UpdatePreview();

            var t2 = Ui.Table(2);
            t2.Dock = DockStyle.None;
            t2.Padding = new Padding(10);
            t2.Controls.Add(Ui.Label("Copy from"));
            t2.Controls.Add(Source);
            t2.Controls.Add(Ui.Label("Move X by"));
            t2.Controls.Add(DX);
            t2.Controls.Add(Ui.Label("Move Z by"));
            t2.Controls.Add(DZ);
            var what = Ui.Row();
            what.Dock = DockStyle.None;
            what.Controls.Add(Flags);
            what.Controls.Add(Npcs);
            what.Controls.Add(Monsters);
            t2.Controls.Add(Ui.Label("Bring over"));
            t2.Controls.Add(what);
            var how = Ui.Row();
            how.Dock = DockStyle.None;
            how.Controls.Add(Replace);
            how.Controls.Add(Add);
            t2.Controls.Add(new Label());
            t2.Controls.Add(how);
            t2.Controls.Add(preview);
            t2.SetColumnSpan(preview, 2);
            var ok = new Button { Text = "Import", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            t2.Controls.Add(ok);
            t2.Controls.Add(cancel);
            Controls.Add(t2);
            AcceptButton = ok;
            CancelButton = cancel;
            if (Source.Items.Count > 0) Source.SelectedIndex = 0;
        }

        void SourceChanged()
        {
            MapEntry s = Chosen;
            if (s == null) return;
            TwinInfo t = target.Twins.FirstOrDefault(x => string.Equals(x.Stem, s.Stem, StringComparison.OrdinalIgnoreCase));
            if (t != null) { DX.Value = t.DX; DZ.Value = t.DZ; }
            else if (s.HasGeometry && target.HasGeometry)
            {
                DX.Value = (decimal)Math.Round((target.MinX + target.MaxX - s.MinX - s.MaxX) / 2);
                DZ.Value = (decimal)Math.Round((target.MinZ + target.MaxZ - s.MinZ - s.MaxZ) / 2);
            }
            UpdatePreview();
        }

        void UpdatePreview()
        {
            MapEntry s = Chosen;
            if (s == null) return;
            int flags = 0, npcs = 0, outside = 0;
            int dx = (int)DX.Value, dz = (int)DZ.Value;
            try
            {
                if (s.SppPath != null)
                    foreach (SpawnFlag f in SppFile.Load(s.SppPath).Flags)
                    {
                        flags++;
                        if (target.HasGeometry && Catalog.Outside(target, f.X + dx, f.Z + dz)) outside++;
                    }
                if (s.SpcPath != null)
                    foreach (NpcRecord n in SpcFile.Load(s.SpcPath).Npcs)
                    {
                        npcs++;
                        if (target.HasGeometry && Catalog.Outside(target, n.X + dx, n.Z + dz)) outside++;
                    }
            }
            catch (IOException) { }
            preview.Text = string.Format("{0} has {1} flags and {2} NPCs. At this offset {3} would land outside {4}.", s.Stem, flags, npcs, outside, target.Stem);
        }
    }
}
