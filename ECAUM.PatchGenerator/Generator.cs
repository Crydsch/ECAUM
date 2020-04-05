using NLog;
using System;
using System.Data.HashFunction.xxHash;
using System.IO;
using FastRsync.Signature;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Core;
using System.Reflection;
using System.IO.Compression;
using ECAUM;
using System.Diagnostics;

namespace ECAUM.PatchGenerator
{
    class Generator
    {
        private readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly SignatureBuilder SignatureBuilder = new SignatureBuilder();
        private readonly DeltaBuilder DeltaBuilder = new DeltaBuilder();

        public void Start(string AssemblyFileName, string AppDirectory, string OutputDirectory, string WorkingDirectory)
        {
            Log.Info($"Arguments: assemblyFileName={AssemblyFileName} appDir={AppDirectory} outputDir={OutputDirectory} workDir={WorkingDirectory}");
            // prepare
            string SignatureDirectory = Path.Combine(WorkingDirectory, "signatures");
            Directory.CreateDirectory(SignatureDirectory);
            string PatchDirectory = Path.Combine(WorkingDirectory, "patch");
            if (Directory.Exists(PatchDirectory))
                Directory.Delete(PatchDirectory, true);
            Directory.CreateDirectory(PatchDirectory);

            DeltaBuilder.ProgressReport = new ConsoleProgressReporter(); // TODO use logger

            // load patchInfo
            Log.Debug("loading patchInfo");
            PatchInfo patchInfo;
            string patchInfoFile = Path.Combine(WorkingDirectory, "patch.json");
            if (File.Exists(patchInfoFile))
                patchInfo = PatchInfo.Deserialize(File.ReadAllText(patchInfoFile));
            else
                patchInfo = new PatchInfo();

            // diff files and generate patch
            Log.Debug("generate patch");
            var hasher = xxHashFactory.Instance.Create();
            string[] files = Directory.GetFiles(AppDirectory, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string filePath = files[i];
                string relFilePath = Path.GetRelativePath(AppDirectory, filePath);
                Log.Info($"({i + 1}/{files.Length}): {relFilePath}");

                string newHash;
                using (var fileStream = File.OpenRead(filePath))
                    newHash = hasher.ComputeHash(fileStream).AsHexString();

                if (patchInfo.FileHashes.TryGetValue(relFilePath, out string oldHash))
                {
                    if (!oldHash.Equals(newHash))
                    { // found newer version of this file => add delta to patch
                        Log.Trace($"file is newer => calculating delta");
                        // calc delta file
                        string deltaFilePath = Path.Combine(PatchDirectory, $"{relFilePath}.delta");
                        Directory.CreateDirectory(Path.GetDirectoryName(deltaFilePath));
                        string signatureFilePath = Path.Combine(SignatureDirectory, $"{relFilePath}.sig");
                        CalculateDelta(signatureFilePath, filePath, deltaFilePath);
                        // calc new signature
                        if (File.Exists(signatureFilePath))
                            File.Delete(signatureFilePath);
                        CalculateSignature(filePath, signatureFilePath);
                        // update hash
                        patchInfo.FileHashes[relFilePath] = newHash;
                    }
                }
                else
                { // completely new file => just add it to the patch
                    Log.Trace($"new file => copy it");
                    // copy file to patchDirectory
                    string newFilePath = Path.Combine(PatchDirectory, relFilePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(newFilePath));
                    File.Copy(filePath, newFilePath);
                    // calc signature
                    string signatureFilePath = Path.Combine(SignatureDirectory, $"{relFilePath}.sig");
                    Directory.CreateDirectory(Path.GetDirectoryName(signatureFilePath));
                    CalculateSignature(filePath, signatureFilePath);
                    // add file+hash to patchInfo
                    patchInfo.FileHashes.Add(relFilePath, newHash);
                }
            }
            Log.Debug("generate patch finished");

            // sanity check: has anything changed?
            if (Directory.GetFiles(PatchDirectory, "*", SearchOption.AllDirectories).Length == 0)
            {
                Log.Warn("No changes found => not generating an empty patch! => exiting");
                Environment.Exit(0);
            }

            // write patch.json
            Log.Debug("Write patch.json");
            File.WriteAllText(patchInfoFile, patchInfo.Serialize());

            // package the patch into a .zip
            Version targetVersion = Assembly.LoadFrom(Path.Combine(AppDirectory, AssemblyFileName)).GetName().Version;
            string patchFileName = $"patch_{targetVersion.ToString().Replace('.', '_')}.zip";
            Log.Info($"Packaging {patchFileName}");
            string patchFilePath = Path.Combine(OutputDirectory, patchFileName);
            ZipFile.CreateFromDirectory(PatchDirectory, patchFilePath);

            // load update.json
            Log.Debug("Load update.json");
            UpdateInfo updateInfo;
            string updateInfoFilePath = Path.Combine(OutputDirectory, "update.json");
            if (File.Exists(updateInfoFilePath))
                updateInfo = UpdateInfo.Deserialize(File.ReadAllText(updateInfoFilePath));
            else
                updateInfo = new UpdateInfo();

            // add patch to patchtrail
            Log.Debug("Add patch to patchtrail");
            long fileSize = new FileInfo(patchFilePath).Length;
            updateInfo.PatchTrail.Add(new Patch(targetVersion, "", fileSize));

            // package full app archive into a .zip
            Log.Info($"Packaging {updateInfo.FullAppArchiveName}.zip");
            string fullAppArchiveFilePath = Path.Combine(OutputDirectory, $"{updateInfo.FullAppArchiveName}.zip");
            if (File.Exists(fullAppArchiveFilePath))
                File.Delete(fullAppArchiveFilePath);
            ZipFile.CreateFromDirectory(AppDirectory, fullAppArchiveFilePath);

            // update fullAppArchiveSize
            Log.Debug("Update fullAppArchiveSize");
            fileSize = new FileInfo(fullAppArchiveFilePath).Length;
            updateInfo.FullAppArchiveSize = fileSize;

            // add updater version & filesize
            Log.Debug("add updater version & filesize");
            foreach (string updaterFilePath in new string[] { Path.Combine(OutputDirectory, "updater.exe"), Path.Combine(OutputDirectory, "updater") })
            {
                if (File.Exists(updaterFilePath))
                {
                    updateInfo.UpdaterVersion = new Version(FileVersionInfo.GetVersionInfo(updaterFilePath).FileVersion); // File Version is equal to assembly version
                    updateInfo.UpdaterSize = new FileInfo(updaterFilePath).Length;
                }
            }

            // write update.json
            Log.Info("Updating update.json");
            File.WriteAllText(updateInfoFilePath, updateInfo.Serialize());

            // clean up patch directory
            Log.Debug("Clean up patch directory");

            if (Directory.Exists(PatchDirectory))
                Directory.Delete(PatchDirectory, true);
        }

        private void CalculateSignature(string baseFilePath, string signatureFilePath)
        {
            using var basisStream = File.OpenRead(baseFilePath);
            using var signatureStream = File.OpenWrite(signatureFilePath);
            SignatureBuilder.Build(basisStream, new SignatureWriter(signatureStream));
        }

        private void CalculateDelta(string signatureFilePath, string newFilePath, string deltaFilePath)
        {
            using var newFileStream = File.OpenRead(newFilePath);
            using var signatureStream = File.OpenRead(signatureFilePath);
            using var deltaStream = File.OpenWrite(deltaFilePath);
            DeltaBuilder.BuildDelta(newFileStream, new SignatureReader(signatureStream, DeltaBuilder.ProgressReport),
                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        }
    }

}
