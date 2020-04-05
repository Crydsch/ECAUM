using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using System.Threading;
using Flurl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECAUM
{
    public delegate void DownloadProgressChangedDel(int percentage);
    public delegate void DownloadUpdateFinishedDel(UpdateState state);

    public class UpdateManager
    {
        public event DownloadProgressChangedDel DownloadProgressChanged;
        public event DownloadUpdateFinishedDel DownloadUpdateFinished;
        public UpdateState State { get; private set; }
        public Update Update { get; private set; }

        private readonly ILogger Logger;
        private readonly Version CurrentVersion;
        private readonly Platform CurrentPlatform;
        private readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        private readonly string DownloadDirectory;
        private readonly string PatchDirectory;
        private readonly string UpdaterFilePath;
        private readonly Uri UpdateUri;
        private readonly DeltaApplier DeltaApplier;
        private readonly object Lock = new object();
        private readonly AutoResetEvent DownloadFinished = new AutoResetEvent(false);
        private UpdateInfo UpdateInfo;

        public UpdateManager(Uri updateUri, string updateDirectory = null, ILogger log = null)
        {
            UpdateUri = updateUri;
            Logger = log ?? NullLogger.Instance;
            updateDirectory = updateDirectory ?? Path.Combine(BaseDirectory, "update");
            DownloadDirectory = Path.Combine(updateDirectory, "download");
            PatchDirectory = Path.Combine(updateDirectory, "patch");
            CurrentPlatform = GetPlatform();
            UpdaterFilePath = Path.Combine(updateDirectory, GetPlatformUpdaterFileName(CurrentPlatform));
            CurrentVersion = Assembly.GetEntryAssembly().GetName().Version;
            State = (CurrentPlatform == Platform.unknown) ? UpdateState.UnknownPlatform : UpdateState.NoUpdateAvailable;
            DeltaApplier = new DeltaApplier
            {
                SkipHashCheck = true
            };
        }

        public UpdateState CheckUpdate()
        {
            lock (Lock)
            {
                Logger.LogDebug("CheckUpdate()");
                try
                {
                    if (State == UpdateState.UnknownPlatform) return State; // theres nothing we can do

                    string updateJsonString;
                    using (WebClient client = new WebClient())
                        updateJsonString = client.DownloadString(Url.Combine(UpdateUri.AbsoluteUri, "update.json"));
                    UpdateInfo = UpdateInfo.Deserialize(updateJsonString);

                    // find necessary patches
                    SortedList<Version, string> patches = new SortedList<Version, string>();
                    long updateSize = 0;
                    for (int i = UpdateInfo.PatchTrail.Count - 1; i >= 0 && UpdateInfo.PatchTrail[i].Version > CurrentVersion; i--)
                    {
                        Logger.LogTrace($"add patch {UpdateInfo.PatchTrail[i].Version} with size {UpdateInfo.PatchTrail[i].SizeInBytes}");
                        patches.Add(UpdateInfo.PatchTrail[i].Version, UpdateInfo.PatchTrail[i].Description);
                        updateSize += UpdateInfo.PatchTrail[i].SizeInBytes;
                    }
                    if (FullAppUpdateNecessary(updateSize))
                    {
                        updateSize = UpdateInfo.FullAppArchiveSize;
                        Logger.LogTrace($"FullAppUpdateNecessary - updateSize={updateSize}");
                    }
                    if (updateSize == 0) return State = UpdateState.NoUpdateAvailable;
                    if (NewUpdaterAvailable())
                    {
                        updateSize += UpdateInfo.UpdaterSize;
                        Logger.LogTrace($"NewUpdaterAvailable - updateSize={updateSize}");
                    }
                    Update = new Update(CurrentVersion, updateSize, patches);
                    Logger.LogDebug($"Update: CurrVer={Update.CurrentVersion} updateSize={Update.UpdateSizeInBytes}");
                    return State = UpdateState.UpdateAvailable;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not check update");
                    return State = UpdateState.Error;
                }
            }
        }

        private bool FullAppUpdateNecessary(long patchesSize)
        {
            return patchesSize >= UpdateInfo.FullAppArchiveSize || UpdateInfo.PatchTrail[0].Version > CurrentVersion;
        }

        private bool NewUpdaterAvailable()
        {
            if (!File.Exists(UpdaterFilePath))
                return true;

            // check version
            return new Version(FileVersionInfo.GetVersionInfo(UpdaterFilePath).FileVersion) < UpdateInfo.UpdaterVersion; // File Version is equal to assembly version
        }

        public UpdateState DownloadUpdateAsync()
        {
            lock (Lock)
            {
                Logger.LogDebug("DownloadUpdateAsync()");
                if (State != UpdateState.UpdateAvailable)
                {
                    Logger.LogError($"DownloadUpdateAsync() called, but state == {State.ToString()}");
                    return State;
                }
                ThreadPool.QueueUserWorkItem(new WaitCallback(InternalDownloadAndPrepareUpdate));
                return State = UpdateState.DownloadInProgress;
            }
        }

        private void InternalDownloadAndPrepareUpdate(object _)
        {
            lock (Lock)
            {
                try
                {
                    Logger.LogDebug("InternalDownloadAndPrepareUpdate()");
                    int currentDownloadProgressPercentage = 0;
                    long currentDownloadBytes = 0;
                    if (Directory.Exists(DownloadDirectory))
                        Directory.Delete(DownloadDirectory, true);
                    Directory.CreateDirectory(DownloadDirectory);
                    if (Directory.Exists(PatchDirectory))
                        Directory.Delete(PatchDirectory, true);
                    Directory.CreateDirectory(PatchDirectory);


                    using (WebClient client = new WebClient())
                    {
                        long currentFileBytes = 0;
                        client.DownloadProgressChanged += (s, e) =>
                        {
                            currentFileBytes = e.TotalBytesToReceive;
                            int percentage = (int)(((currentDownloadBytes + e.BytesReceived) * 100) / Update.UpdateSizeInBytes);
                            Logger.LogTrace($"DownloadProgressChanged: {percentage}% currentDownloadBytes={currentDownloadBytes} Update.UpdateSizeInBytes={Update.UpdateSizeInBytes}");
                            percentage = percentage > 100 ? 100 : percentage;
                            if (percentage > currentDownloadProgressPercentage)
                            {
                                currentDownloadProgressPercentage = percentage;
                                DownloadProgressChanged(percentage);
                            }
                        };
                        client.DownloadFileCompleted += (s, e) =>
                        {
                            currentDownloadBytes += currentFileBytes;
                            Logger.LogTrace($"DownloadFileCompleted: error={(e.Error == null ? "false" : "true")}");
                            if (e.Error != null)
                            {
                                Logger.LogError(e.Error, $"Could not download file");
                                State = UpdateState.Error;
                            }
                            DownloadFinished.Set();
                        };

                        // download updater
                        bool patchesSmallerThanFullArchive;
                        if (NewUpdaterAvailable())
                        {
                            Logger.LogDebug("download updater");
                            if (File.Exists(UpdaterFilePath))
                                File.Delete(UpdaterFilePath);
                            client.DownloadFileAsync(new Uri(Url.Combine(UpdateUri.AbsoluteUri, GetPlatformUpdaterFileName(CurrentPlatform))), UpdaterFilePath);
                            DownloadFinished.WaitOne();
                            if (State == UpdateState.Error)
                            { // catch download error
                                DownloadUpdateFinished(State);
                                if (File.Exists(UpdaterFilePath)) // delete erroneous file
                                    File.Delete(UpdaterFilePath);
                                return;
                            }

                            // include updater size
                            patchesSmallerThanFullArchive = Update.UpdateSizeInBytes < UpdateInfo.FullAppArchiveSize + UpdateInfo.UpdaterSize;
                        }
                        else
                        {
                            patchesSmallerThanFullArchive = Update.UpdateSizeInBytes < UpdateInfo.FullAppArchiveSize;
                        }

                        // download update
                        bool patchesApplyCleanly = UpdateInfo.PatchTrail[0].Version <= CurrentVersion;
                        Logger.LogDebug($"patchesSmallerThanFullArchive={patchesSmallerThanFullArchive} patchesApplyCleanly={patchesApplyCleanly}");
                        if (patchesSmallerThanFullArchive && patchesApplyCleanly)
                        { // download patches
                            Logger.LogDebug("download patches");
                            for (int i = 0; i < Update.PatchTrail.Count; i++)
                            {
                                string patchFile = $"patch_{Update.PatchTrail.Keys[i].ToString().Replace('.', '_')}.zip";
                                Logger.LogDebug($"download {patchFile}");
                                string zipFile = Path.Combine(DownloadDirectory, patchFile);
                                client.DownloadFileAsync(new Uri(Url.Combine(UpdateUri.AbsoluteUri, patchFile)), zipFile);
                                DownloadFinished.WaitOne();
                                if (State == UpdateState.Error) { DownloadUpdateFinished(State); return; } // catch download error

                                Logger.LogTrace($"prepare {patchFile}");
                                using (var archive = ZipFile.OpenRead(zipFile))
                                {
                                    foreach (ZipArchiveEntry file in archive.Entries)
                                    {
                                        if (Path.HasExtension(file.Name) && ".delta".Equals(Path.GetExtension(file.Name)))
                                        {
                                            Logger.LogTrace($"apply delta: {file.FullName}");
                                            string baseFilePath = Path.Combine(BaseDirectory, file.FullName.Remove(file.Name.Length - 6));
                                            string filePath = Path.Combine(PatchDirectory, file.FullName.Remove(file.Name.Length - 6));

                                            using (Stream zipStream = file.Open())
                                            {
                                                using (MemoryStream deltaStream = new MemoryStream())
                                                {
                                                    // This is necessary to gain access to Stream.Position and Stream.Length,
                                                    // which are not supported a "zipped" Stream
                                                    zipStream.CopyTo(deltaStream);
                                                    ApplyDelta(baseFilePath, deltaStream, filePath);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            Logger.LogTrace($"copy       : {file.FullName}");
                                            string filePath = Path.Combine(PatchDirectory, file.FullName);
                                            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                                            file.ExtractToFile(filePath, true);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        { // download full archive instead of patches
                            Logger.LogDebug("download full app archive");
                            string zipFile = Path.Combine(DownloadDirectory, UpdateInfo.FullAppArchiveName + ".zip");
                            client.DownloadFileAsync(new Uri(Url.Combine(UpdateUri.AbsoluteUri, UpdateInfo.FullAppArchiveName + ".zip")), zipFile);
                            DownloadFinished.WaitOne();
                            if (State == UpdateState.Error) { DownloadUpdateFinished(State); return; } // catch download error

                            using (var archive = ZipFile.OpenRead(zipFile))
                            {
                                archive.ExtractToDirectory(PatchDirectory, true);
                            }
                        }
                    }

                    // clean download dir
                    if (Directory.Exists(DownloadDirectory))
                        Directory.Delete(DownloadDirectory, true);

                    // notify app
                    DownloadUpdateFinished(State = UpdateState.UpdateReady);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not download update");
                    DownloadUpdateFinished(State = UpdateState.Error);
                }
            }
        }

        private void ApplyDelta(string basisFilePath, Stream deltaStream, string newFilePath)
        {
            using (var basisStream = File.OpenRead(basisFilePath))
            using (var newFileStream = File.OpenWrite(newFilePath))
            {
                DeltaApplier.Apply(basisStream, new BinaryDeltaReader(deltaStream, new ConsoleProgressReporter()), newFileStream); // TODO progress
            }
        }

        private string GetPlatformUpdaterFileName(Platform platform)
        {
            switch (platform)
            {
                case Platform.win_x64:
                case Platform.win_x86:
                case Platform.win_arm:
                    return "updater.exe";
                case Platform.linux_x64:
                case Platform.linux_arm:
                case Platform.osx_x64:
                    return "updater";
                default:
                    return null;
            }
        }

        private Platform GetPlatform()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                switch (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
                {
                    case System.Runtime.InteropServices.Architecture.X64:
                        return Platform.win_x64;
                    case System.Runtime.InteropServices.Architecture.X86:
                        return Platform.win_x86;
                    case System.Runtime.InteropServices.Architecture.Arm64:
                        return Platform.win_arm;
                    case System.Runtime.InteropServices.Architecture.Arm:
                        return Platform.win_arm;
                }
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                switch (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
                {
                    case System.Runtime.InteropServices.Architecture.X64:
                        return Platform.linux_x64;
                    case System.Runtime.InteropServices.Architecture.Arm64:
                        return Platform.linux_arm;
                    case System.Runtime.InteropServices.Architecture.Arm:
                        return Platform.linux_arm;
                }
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                switch (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
                {
                    case System.Runtime.InteropServices.Architecture.X64:
                        return Platform.osx_x64;
                }
            }

            return Platform.unknown;
        }

        private enum Platform
        {
            win_x86,
            win_x64,
            win_arm,
            osx_x64,
            linux_x64,
            linux_arm,
            unknown
        }

        public UpdateState InstallUpdate(string applicationNameToRestart = null)
        {
            lock (Lock)
            {
                Logger.LogDebug("InstallUpdate()");
                if (State != UpdateState.UpdateReady)
                {
                    Logger.LogError($"InstallUpdate() called, but state == {State.ToString()}");
                    return State;
                }

                try
                {
                    int ApplicationProcessID = Process.GetCurrentProcess().Id;
                    //string arguments = $"{ApplicationProcessID} '{PatchDirectory}' '{BaseDirectory}'";
                    //if (applicationNameToRestart != null) arguments += $" '{applicationNameToRestart}'";
                    List<string> arguments = new List<string>(4);
                    arguments.Add(ApplicationProcessID.ToString());
                    arguments.Add(PatchDirectory);
                    arguments.Add(BaseDirectory);
                    if (applicationNameToRestart != null) arguments.Add(applicationNameToRestart);
                    Logger.LogDebug($"Starting updater: {UpdaterFilePath} {string.Join(",", arguments)}");
                    // start updater without window
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = UpdaterFilePath,
                        Arguments = BuildCommandLineFromArgs(arguments.ToArray()),
                        WorkingDirectory = Path.GetDirectoryName(UpdaterFilePath),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    // arguments <processID> <patchDirectory> <applicationDirectory> [applicationName]
                    Process.Start(startInfo);
                    return UpdateState.UpdaterStarted;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Could not start updater");
                    return UpdateState.Error;
                }
            }
        }

        private static string BuildCommandLineFromArgs(params string[] args) // @ https://stackoverflow.com/questions/5510343/escape-command-line-arguments-in-c-sharp
        {
            if (args == null)
                return null;
            string result = "";

            if (Environment.OSVersion.Platform == PlatformID.Unix
                ||
                Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                foreach (string arg in args)
                {
                    result += (result.Length > 0 ? " " : "")
                        + arg
                            .Replace(@" ", @"\ ")
                            .Replace("\t", "\\\t")
                            .Replace(@"\", @"\\")
                            .Replace(@"""", @"\""")
                            .Replace(@"<", @"\<")
                            .Replace(@">", @"\>")
                            .Replace(@"|", @"\|")
                            .Replace(@"@", @"\@")
                            .Replace(@"&", @"\&");
                }
            }
            else //Windows family
            {
                bool enclosedInApo, wasApo;
                string subResult;
                foreach (string arg in args)
                {
                    enclosedInApo = arg.LastIndexOfAny(
                        new char[] { ' ', '\t', '|', '@', '^', '<', '>', '&' }) >= 0;
                    wasApo = enclosedInApo;
                    subResult = "";
                    for (int i = arg.Length - 1; i >= 0; i--)
                    {
                        switch (arg[i])
                        {
                            case '"':
                                subResult = @"\""" + subResult;
                                wasApo = true;
                                break;
                            case '\\':
                                subResult = (wasApo ? @"\\" : @"\") + subResult;
                                break;
                            default:
                                subResult = arg[i] + subResult;
                                wasApo = false;
                                break;
                        }
                    }
                    result += (result.Length > 0 ? " " : "")
                        + (enclosedInApo ? "\"" + subResult + "\"" : subResult);
                }
            }

            return result;
        }

    }

    public class Update
    {
        public readonly SortedList<Version, string> PatchTrail; // Note: musnt include current version => full app update
        public readonly Version CurrentVersion;
        public readonly long UpdateSizeInBytes;
        public Version LatestVersion { get { return PatchTrail.Count > 0 ? PatchTrail.Keys[PatchTrail.Count - 1] : new Version("0.0.0.0"); } }
        //public bool UpdateAvailable { get { return CurrentVersion < LatestVersion; } }

        public Update(Version currentVersion, long updateSizeInBytes, SortedList<Version, string> patchTrail)
        {
            CurrentVersion = currentVersion;
            UpdateSizeInBytes = updateSizeInBytes;
            PatchTrail = patchTrail;
        }
    }

    public enum UpdateState
    {
        NoUpdateAvailable,
        UpdateAvailable,
        DownloadInProgress,
        UpdateReady,
        UpdaterStarted,
        Error,
        UnknownPlatform
    }
}
