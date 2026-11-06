using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PriTaTor.Core;
using PriTaTor.UI;

namespace PriTaTor
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception);

            string server = null, field = null, uiTest = null, textures = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--server" && i + 1 < args.Length) server = args[++i];
                else if (args[i] == "--field" && i + 1 < args.Length) field = args[++i];
                else if (args[i] == "--uitest" && i + 1 < args.Length) uiTest = args[++i];
                else if (args[i] == "--textures" && i + 1 < args.Length) textures = args[++i];
                else if (!args[i].StartsWith("--") && Directory.Exists(args[i])) server = args[i];
            }

            if (server == null)
            {
                string here = Path.GetDirectoryName(Application.ExecutablePath);
                if (Catalog.LooksLikeServer(here)) server = here;
            }
            while (server == null || !Catalog.LooksLikeServer(server))
            {
                using (var d = new FolderBrowserDialog { Description = "PriTaTor goes in the game server folder, next to GameServer and Field. Which folder is the server in?" })
                {
                    if (d.ShowDialog() != DialogResult.OK) return 1;
                    server = d.SelectedPath;
                }
                if (!Catalog.LooksLikeServer(server))
                    MessageBox.Show("That folder has no GameServer\\Field folder.", "PriTaTor");
            }

            server = Path.GetFullPath(server);
            SafeWrite.ServerRoot = server;
            // the loading window comes first, so something is on screen straight away
            var load = new LoadingForm(true);
            load.Show();
            load.Update();
            var context = new ApplicationContext();
            var form = new MainForm(server, field, textures);
            if (uiTest != null) UiTest.Attach(form, uiTest, load);
            form.BeginLoad(load, () => context.MainForm = form);
            Application.Run(context);
            return UiTest.ExitCode;
        }

        static void Report(Exception ex)
        {
            if (ex == null) return;
            try
            {
                ToolFolder.LogError(SafeWrite.ServerRoot ?? Path.GetDirectoryName(Application.ExecutablePath), ex.ToString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (UiTest.Running) { UiTest.Fail(ex); return; }
            MessageBox.Show("Something went wrong:\n\n" + ex.Message + "\n\nDetails are in PriTaTor\\errors.log.", "PriTaTor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
