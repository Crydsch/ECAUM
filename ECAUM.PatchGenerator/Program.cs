using NLog;
using System;
using System.IO;
using System.Reflection;

namespace ECAUM.PatchGenerator
{
    class Program
    {
        private static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        private static Logger Log;

        static void Main(string[] args)
        {
            // Configure Logger
            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile")
            {
                FileName = Path.Combine(BaseDirectory, "GeneratorLog.txt"),
                Layout = "${longdate} [${level:uppercase=true}] ${logger}: ${message} ${exception}"
            };
            var logconsole = new NLog.Targets.ConsoleTarget()
            {
                Layout = "[${level:uppercase=true}] ${message}"
            };
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logfile);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
            LogManager.Configuration = config;
            Log = LogManager.GetCurrentClassLogger();
            Log.Debug("---------- PatchGenerator ----------");
            Log.Info($"Patch Generator v{Assembly.GetExecutingAssembly().GetName().Version}");


            // Check input
            if (args.Length < 3)
            {
                Log.Error("Not enough arguments");
                PrintUsage();
                return;
            }
            if (!Directory.Exists(args[1]))
            {
                Log.Error("AppDirectory does not exist");
                return;
            }
            if (!File.Exists(Path.Combine(args[1], args[0])))
            {
                Log.Error($"AssemblyFile does not exist: {Path.Combine(args[1], args[0])}");
                return;
            }

            // prepare directories
            Directory.CreateDirectory(args[2]);
            Directory.CreateDirectory(args[3]);

            // start generator
            Generator generator = new Generator();
            if (args.Length >= 4)
            {
                Directory.CreateDirectory(args[3]);
                generator.Start(args[0], args[1], args[2], args[3]);
            }
            else
            {
                generator.Start(args[0], args[1], args[2], BaseDirectory);
            }

        }

        private static void PrintUsage()
        {
            Log.Info("Usage: PatchGenerator AssemblyFileName AppDirectory OutputDirectory [WorkingDirectory]");
            Log.Info("AssemblyFileName  :: Specifies the assembly with the new AssemblyVersion");
            Log.Info("AppDirectory      :: Specifies the directory of the new version of the app");
            Log.Info("OutputDirectory   :: Specifies the directory where the output files are generated (PatchFile + FullAppArchive)");
            Log.Info("                      The update.json is expected in this directory and will be updated");
            Log.Info("WorkingDirectory  :: (Optional) Specifies the directory where the generator will store intermediate files");
            Log.Info("                      Must be the same directory between patches! If not specifed the generator will use its app directory");
        }
    }

}

