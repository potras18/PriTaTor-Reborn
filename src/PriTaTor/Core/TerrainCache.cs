// Terrain pictures saved as PNGs in <server>\PriTaTor\Terrain, mirroring the Field folder, so a map only gets
// drawn again when its .smd changes. PriTaTor.ini records which .smd each picture was drawn from.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace PriTaTor.Core
{
    public static class TerrainCache
    {
        public const string FolderName = ToolFolder.Name;
        public const int LongestSide = 2048;
        /// <summary>Raise this when the drawing changes, so saved pictures get redrawn.</summary>
        public const int DrawVersion = 1;

        public static string Root(Catalog c) => Path.Combine(c.ServerRoot, FolderName, "Terrain");
        public static string PathFor(Catalog c, MapEntry m) => Path.Combine(Root(c), Path.ChangeExtension(m.RelPath, ".png"));
        public static string StampFor(MapEntry m) => m.SmdSize + ":" + m.SmdTicks + ":" + DrawVersion;
        public static bool IsCurrent(Catalog c, MapEntry m) => m.TerrainStamp == StampFor(m) && File.Exists(PathFor(c, m));

        /// <summary>Can pictures be saved here? A read-only server still works, it just draws maps as they open.</summary>
        public static bool CanWrite(Catalog c)
        {
            try
            {
                Directory.CreateDirectory(Root(c));
                string probe = Path.Combine(Root(c), ".write_test");
                File.WriteAllBytes(probe, new byte[0]);
                File.Delete(probe);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>Draws a map and saves its picture. Returns false if it couldn't be saved.</summary>
        public static bool Redraw(Catalog c, MapEntry m, StageMesh mesh)
        {
            using (Bitmap b = TerrainRaster.Build(mesh, LongestSide).ToBitmap())
                return Save(c, m, b);
        }

        static bool Save(Catalog c, MapEntry m, Bitmap b)
        {
            string path = PathFor(c, m), tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var rgb = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(rgb))
                        g.DrawImageUnscaled(b, 0, 0);
                    rgb.Save(tmp, ImageFormat.Png);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                m.TerrainStamp = StampFor(m);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.ExternalException)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return false;
            }
        }

        /// <summary>
        /// The picture to show for a map: the saved one when it's current, otherwise drawn now and saved for next time.
        /// <paramref name="savedNew"/> says a new picture was saved, so the settings need saving too.
        /// </summary>
        public static Bitmap Load(Catalog c, MapEntry m, StageMesh mesh, out bool savedNew)
        {
            savedNew = false;
            if (IsCurrent(c, m))
            {
                try
                {
                    using (var fs = File.OpenRead(PathFor(c, m)))
                    using (Image img = Image.FromStream(fs))
                    {
                        // a copy the file isn't tied to, in the format that draws fastest
                        var copy = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(copy))
                            g.DrawImageUnscaled(img, 0, 0);
                        return copy;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException) { }
            }
            Bitmap drawn = TerrainRaster.Build(mesh, LongestSide).ToBitmap();
            savedNew = Save(c, m, drawn);
            return drawn;
        }

        public static void DeleteFor(Catalog c, string relPath)
        {
            try
            {
                string p = Path.Combine(Root(c), Path.ChangeExtension(relPath, ".png"));
                if (File.Exists(p)) File.Delete(p);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
    }
}
