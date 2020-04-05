using System;
using System.Collections.Generic;
using System.Data.HashFunction.xxHash;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;
using ECAUM;

namespace ECAUM.Tests
{
    public class Helper
    {
        public static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string TestFilesDirectory = Path.GetFullPath(Path.Combine(BaseDirectory, "..", "..", "..", "..", "TestFiles"));
        public static readonly int UPDATER_TIMEOUT = 2000; // in msec
        public static readonly string AppName = "TestApp.exe";

        public static void CopyDirectory(string sourceDir, string targetDir, bool overwrite = false)
        {
            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relFilePath = Path.GetRelativePath(sourceDir, file);
                string targetFilePath = Path.Combine(targetDir, relFilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath));
                File.Copy(file, targetFilePath, overwrite);
            }
        }

        public static bool DirectoriesEqual(string dir1, string dir2)
        {
            var hasher = xxHashFactory.Instance.Create();
            HashSet<string> files = new HashSet<string>();

            foreach (string file1 in Directory.GetFiles(dir1, "*", SearchOption.AllDirectories))
            {
                string relPath = Path.GetRelativePath(dir1, file1);
                string file2 = Path.Combine(dir2, relPath);
                if (!File.Exists(file2)) // has to exist in dir2
                    return false;
                string hash1, hash2;
                using (var fileStream = File.OpenRead(file1))
                    hash1 = hasher.ComputeHash(fileStream).AsHexString();
                using (var fileStream = File.OpenRead(file2))
                    hash2 = hasher.ComputeHash(fileStream).AsHexString();
                if (!hash1.Equals(hash2))
                    return false;
                files.Add(relPath);
            }
            // cross check that dir2 does not contain extra files
            foreach (string file2 in Directory.GetFiles(dir2, "*", SearchOption.AllDirectories))
            {
                if (!files.Contains(Path.GetRelativePath(dir2, file2)))
                    return false;
            }
            return true;
        }
    }

    public class V0ToV1
    {

        [Theory, CombinatorialData]
        public void UpdateV0ToV1(bool useDefaultUpdateDir, bool enableLogging)
        {
            string TestName = $"UpdateV0ToV1";
            TestName += useDefaultUpdateDir ? "_DefaultUpdateDir" : "_SpecifiedUpdateDir";
            TestName += enableLogging ? "_Logging" : "_NoLogging";
            string AppDir = Path.Combine(Helper.BaseDirectory, TestName, "app");
            string UpdateDir = Path.Combine(Helper.BaseDirectory, TestName, "update");
            string DefaultUpdateDir = Path.Combine(AppDir, "update");

            // Cleanup prior test
            if (Directory.Exists(AppDir))
                Directory.Delete(AppDir, true);
            if (Directory.Exists(UpdateDir))
                Directory.Delete(UpdateDir, true);

            // Prepare
            Helper.CopyDirectory(Path.Combine(Helper.TestFilesDirectory, "app_v0"), AppDir);

            // Start app
            string arguments = new Uri(Path.Combine(Helper.TestFilesDirectory, "update_v1")).ToString();
            arguments += ' ' + enableLogging.ToString();
            if (!useDefaultUpdateDir) arguments += ' ' + UpdateDir;
            Process app = Process.Start(Path.Combine(AppDir, Helper.AppName), arguments);
            app.WaitForExit();
            Assert.Equal(0, app.ExitCode);

            Thread.Sleep(Helper.UPDATER_TIMEOUT); // wait for the updater to patch the files

            if (useDefaultUpdateDir)
            { // Move default update dir to check directories
                Assert.True(Directory.Exists(DefaultUpdateDir));
                Directory.Move(DefaultUpdateDir, UpdateDir);
            }
            else
            {
                Assert.True(!Directory.Exists(DefaultUpdateDir));
            }

            Assert.True(Helper.DirectoriesEqual(Path.Combine(Helper.TestFilesDirectory, "app_v2"), AppDir));
        }
    }

    public class V1ToV3
    {
        [Theory, CombinatorialData]
        public void UpdateV1ToV3(bool useDefaultUpdateDir, bool enableLogging)
        {
            string TestName = $"UpdateV1ToV3";
            TestName += useDefaultUpdateDir ? "_DefaultUpdateDir" : "_SpecifiedUpdateDir";
            TestName += enableLogging ? "_Logging" : "_NoLogging";
            string AppDir = Path.Combine(Helper.BaseDirectory, TestName, "app");
            string UpdateDir = Path.Combine(Helper.BaseDirectory, TestName, "update");
            string DefaultUpdateDir = Path.Combine(AppDir, "update");

            // Cleanup prior test
            if (Directory.Exists(AppDir))
                Directory.Delete(AppDir, true);
            if (Directory.Exists(UpdateDir))
                Directory.Delete(UpdateDir, true);

            // Prepare
            Helper.CopyDirectory(Path.Combine(Helper.TestFilesDirectory, "app_v1"), AppDir);

            // Start app
            string arguments = new Uri(Path.Combine(Helper.TestFilesDirectory, "updatedir_v3")).ToString();
            arguments += ' ' + enableLogging.ToString();
            if (!useDefaultUpdateDir) arguments += ' ' + UpdateDir;
            Process app = Process.Start(Path.Combine(AppDir, Helper.AppName), arguments);
            app.WaitForExit();
            Assert.Equal(0, app.ExitCode);

            Thread.Sleep(Helper.UPDATER_TIMEOUT); // wait for the updater to patch the files

            if (useDefaultUpdateDir)
            { // Move default update dir to check directories
                Assert.True(Directory.Exists(DefaultUpdateDir));
                Directory.Move(DefaultUpdateDir, UpdateDir);
            }
            else
            {
                Assert.True(!Directory.Exists(DefaultUpdateDir));
            }

            Assert.True(Helper.DirectoriesEqual(Path.Combine(Helper.TestFilesDirectory, "app_v3"), AppDir));
        }
    }

    public class V1ToV2ToV3
    {
        [Theory, CombinatorialData]
        public void UpdateV1ToV2ToV3(bool useDefaultUpdateDir, bool enableLogging)
        {
            string TestName = $"UpdateV1ToV2ToV3";
            TestName += useDefaultUpdateDir ? "_DefaultUpdateDir" : "_SpecifiedUpdateDir";
            TestName += enableLogging ? "_Logging" : "_NoLogging";
            string AppDir = Path.Combine(Helper.BaseDirectory, TestName, "app");
            string UpdateDir = Path.Combine(Helper.BaseDirectory, TestName, "update");
            string DefaultUpdateDir = Path.Combine(AppDir, "update");

            // Cleanup prior test
            if (Directory.Exists(AppDir))
                Directory.Delete(AppDir, true);
            if (Directory.Exists(UpdateDir))
                Directory.Delete(UpdateDir, true);

            // Prepare
            Helper.CopyDirectory(Path.Combine(Helper.TestFilesDirectory, "app_v1"), AppDir);

            // Start app with update dir v2
            string arguments = new Uri(Path.Combine(Helper.TestFilesDirectory, "updatedir_v2")).ToString();
            arguments += ' ' + enableLogging.ToString();
            if (!useDefaultUpdateDir) arguments += ' ' + UpdateDir;
            Process app = Process.Start(Path.Combine(AppDir, Helper.AppName), arguments);
            app.WaitForExit();
            Assert.Equal(0, app.ExitCode);

            Thread.Sleep(Helper.UPDATER_TIMEOUT); // wait for the updater to patch the files

            // Start app with update dir v3
            arguments = new Uri(Path.Combine(Helper.TestFilesDirectory, "updatedir_v3")).ToString();
            arguments += ' ' + enableLogging.ToString();
            if (!useDefaultUpdateDir) arguments += ' ' + UpdateDir;
            app = Process.Start(Path.Combine(AppDir, Helper.AppName), arguments);
            app.WaitForExit();
            Assert.Equal(0, app.ExitCode);

            Thread.Sleep(Helper.UPDATER_TIMEOUT); // wait for the updater to patch the files


            if (useDefaultUpdateDir)
            { // Move default update dir to check directories
                Assert.True(Directory.Exists(DefaultUpdateDir));
                Directory.Move(DefaultUpdateDir, UpdateDir);
            }
            else
            {
                Assert.True(!Directory.Exists(DefaultUpdateDir));
            }

            Assert.True(Helper.DirectoriesEqual(Path.Combine(Helper.TestFilesDirectory, "app_v3"), AppDir));
        }
    }

    public class FullArchive
    {
        [Fact]
        public void UpdateFullArchive()
        {
            string TestName = $"UpdateFullArchive_DefaultUpdateDir_Logging";
            string AppDir = Path.Combine(Helper.BaseDirectory, TestName, "app");
            string UpdateDir = Path.Combine(Helper.BaseDirectory, TestName, "update");
            string DefaultUpdateDir = Path.Combine(AppDir, "update");
            string UpdateFiles = Path.Combine(Helper.BaseDirectory, TestName, "updateFiles");

            // Cleanup prior test
            if (Directory.Exists(AppDir))
                Directory.Delete(AppDir, true);
            if (Directory.Exists(UpdateDir))
                Directory.Delete(UpdateDir, true);
            if (Directory.Exists(UpdateFiles))
                Directory.Delete(UpdateFiles, true);

            // Prepare
            Helper.CopyDirectory(Path.Combine(Helper.TestFilesDirectory, "app_v1"), AppDir);
            Helper.CopyDirectory(Path.Combine(Helper.TestFilesDirectory, "updatedir_v3"), UpdateFiles);
            // force full archive update by making sure its smaller than the patches
            string updateInfoFile = Path.Combine(UpdateFiles, "update.json");
            UpdateInfo updateInfo = UpdateInfo.Deserialize(File.ReadAllText(updateInfoFile));
            updateInfo.PatchTrail[updateInfo.PatchTrail.Count - 1].SizeInBytes = updateInfo.FullAppArchiveSize * 2;
            File.WriteAllText(updateInfoFile, updateInfo.Serialize());

            // Start app
            string arguments = $"{new Uri(UpdateFiles).ToString()} true";
            Process app = Process.Start(Path.Combine(AppDir, Helper.AppName), arguments);
            app.WaitForExit();
            Assert.Equal(0, app.ExitCode);

            Thread.Sleep(Helper.UPDATER_TIMEOUT); // wait for the updater to patch the files

            Assert.True(Directory.Exists(DefaultUpdateDir));
            Directory.Move(DefaultUpdateDir, UpdateDir);

            Assert.True(Helper.DirectoriesEqual(Path.Combine(Helper.TestFilesDirectory, "app_v3"), AppDir));
        }
    }
}
