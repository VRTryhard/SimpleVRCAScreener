using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;

class Program
{
    static string? _vrcaFolder = string.Empty;

    static string BaseDir = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    static string VrcaFolder => _vrcaFolder ?? string.Empty;
    static string VrcaViewerDir => Path.Combine(BaseDir, "Viewer") + Path.DirectorySeparatorChar;
    static string AssetViewerExe => Path.Combine(VrcaViewerDir, "AssetViewer.exe");
    static string AssetViewerData => Path.Combine(VrcaViewerDir, "AssetViewer_Data") + Path.DirectorySeparatorChar;
    static string OutputDir => Path.Combine(BaseDir, "Pictures") + Path.DirectorySeparatorChar;
    static string ErrorDir => Path.Combine(BaseDir, "Error") + Path.DirectorySeparatorChar;
    static string AvatarInfoFile => Path.Combine(VrcaViewerDir, "avatarInfo.txt");
    static string BlankVrca = "blank.vrca";

    class VrcaEntry
    {
        public string FullPath { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
    }

    static int Main(string[] args)
    {
        string? overridePath = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--PathOverride=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("PathOverride=", StringComparison.OrdinalIgnoreCase))
            {
                int idx = a.IndexOf('=');
                if (idx >= 0 && a.Length > idx + 1) overridePath = a.Substring(idx + 1).Trim('"');
            }
            else if (a.Equals("--PathOverride", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                overridePath = args[i + 1].Trim('"');
            }
        }

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            try { _vrcaFolder = Path.GetFullPath(overridePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar; }
            catch { Console.WriteLine("Invalid PathOverride provided."); return 1; }
        }
        else
        {
            _vrcaFolder = Path.Combine(BaseDir, "VRCA") + Path.DirectorySeparatorChar;
        }

        Directory.CreateDirectory(OutputDir);
        Directory.CreateDirectory(ErrorDir);
        Directory.CreateDirectory(VrcaViewerDir);

        if (!Directory.Exists(VrcaFolder)) { Console.WriteLine("VRCA folder not found: " + VrcaFolder); return 1; }
        if (!File.Exists(AssetViewerExe)) { Console.WriteLine("AssetViewer.exe not found at " + AssetViewerExe + ", make sure you downloaded AssetViewer and that you extracted it in the Viewer folder."); return 2; }

        while (true)
        {
            var allVrca = Directory.EnumerateFiles(VrcaFolder, "*.vrca")
                                   .Select(f => new VrcaEntry { FullPath = f, Id = Path.GetFileNameWithoutExtension(f) })
                                   .OrderBy(e =>
                                   {
                                       try { return new FileInfo(e.FullPath).Length; } catch { return long.MaxValue; }
                                   })
                                   .ToList();

            var doneIds = Directory.Exists(OutputDir)
                ? Directory.EnumerateFiles(OutputDir, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var remaining = allVrca.Where(x => !doneIds.Contains(x.Id)).ToList();
            int ignoredCount = allVrca.Count - remaining.Count;

            Console.WriteLine($"Opening {remaining.Count} VRCA Files, {ignoredCount} have been ignored (already have a picture).");

            if (!remaining.Any()) { Console.WriteLine("No remaining .vrca files to process."); return 0; }

            try
            {
                using (var sw = new StreamWriter(AvatarInfoFile, false))
                {
                    foreach (var a in remaining) sw.WriteLine($"{a.FullPath};{a.Id}");
                }
            }
            catch (Exception e) { Console.WriteLine("Failed to write avatarInfo.txt: " + e.Message); return 3; }

            SafeClearAssetViewerData();

            var runResult = RunAssetViewerProcess();
            if (!runResult.Started)
            {
                Console.WriteLine("Failed to start AssetViewer.exe at " + AssetViewerExe + ", make sure you downloaded AssetViewer and that you extracted it in the Viewer folder.");
                bool moved = IsolationTestAndMoveOne(remaining);
                if (moved) continue;
                return 4;
            }

            if (runResult.ExitCode != 0)
            {
                MoveProducedScreenshotsToOutput();

                bool moved = IsolationTestAndMoveOne(remaining);
                if (moved) continue;
                Console.WriteLine("AssetViewer failed for batch and culprit not identified.");
                return 5;
            }

            try
            {
                var lines = File.ReadAllLines(AvatarInfoFile)
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .Select(l => l.Split(';'))
                                .Where(parts => parts.Length >= 2)
                                .Select(parts => parts[1].Trim())
                                .ToList();

                foreach (var id in lines)
                {
                    var src = Path.Combine(AssetViewerData, id + ".png");
                    var dest = Path.Combine(OutputDir, id + ".png");
                    var fallback = Path.Combine(AssetViewerData, "avatarscreen.png");

                    if (File.Exists(src))
                    {
#if NET6_0_OR_GREATER
                        File.Move(src, dest, overwrite: true);
#else
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(src, dest);
#endif
                        Console.WriteLine("Saved: " + id + ".png");
                    }
                    else if (File.Exists(fallback))
                    {
#if NET6_0_OR_GREATER
                        File.Copy(fallback, dest, overwrite: true);
#else
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Copy(fallback, dest);
#endif
                        Console.WriteLine("Saved: " + id + ".png");
                    }
                }
            }
            catch (Exception e) { Console.WriteLine("Error while moving screenshots: " + e.Message + ",try opening start.bat as administrator."); return 6; }

            Console.WriteLine("All done.");
            return 0;
        }
    }

    static void SafeClearAssetViewerData()
    {
        try
        {
            if (Directory.Exists(AssetViewerData))
            {
                foreach (var f in Directory.EnumerateFiles(AssetViewerData, "*.png")) File.Delete(f);
            }
        }
        catch { }
    }

    static (bool Started, int ExitCode) RunAssetViewerProcess()
    {
        var psi = new ProcessStartInfo
        {
            FileName = AssetViewerExe,
            Arguments = $"\"{BlankVrca}\" \"screen.shot\"",
            WorkingDirectory = VrcaViewerDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using (var p = Process.Start(psi))
            {
                if (p == null) return (false, -1);
                if (!p.WaitForExit(5 * 60 * 1000))
                {
                    try { p.Kill(); } catch { }
                    Console.WriteLine("AssetViewer stopped working and is being restarted.");
                    return (true, -1);
                }
                return (true, p.ExitCode);
            }
        }
        catch
        {
            return (false, -1);
        }
    }

    static void MoveProducedScreenshotsToOutput()
    {
        try
        {
            if (!Directory.Exists(AssetViewerData)) return;
            var intendedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(AvatarInfoFile))
            {
                intendedIds.UnionWith(File.ReadAllLines(AvatarInfoFile)
                                     .Where(l => !string.IsNullOrWhiteSpace(l))
                                     .Select(l => l.Split(';'))
                                     .Where(p => p.Length >= 2)
                                     .Select(p => p[1].Trim()));
            }

            foreach (var png in Directory.EnumerateFiles(AssetViewerData, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(png);
                string dest = Path.Combine(OutputDir, Path.GetFileName(png));
                if (File.Exists(dest))
                {
                    string baseName = Path.GetFileNameWithoutExtension(dest);
                    string ext = Path.GetExtension(dest);
                    dest = Path.Combine(OutputDir, baseName + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ext);
                }
                try
                {
#if NET6_0_OR_GREATER
                    File.Move(png, dest, overwrite: true);
#else
                    File.Move(png, dest);
#endif
                }
                catch
                {
                    try { File.Copy(png, dest, true); File.Delete(png); } catch { }
                }
            }
        }
        catch { }
    }

    static bool IsolationTestAndMoveOne(List<VrcaEntry> remaining)
    {
        Console.WriteLine("Starting isolation testing...");

        var doneIdsNow = Directory.Exists(OutputDir)
            ? Directory.EnumerateFiles(OutputDir, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidates = remaining.Where(r => !doneIdsNow.Contains(r.Id)).ToList();
        foreach (var candidate in candidates)
        {
            Console.WriteLine("Isolation test: " + candidate.Id);

            try
            {
                using (var sw = new StreamWriter(AvatarInfoFile, false))
                {
                    sw.WriteLine($"{candidate.FullPath};{candidate.Id}");
                }
            }
            catch
            {
                Console.WriteLine("Failed to write avatarInfo.txt for isolation test.");
                continue;
            }

            SafeClearAssetViewerData();

            var result = RunAssetViewerProcess();
            if (!result.Started)
            {
                Console.WriteLine("Failed to start AssetViewer for isolation test.");
                continue;
            }

            MoveProducedScreenshotsToOutput();

            bool produced = File.Exists(Path.Combine(OutputDir, candidate.Id + ".png"));

            if (result.ExitCode != 0 || !produced)
            {
                string culpritFile = candidate.FullPath;
                if (File.Exists(culpritFile))
                {
                    Console.WriteLine($"Isolation test indicates crash with {candidate.Id}. Moving it to Error folder.");
                    MoveToError(culpritFile);
                    return true;
                }
            }
            else
            {
                Console.WriteLine($"Isolation test succeeded for {candidate.Id}, opening next file.");
            }
        }
        return false;
    }

    static void MoveToError(string vrcaFullPath)
    {
        try
        {
            Directory.CreateDirectory(ErrorDir);
            if (!File.Exists(vrcaFullPath)) return;
            string destPath = Path.Combine(ErrorDir, Path.GetFileName(vrcaFullPath));
            if (File.Exists(destPath))
            {
                string baseName = Path.GetFileNameWithoutExtension(destPath);
                string ext = Path.GetExtension(destPath);
                destPath = Path.Combine(ErrorDir, baseName + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ext);
            }
            File.Move(vrcaFullPath, destPath);
        }
        catch
        {
            Console.WriteLine("Failed to move to Error folder: " + vrcaFullPath);
        }
    }
}
