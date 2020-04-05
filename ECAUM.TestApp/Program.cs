using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ECAUM;
using Microsoft.Extensions.Logging;
using NLog;

namespace ECAUM.TestApp
{
    class Program
    {
        private static readonly ManualResetEvent shutdown = new ManualResetEvent(false);
        private static int errorThroughEvent = 0;
        private static ILoggerFactory loggerFactory;

        static int Main(string[] args) // args= updateUri, enableLogging, [updateDir]
        {
            if (args.Length < 2) { return (1337); }
            if (args.Length >= 3)
                loggerFactory = ConfigureLogging(args[2]);
            else
                loggerFactory = ConfigureLogging(Path.Combine(GetBasePath(), "update"));

            try
            {
                int CurrentPercentage = -1;
                UpdateManager manager = new UpdateManager(new Uri(args[0]), args.Length >= 3 ? args[2] : null, bool.Parse(args[1]) ? loggerFactory.CreateLogger<UpdateManager>() : null);
                manager.DownloadProgressChanged += (p) =>
                {
                    if (p <= CurrentPercentage)
                    {
                        errorThroughEvent = (9); // invalid percentage reported
                        shutdown.Set();
                    }
                    CurrentPercentage = p;
                };
                manager.DownloadUpdateFinished += (s) =>
                {
                    if (s != UpdateState.UpdateReady)
                    {
                        errorThroughEvent = (20 + StateToExitCode(s));
                        shutdown.Set();
                    }

                    if ((s = manager.InstallUpdate()) != UpdateState.UpdaterStarted)
                    {
                        errorThroughEvent = (30 + StateToExitCode(s));
                        shutdown.Set();
                    }

                    // shutdown gracefull
                    shutdown.Set();
                };

                UpdateState state;
                if ((state = manager.CheckUpdate()) != UpdateState.UpdateAvailable)
                {
                    return (StateToExitCode(state));
                }

                if ((state = manager.DownloadUpdateAsync()) != UpdateState.DownloadInProgress)
                {
                    return (10 + StateToExitCode(state));
                }

                shutdown.WaitOne();
                return errorThroughEvent; // is 0 if no error
            }
            catch (Exception)
            {
                return 666;
            }
        }
        private static string GetBasePath() // Appdomain.BaseDirectory does not work with single file executables
        {
            using var processModule = Process.GetCurrentProcess().MainModule;
            return Path.GetDirectoryName(processModule?.FileName);
        }

        private static ILoggerFactory ConfigureLogging(string logDirectory)
        {
            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile")
            {
                FileName = Path.Combine(logDirectory, "UpdateLog.txt"),
                Layout = "${longdate} [${level:uppercase=true}] ${logger}: ${message} ${exception}"
            };
            config.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, logfile);
            LogManager.Configuration = config;

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new NLog.Extensions.Logging.NLogLoggerProvider());
            return loggerFactory;
        }

        private static int StateToExitCode(UpdateState state)
        {
            return state switch
            {
                UpdateState.NoUpdateAvailable => 2,
                UpdateState.UpdateAvailable => 3,
                UpdateState.DownloadInProgress => 4,
                UpdateState.UpdateReady => 5,
                UpdateState.UpdaterStarted => 6,
                UpdateState.Error => 7,
                UpdateState.UnknownPlatform => 8,
                _ => 1,// unknown state
            };
        }
    }
}
