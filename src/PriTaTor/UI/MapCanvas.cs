// The map view: pan, zoom, select, drag flags and NPCs, turn NPCs with the handle.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using PriTaTor.Core;

namespace PriTaTor.UI
{
    public enum CanvasTool { Select, AddFlag, AddNpc, AlignMinimap }

    public sealed class MapCanvas : Control
    {
        public MapSession Session;
        public Bitmap Terrain, Minimap;

        public bool ShowTerrain = true, ShowMinimap = true, ShowFlags = true, ShowNpcs = true, ShowNames;
        public float MinimapOpacity = 0.85f;
        public CanvasTool Tool = CanvasTool.Select;
        public readonly HashSet<int> SelFlags = new HashSet<int>(), SelNpcs = new HashSet<int>();
        public Func<NpcRecord, string> NpcLabel = n => n.Name;

        public event Action SelectionChangedByUser;
        public event Action EditCommitted;
        public event Action<double, double> CursorWorld;
        public event Action<double, double> RequestAddNpc;
        public event Action<double, double> RequestAddFlag;
        public event Action<Point, double, double> RequestMenu;
        public event Action MinimapMoved;

        double zoom = 0.05, cx, cz;
        Bitmap baseBuf;
        bool baseDirty = true;

        enum Drag { None, Pan, MoveItems, Rotate, Rubber, Minimap }
        Drag drag;
        Point downAt, lastAt;
        bool moved, rightDown;
        double downWorldX, downWorldZ;
        Dictionary<SpawnFlag, Tuple<int, int>> startFlags;
        Dictionary<NpcRecord, Tuple<int, int>> startNpcs;
        double startDX, startDZ;
        NpcRecord rotating;
        Rectangle rubber;

        const int HandleDist = 46, HitRadius = 10;

        static readonly Color BackColorDark = Color.FromArgb(20, 23, 28);
        static readonly Color FlagOn = Color.FromArgb(235, 64, 52), FlagOff = Color.FromArgb(150, 150, 150), NpcOn = Color.FromArgb(70, 165, 255), NpcOff = Color.FromArgb(120, 130, 145), Selected = Color.FromArgb(255, 214, 10), Warn = Color.FromArgb(255, 140, 0);

        public MapCanvas()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            BackColor = BackColorDark;
            TabStop = true;
            Font = new Font("Segoe UI", 8.5f);
        }

        // ----- geometry -----

        public void WorldBounds(out double minX, out double maxX, out double minZ, out double maxZ)
        {
            MapEntry e = Session?.Entry;
            if (e != null && e.HasGeometry) { minX = e.MinX; maxX = e.MaxX; minZ = e.MinZ; maxZ = e.MaxZ; return; }
            minX = minZ = double.MaxValue; maxX = maxZ = double.MinValue;
            if (Session != null)
            {
                foreach (SpawnFlag f in Session.Flags) { minX = Math.Min(minX, f.X); maxX = Math.Max(maxX, f.X); minZ = Math.Min(minZ, f.Z); maxZ = Math.Max(maxZ, f.Z); }
                foreach (NpcRecord n in Session.Npcs) { minX = Math.Min(minX, n.X); maxX = Math.Max(maxX, n.X); minZ = Math.Min(minZ, n.Z); maxZ = Math.Max(maxZ, n.Z); }
            }
            if (minX > maxX) { minX = -1000; maxX = 1000; minZ = -1000; maxZ = 1000; }
            minX -= 200; maxX += 200; minZ -= 200; maxZ += 200;
        }

        public PointF ToScreen(double x, double z) => new PointF((float)(Width / 2.0 + (x - cx) * zoom), (float)(Height / 2.0 - (z - cz) * zoom));
        public void ToWorld(Point p, out double x, out double z) { x = cx + (p.X - Width / 2.0) / zoom; z = cz - (p.Y - Height / 2.0) / zoom; }

        public void FitAll()
        {
            WorldBounds(out double a, out double b, out double c, out double d);
            cx = (a + b) / 2; cz = (c + d) / 2;
            double w = Math.Max(10, b - a), h = Math.Max(10, d - c);
            zoom = Math.Min(Math.Max(50, Width - 40) / w, Math.Max(50, Height - 40) / h);
            Invalidate2();
        }

        public void CenterOn(double x, double z)
        {
            cx = x; cz = z;
            Invalidate2();
        }

        public void InvalidateLayers() => Invalidate2();
        void Invalidate2() { baseDirty = true; Invalidate(); }

        // ----- drawing -----

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            baseBuf?.Dispose();
            baseBuf = null;
            baseDirty = true;
        }

        void DrawWorldImage(Graphics g, Bitmap img, double minX, double maxX, double minZ, double maxZ, ImageAttributes attrs)
        {
            PointF tl = ToScreen(minX, maxZ), br = ToScreen(maxX, minZ);
            float w = br.X - tl.X, h = br.Y - tl.Y;
            if (w <= 0 || h <= 0) return;
            float x0 = Math.Max(0, tl.X), y0 = Math.Max(0, tl.Y), x1 = Math.Min(Width, br.X), y1 = Math.Min(Height, br.Y);
            if (x1 <= x0 || y1 <= y0) return;
            float su = img.Width / w, sv = img.Height / h;
            var dest = new[] { new PointF(x0, y0), new PointF(x1, y0), new PointF(x0, y1) };
            var src = new RectangleF((x0 - tl.X) * su, (y0 - tl.Y) * sv, (x1 - x0) * su, (y1 - y0) * sv);
            g.DrawImage(img, dest, src, GraphicsUnit.Pixel, attrs);
        }

        void RenderBase()
        {
            if (Width < 1 || Height < 1) return;
            if (baseBuf == null) baseBuf = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(baseBuf))
            {
                g.Clear(BackColorDark);
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.InterpolationMode = zoom > 0.25 ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
                MapEntry e = Session?.Entry;
                if (e != null && e.HasGeometry)
                {
                    if (ShowTerrain && Terrain != null) DrawWorldImage(g, Terrain, e.MinX, e.MaxX, e.MinZ, e.MaxZ, null);
                    if (ShowMinimap && Minimap != null)
                    {
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        var cm = new ColorMatrix { Matrix33 = MinimapOpacity };
                        using (var attrs = new ImageAttributes())
                        {
                            attrs.SetColorMatrix(cm);
                            DrawWorldImage(g, Minimap, e.MinX + e.MinimapDX, e.MaxX + e.MinimapDX, e.MinZ + e.MinimapDZ, e.MaxZ + e.MinimapDZ, attrs);
                        }
                    }
                    PointF a = ToScreen(e.MinX, e.MaxZ), b = ToScreen(e.MaxX, e.MinZ);
                    using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255)) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(pen, a.X, a.Y, b.X - a.X, b.Y - a.Y);
                }
            }
            baseDirty = false;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            if (baseDirty || baseBuf == null) RenderBase();
            Graphics g = pe.Graphics;
            if (baseBuf != null) g.DrawImageUnscaled(baseBuf, 0, 0);
            if (Session == null)
            {
                TextRenderer.DrawText(g, "Pick a map on the left", Font, ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool labels = zoom > 0.06;
            MapEntry e = Session.Entry;
            if (ShowFlags)
                foreach (SpawnFlag f in Session.Flags)
                {
                    PointF p = ToScreen(f.X, f.Z);
                    if (p.X < -20 || p.Y < -20 || p.X > Width + 20 || p.Y > Height + 20) continue;
                    DrawFlag(g, p, f, labels, e);
                }
            if (ShowNpcs)
                foreach (NpcRecord n in Session.Npcs)
                {
                    PointF p = ToScreen(n.X, n.Z);
                    if (p.X < -60 || p.Y < -60 || p.X > Width + 60 || p.Y > Height + 60) continue;
                    DrawNpc(g, p, n, e);
                }
            if (drag == Drag.Rubber && rubber.Width > 0)
                using (var pen = new Pen(Selected) { DashStyle = DashStyle.Dash })
                using (var br = new SolidBrush(Color.FromArgb(40, Selected)))
                {
                    g.FillRectangle(br, rubber);
                    g.DrawRectangle(pen, rubber);
                }
            DrawScaleBar(g);
            if (Tool != CanvasTool.Select)
            {
                string msg = Tool == CanvasTool.AddFlag ? "Click to place a monster flag. Esc to stop."
                    : Tool == CanvasTool.AddNpc ? "Click to place an NPC. Esc to stop."
                    : "Drag to line the minimap up with the terrain. Esc to finish.";
                using (var b = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                {
                    SizeF sz = g.MeasureString(msg, Font);
                    g.FillRectangle(b, 8, 8, sz.Width + 10, sz.Height + 6);
                    g.DrawString(msg, Font, Brushes.White, 13, 11);
                }
            }
        }

        void DrawFlag(Graphics g, PointF p, SpawnFlag f, bool labels, MapEntry e)
        {
            bool sel = SelFlags.Contains(f.Slot);
            bool outside = e.HasGeometry && Catalog.Outside(e, f.X, f.Z);
            float r = sel ? 6f : 4.5f;
            Color c = f.State != 0 ? FlagOn : FlagOff;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, p.X - r, p.Y - r, r * 2, r * 2);
            using (var pen = new Pen(sel ? Selected : outside ? Warn : Color.White, sel || outside ? 2f : 1f)) g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2);
            if (labels || sel) DrawLabel(g, (f.Slot + 1).ToString(), p.X + r + 2, p.Y - 7, sel ? Selected : Color.FromArgb(235, 235, 235));
        }

        void DrawNpc(Graphics g, PointF p, NpcRecord n, MapEntry e)
        {
            bool sel = SelNpcs.Contains(n.Slot);
            bool outside = e.HasGeometry && Catalog.Outside(e, n.X, n.Z);
            float r = sel ? 7f : 5.5f;
            Color c = n.Active ? NpcOn : NpcOff;
            double a = n.Degrees * Math.PI / 180;
            float sx = (float)Math.Sin(a), sy = (float)-Math.Cos(a);
            float len = sel ? HandleDist : 16;
            using (var pen = new Pen(sel ? Selected : Color.White, sel ? 2f : 1.5f))
                g.DrawLine(pen, p.X, p.Y, p.X + sx * len, p.Y + sy * len);
            var diamond = new[] { new PointF(p.X, p.Y - r), new PointF(p.X + r, p.Y), new PointF(p.X, p.Y + r), new PointF(p.X - r, p.Y) };
            using (var b = new SolidBrush(c)) g.FillPolygon(b, diamond);
            using (var pen = new Pen(sel ? Selected : outside ? Warn : Color.White, sel || outside ? 2f : 1f)) g.DrawPolygon(pen, diamond);
            if (sel && SelNpcs.Count == 1 && SelFlags.Count == 0)
            {
                PointF h = new PointF(p.X + sx * HandleDist, p.Y + sy * HandleDist);
                g.FillEllipse(Brushes.White, h.X - 5, h.Y - 5, 10, 10);
                using (var pen = new Pen(Selected, 2f)) g.DrawEllipse(pen, h.X - 5, h.Y - 5, 10, 10);
            }
            if (ShowNames || sel) DrawLabel(g, NpcLabel(n), p.X + r + 3, p.Y - 7, sel ? Selected : Color.FromArgb(200, 228, 255));
        }

        void DrawLabel(Graphics g, string s, float x, float y, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (var sb = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.DrawString(s, Font, sb, x + 1, y + 1);
            using (var b = new SolidBrush(c)) g.DrawString(s, Font, b, x, y);
        }

        void DrawScaleBar(Graphics g)
        {
            double[] options = { 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };
            double target = 120 / zoom, best = options[0];
            foreach (double o in options) if (Math.Abs(o - target) < Math.Abs(best - target)) best = o;
            int px = (int)(best * zoom), y = Height - 14;
            using (var pen = new Pen(Color.White, 2f))
            {
                g.DrawLine(pen, 12, y, 12 + px, y);
                g.DrawLine(pen, 12, y - 4, 12, y + 4);
                g.DrawLine(pen, 12 + px, y - 4, 12 + px, y + 4);
            }
            DrawLabel(g, best + " units", 14, y - 18, Color.White);
        }

        // ----- hit testing -----

        public object HitTest(Point p)
        {
            object best = null;
            double bestD = HitRadius * HitRadius;
            if (ShowNpcs)
                foreach (NpcRecord n in Session.Npcs)
                {
                    PointF s = ToScreen(n.X, n.Z);
                    double d = (s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y);
                    if (d < bestD) { bestD = d; best = n; }
                }
            if (ShowFlags && best == null)
                foreach (SpawnFlag f in Session.Flags)
                {
                    PointF s = ToScreen(f.X, f.Z);
                    double d = (s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y);
                    if (d < bestD) { bestD = d; best = f; }
                }
            return best;
        }

        NpcRecord HitHandle(Point p)
        {
            if (SelNpcs.Count != 1 || SelFlags.Count != 0 || !ShowNpcs) return null;
            NpcRecord n = Session.Npcs.FirstOrDefault(x => x.Slot == SelNpcs.First());
            if (n == null) return null;
            PointF c = ToScreen(n.X, n.Z);
            double a = n.Degrees * Math.PI / 180;
            float hx = c.X + (float)Math.Sin(a) * HandleDist, hy = c.Y - (float)Math.Cos(a) * HandleDist;
            return (hx - p.X) * (hx - p.X) + (hy - p.Y) * (hy - p.Y) <= 81 ? n : null;
        }

        // ----- mouse -----

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (Session == null) return;
            downAt = lastAt = e.Location;
            moved = false;
            ToWorld(e.Location, out downWorldX, out downWorldZ);
            if (e.Button == MouseButtons.Middle) { drag = Drag.Pan; return; }
            if (e.Button == MouseButtons.Right) { drag = Drag.Pan; rightDown = true; return; }
            if (e.Button != MouseButtons.Left) return;

            switch (Tool)
            {
                case CanvasTool.AddFlag: RequestAddFlag?.Invoke(downWorldX, downWorldZ); return;
                case CanvasTool.AddNpc: RequestAddNpc?.Invoke(downWorldX, downWorldZ); return;
                case CanvasTool.AlignMinimap:
                    drag = Drag.Minimap;
                    startDX = Session.Entry.MinimapDX;
                    startDZ = Session.Entry.MinimapDZ;
                    return;
            }

            rotating = HitHandle(e.Location);
            if (rotating != null) { drag = Drag.Rotate; return; }

            object hit = HitTest(e.Location);
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            if (hit == null)
            {
                if (!ctrl) ClearSelection();
                rubber = Rectangle.Empty;
                drag = Drag.Rubber;
                SelectionChangedByUser?.Invoke();
                Invalidate();
                return;
            }
            bool wasSelected = IsSelected(hit);
            if (ctrl)
            {
                Toggle(hit);
                SelectionChangedByUser?.Invoke();
                Invalidate();
                if (!IsSelected(hit)) return;
            }
            else if (!wasSelected)
            {
                ClearSelection();
                Toggle(hit);
                SelectionChangedByUser?.Invoke();
                Invalidate();
            }
            drag = Drag.MoveItems;
            startFlags = Session.Flags.Where(f => SelFlags.Contains(f.Slot)).ToDictionary(f => f, f => Tuple.Create(f.X, f.Z));
            startNpcs = Session.Npcs.Where(n => SelNpcs.Contains(n.Slot)).ToDictionary(n => n, n => Tuple.Create(n.RawX, n.RawZ));
        }

        bool IsSelected(object o) => o is SpawnFlag f ? SelFlags.Contains(f.Slot) : o is NpcRecord n && SelNpcs.Contains(n.Slot);

        void Toggle(object o)
        {
            if (o is SpawnFlag f) { if (!SelFlags.Remove(f.Slot)) SelFlags.Add(f.Slot); }
            else if (o is NpcRecord n) { if (!SelNpcs.Remove(n.Slot)) SelNpcs.Add(n.Slot); }
        }

        public void ClearSelection() { SelFlags.Clear(); SelNpcs.Clear(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Session == null) return;
            ToWorld(e.Location, out double wx, out double wz);
            CursorWorld?.Invoke(wx, wz);
            if (drag == Drag.None) return;
            if (!moved && Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) < 4) return;
            bool first = !moved;
            moved = true;
            switch (drag)
            {
                case Drag.Pan:
                    cx -= (e.X - lastAt.X) / zoom;
                    cz += (e.Y - lastAt.Y) / zoom;
                    Invalidate2();
                    break;
                case Drag.MoveItems:
                    if (first) Session.Checkpoint();
                    double dx = wx - downWorldX, dz = wz - downWorldZ;
                    foreach (var kv in startFlags)
                    {
                        kv.Key.X = (int)Math.Round(kv.Value.Item1 + dx);
                        kv.Key.Z = (int)Math.Round(kv.Value.Item2 + dz);
                    }
                    foreach (var kv in startNpcs)
                    {
                        kv.Key.RawX = kv.Value.Item1 + (int)Math.Round(dx * 256);
                        kv.Key.RawZ = kv.Value.Item2 + (int)Math.Round(dz * 256);
                    }
                    Invalidate();
                    break;
                case Drag.Rotate:
                    if (first) Session.Checkpoint();
                    PointF c = ToScreen(rotating.X, rotating.Z);
                    double deg = Math.Atan2(e.X - c.X, -(e.Y - c.Y)) * 180 / Math.PI;
                    if (deg < 0) deg += 360;
                    if ((ModifierKeys & Keys.Shift) != 0) deg = Math.Round(deg / 15) * 15;
                    rotating.Degrees = deg;
                    Invalidate();
                    break;
                case Drag.Rubber:
                    rubber = Rectangle.FromLTRB(Math.Min(downAt.X, e.X), Math.Min(downAt.Y, e.Y), Math.Max(downAt.X, e.X), Math.Max(downAt.Y, e.Y));
                    Invalidate();
                    break;
                case Drag.Minimap:
                    Session.Entry.ManualDX = Math.Round(startDX + (e.X - downAt.X) / zoom);
                    Session.Entry.ManualDZ = Math.Round(startDZ - (e.Y - downAt.Y) / zoom);
                    Invalidate2();
                    break;
            }
            lastAt = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (Session == null) { drag = Drag.None; return; }
            Drag was = drag;
            drag = Drag.None;
            if (rightDown && e.Button == MouseButtons.Right)
            {
                rightDown = false;
                if (!moved)
                {
                    ToWorld(e.Location, out double wx, out double wz);
                    RequestMenu?.Invoke(e.Location, wx, wz);
                }
                return;
            }
            if (!moved) return;
            switch (was)
            {
                case Drag.MoveItems:
                    foreach (NpcRecord n in startNpcs.Keys) n.Y = Session.GroundOr(n.X, n.Z, n.Y);
                    Session.UpdateDirty();
                    EditCommitted?.Invoke();
                    break;
                case Drag.Rotate:
                    Session.UpdateDirty();
                    EditCommitted?.Invoke();
                    break;
                case Drag.Rubber:
                    if ((ModifierKeys & Keys.Control) == 0) ClearSelection();
                    if (ShowFlags)
                        foreach (SpawnFlag f in Session.Flags)
                            if (rubber.Contains(Point.Round(ToScreen(f.X, f.Z)))) SelFlags.Add(f.Slot);
                    if (ShowNpcs)
                        foreach (NpcRecord n in Session.Npcs)
                            if (rubber.Contains(Point.Round(ToScreen(n.X, n.Z)))) SelNpcs.Add(n.Slot);
                    rubber = Rectangle.Empty;
                    SelectionChangedByUser?.Invoke();
                    break;
                case Drag.Minimap:
                    MinimapMoved?.Invoke();
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Session == null) return;
            ToWorld(e.Location, out double wx, out double wz);
            double factor = e.Delta > 0 ? 1.25 : 0.8;
            zoom = Math.Max(0.002, Math.Min(4, zoom * factor));
            cx = wx - (e.X - Width / 2.0) / zoom;
            cz = wz + (e.Y - Height / 2.0) / zoom;
            Invalidate2();
        }

        protected override bool IsInputKey(Keys k)
        {
            Keys c = k & Keys.KeyCode;
            return c == Keys.Left || c == Keys.Right || c == Keys.Up || c == Keys.Down || base.IsInputKey(k);
        }

        // for automated checks of the real input path
        public void TestMouseDown(MouseButtons b, Point p) => OnMouseDown(new MouseEventArgs(b, 1, p.X, p.Y, 0));
        public void TestMouseMove(MouseButtons b, Point p) => OnMouseMove(new MouseEventArgs(b, 0, p.X, p.Y, 0));
        public void TestMouseUp(MouseButtons b, Point p) => OnMouseUp(new MouseEventArgs(b, 1, p.X, p.Y, 0));
        public void TestKey(Keys k) => OnKeyDown(new KeyEventArgs(k));
        public double Zoom => zoom;
    }
}
