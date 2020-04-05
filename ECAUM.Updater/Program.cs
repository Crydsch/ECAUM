using NLog;
using System;
using System.Diagnostics;
using System.IO;

namespace ECAUM.Updater
{
    class Program
    {
        private static Logger Log;

        static void Main(string[] args)
        {
            ConfigureLogger(GetBasePath());
            Log.Debug($"args={string.Join(",", args)}");

            // arguments <processID> <patchDirectory> <applicationDirectory> [applicationName]
            if (args == null || args.Length < 3)
            {
                Log.Error("Not enough arguments => exiting");
                Environment.Exit(1);
            }

            int applicationProcessID = 0;
            if (args[0] == null || !int.TryParse(args[0], out applicationProcessID))
            {
                Log.Error("First argument: Not a number => exiting");
                Environment.Exit(1);
            }
            if (args[1] == null || !Directory.Exists(args[1]))
            {
                Log.Error("Second argument: Directory does not exists => exiting");
                Environment.Exit(1);
            }
            if (args[2] == null || !Directory.Exists(args[2]))
            {
                Log.Error("Third argument: Directory does not exists => exiting");
                Environment.Exit(1);
            }
            Log.Info($"Arguments: pid={applicationProcessID} patchDir={args[1]} appDir={args[2]} applicationName={(args.Length >= 4 ? args[3] : "none")}");

            // wait for app to exit
            try
            {
                Log.Info("Waiting for app termination...");
                Process App = Process.GetProcessById(applicationProcessID);
                int i;
                for (i = 0; i < 10 && !App.HasExited; i++) // 10 sec max
                {
                    App.WaitForExit(1000); // 1 sec
                }
                if (i == 10)
                {
                    Log.Error("App did not terminate after 10 seconds => exiting");
                    Environment.Exit(1);
                }
            }
            catch (ArgumentException)
            {
                // specified process has already exited
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception while waiting for app termination => exiting");
                Environment.Exit(1);
            }

            Log.Info("... app terminated => applying update");

            // apply update aka copy files
            foreach (string file in Directory.GetFiles(args[1], "*", SearchOption.AllDirectories))
            {
                string relFilePath = Path.GetRelativePath(args[1], file);
                string targetFilePath = Path.Combine(args[2], relFilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath));
                File.Copy(file, targetFilePath, true);
            }
            Log.Info("Update finished");

            // restart application if specified
            if (args.Length >= 4 && args[3] != null && File.Exists(Path.Combine(args[2], args[3])))
            {
                Log.Info("Restarting app");
                Process.Start(Path.Combine(args[2], args[3]));
            }

            // clean patch directory
            Log.Info("Cleaning patch directory");
            Directory.Delete(args[1], true);

            LogManager.Shutdown();
        }

        private static void ConfigureLogger(string logDirectory)
        {
            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile")
            {
                FileName = Path.Combine(logDirectory, "UpdateLog.txt"),
                Layout = "${longdate} [${level:uppercase=true}] ${logger}: ${message} ${exception}"
            };
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logfile);
            LogManager.Configuration = config;
            Log = LogManager.GetCurrentClassLogger();
        }

        private static string GetBasePath() // Appdomain.BaseDirectory does not work with single file executables
        {
            using var processModule = Process.GetCurrentProcess().MainModule;
            return Path.GetDirectoryName(processModule?.FileName);
        }
    }
}
