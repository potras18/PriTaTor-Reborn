// Everything the tool writes for itself lives in one folder in the server folder:
//   PriTaTor\PriTaTor.ini       settings
//   PriTaTor\Backups\           a copy of each file before it's overwritten
//   PriTaTor\Terrain\           terrain pictures
//   PriTaTor\NotMaps.txt        .smd files already known not to be maps
//   PriTaTor\errors.log         only if something went wrong
// Versions before 3.2.1 kept the settings, backups and log loose in the server folder, so those get moved in.

using System;
using System.IO;

namespace PriTaTor.Core
{
    public static class ToolFolder
    {
        public const string Name = "PriTaTor";
        public const string OldBackupsName = "PriTaTor_Backups";
        const string OldLogName = "PriTaTor_errors.log";

        public static string Dir(string root) => Path.Combine(root, Name);
        public static string SettingsFile(string root) => Path.Combine(Dir(root), "PriTaTor.ini");
        public static string BackupsFolder(string root) => Path.Combine(Dir(root), "Backups");
        public static string ErrorLog(string root) => Path.Combine(Dir(root), "errors.log");
        static string OldSettingsFile(string root) => Path.Combine(root, "PriTaTor.ini");

        /// <summary>The settings to read. The old place only counts if they couldn't be moved, such as on a read-only server.</summary>
        public static string SettingsToRead(string root)
        {
            string now = SettingsFile(root);
            return File.Exists(now) || !File.Exists(OldSettingsFile(root)) ? now : OldSettingsFile(root);
        }

        /// <summary>Writing the log must never cause a second failure.</summary>
        public static void LogError(string root, string text)
        {
            try
            {
                Directory.CreateDirectory(Dir(root));
                File.AppendAllText(ErrorLog(root), DateTime.Now + "\r\n" + text + "\r\n\r\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Moves files left by an older version. Anything that can't move stays where it is, and nothing is overwritten.</summary>
        public static void MoveOldFiles(string root)
        {
            string oldIni = OldSettingsFile(root), oldBackups = Path.Combine(root, OldBackupsName), oldLog = Path.Combine(root, OldLogName);
            if (!File.Exists(oldIni) && !Directory.Exists(oldBackups) && !File.Exists(oldLog)) return;
            try
            {
                Directory.CreateDirectory(Dir(root));
                if (File.Exists(oldIni))
                {
                    // when both exist the old one is out of date, since this version only writes the new one
                    if (File.Exists(SettingsFile(root))) File.Delete(oldIni);
                    else File.Move(oldIni, SettingsFile(root));
                }
                if (File.Exists(oldLog))
                {
                    if (File.Exists(ErrorLog(root)))
                    {
                        File.AppendAllText(ErrorLog(root), File.ReadAllText(oldLog));
                        File.Delete(oldLog);
                    }
                    else File.Move(oldLog, ErrorLog(root));
                }
                if (Directory.Exists(oldBackups))
                {
                    Directory.CreateDirectory(BackupsFolder(root));
                    MergeFolder(oldBackups, BackupsFolder(root));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static void MergeFolder(string from, string to)
        {
            foreach (string d in Directory.GetDirectories(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(d));
                if (!Directory.Exists(dest)) Directory.Move(d, dest);
                else MergeFolder(d, dest);
            }
            foreach (string f in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(f));
                // two backups made in the same second: both are kept
                if (File.Exists(dest)) dest += ".moved";
                if (!File.Exists(dest)) File.Move(f, dest);
            }
            if (Directory.GetFileSystemEntries(from).Length == 0) Directory.Delete(from);
        }
    }
}
