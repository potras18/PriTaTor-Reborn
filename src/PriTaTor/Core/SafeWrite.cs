// All saving goes through here: skip identical data, back up the old file, write atomically.

using System;
using System.IO;
using System.Linq;

namespace PriTaTor.Core
{
    public static class SafeWrite
    {
        public static string ServerRoot;
        public static string BackupFolderName = Path.Combine(ToolFolder.Name, "Backups");

        /// <summary>Returns false when the file already held exactly this data.</summary>
        public static bool Save(string path, byte[] data)
        {
            bool exists = File.Exists(path);
            if (exists)
            {
                byte[] old = File.ReadAllBytes(path);
                if (old.SequenceEqual(data)) return false;
                BackUp(path);
            }
            else
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".pritator_tmp";
            File.WriteAllBytes(tmp, data);
            if (exists)
            {
                FileAttributes attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
                File.Replace(tmp, path, null);
            }
            else
                File.Move(tmp, path);
            return true;
        }

        public static string BackUp(string path)
        {
            string root = ServerRoot ?? Path.GetDirectoryName(path);
            string full = Path.GetFullPath(path), rootFull = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string rel = full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full.Substring(rootFull.Length) : Path.GetFileName(path);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            string dest = Path.Combine(root, BackupFolderName, stamp, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(path, dest, true);
            return dest;
        }

        public static void Delete(string path)
        {
            if (!File.Exists(path)) return;
            BackUp(path);
            File.Delete(path);
        }
    }
}
