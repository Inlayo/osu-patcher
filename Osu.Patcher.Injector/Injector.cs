using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;
using HoLLy.ManagedInjector;

namespace Osu.Patcher.Injector;

[SupportedOSPlatform("windows")]
internal static class Injector
{
    private const string ServerAddress = "inlayo.com";
    private static readonly string DefaultOsuDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "osu!");
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Inlayo-patcher",
        "settings.json");

    [STAThread]
    public static void Main()
    {
        try
        {
            var osuPath = FindOsuExecutable();
            SaveSettings(osuPath);
            var injectionDirectory = ExtractInjectionFiles();

            var startInfo = new ProcessStartInfo
            {
                FileName = osuPath,
                WorkingDirectory = Path.GetDirectoryName(osuPath)!,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-nosplash");
            startInfo.ArgumentList.Add("-devserver");
            startInfo.ArgumentList.Add(ServerAddress);

            LaunchAndInject(startInfo, injectionDirectory);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            MessageBox.Show(
                e.Message,
                "Inlayo-patcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void LaunchAndInject(ProcessStartInfo startInfo, string injectionDirectory)
    {
        var stagingPath = Path.Combine(startInfo.WorkingDirectory, "_staging");
        if (File.Exists(stagingPath) || Directory.Exists(stagingPath))
            throw new InvalidOperationException(
                "An _staging file or folder already exists in the osu! folder. Remove it before starting the patcher.");

        var createdStagingDirectory = false;
        try
        {
            Directory.CreateDirectory(stagingPath);
            createdStagingDirectory = true;
            var stagingDirectory = new DirectoryInfo(stagingPath);
            stagingDirectory.Attributes |= FileAttributes.ReadOnly;

            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var osuProcess = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start osu!.");

                try
                {
                    WaitForGameWindow(osuProcess);
                    WaitForSupportedTarget(osuProcess);
                }
                catch (OsuExitedDuringStartupException) when (attempt == 0)
                {
                    continue;
                }

                try
                {
                    InjectIntoProcess(osuProcess.Id, injectionDirectory);
                    return;
                }
                catch
                {
                    StopOsuProcess(osuProcess);
                    if (attempt == 0)
                        continue;

                    throw;
                }
            }
        }
        finally
        {
            if (createdStagingDirectory && Directory.Exists(stagingPath))
            {
                var stagingDirectory = new DirectoryInfo(stagingPath);
                stagingDirectory.Attributes &= ~FileAttributes.ReadOnly;
                stagingDirectory.Delete();
            }
        }
    }

    private static void InjectIntoProcess(int processId, string injectionDirectory)
    {
        using var proc = new InjectableProcess((uint)processId);
        var dllPath = Path.Combine(injectionDirectory, "osu!.hook.dll");
        proc.Inject(dllPath, "Osu.Patcher.Hook.Hook", "Initialize");
    }

    private static void StopOsuProcess(Process process)
    {
        if (process.HasExited)
            return;

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }

        process.WaitForExit();
    }

    /// <summary>
    ///     Uses the saved osu! path, common install locations, or asks for the executable path.
    /// </summary>
    private static string FindOsuExecutable()
    {
        if (File.Exists(SettingsPath))
        {
            Settings? settings;
            try
            {
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
            }
            catch (JsonException)
            {
                settings = null;
            }

            if (settings?.OsuExecutablePath is { } savedPath && IsOsuExecutable(savedPath))
                return Path.GetFullPath(savedPath);
        }

        var candidates = new[]
        {
            Path.Combine(DefaultOsuDirectory, "osu!.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "osu!", "osu!.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "osu!", "osu!.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "osu!", "osu!.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (IsOsuExecutable(candidate))
                return Path.GetFullPath(candidate);
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Select osu!.exe",
            Filter = "osu!.exe|osu!.exe",
            CheckFileExists = true,
            FileName = "osu!.exe",
            InitialDirectory = DefaultOsuDirectory,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            throw new OperationCanceledException();

        if (!IsOsuExecutable(dialog.FileName))
            throw new FileNotFoundException("The selected file is not osu!.exe.");

        return Path.GetFullPath(dialog.FileName);
    }

    private static bool IsOsuExecutable(string path) =>
        string.Equals(Path.GetFileName(path), "osu!.exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    private static void SaveSettings(string osuPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(
                new Settings { OsuExecutablePath = osuPath },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ExtractInjectionFiles()
    {
        var assembly = typeof(Injector).Assembly;
        var files = new[]
        {
            "osu!.hook.dll",
            "osu!.stubs.dll",
            "utils.dll",
            "0Harmony.dll",
            "JetBrains.Annotations.dll",
        }.Select(file => (Name: file, Contents: ReadEmbeddedFile(assembly, $"InjectionFiles.{file}"))).ToArray();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
            hash.AppendData(file.Contents);

        var version = Convert.ToHexString(hash.GetHashAndReset());
        var injectionDirectory = Path.Combine(
            Path.GetTempPath(),
            "Inlayo-patcher",
            "injection",
            version);
        Directory.CreateDirectory(injectionDirectory);

        foreach (var file in files)
        {
            var path = Path.Combine(injectionDirectory, file.Name);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(file.Contents))
                File.WriteAllBytes(path, file.Contents);
        }

        return injectionDirectory;
    }

    private static void WaitForSupportedTarget(Process process)
    {
        var processId = process.Id;
        var timeout = DateTime.UtcNow.AddSeconds(15);
        var lastStatus = ProcessStatus.Unknown;

        while (DateTime.UtcNow < timeout)
        {
            process.Refresh();
            if (process.HasExited)
                throw new OsuExitedDuringStartupException("osu! exited before its .NET runtime became available.");

            using var target = new InjectableProcess((uint)processId);
            lastStatus = target.GetStatus();

            if (lastStatus == ProcessStatus.ArchitectureMismatch)
            {
                throw new InvalidOperationException(
                    "The patcher and osu! have incompatible architectures. This build requires 32-bit osu!.");
            }

            if (lastStatus == ProcessStatus.Ok)
            {
                var architecture = target.GetArchitecture();
                if (architecture != ProcessArchitecture.NetFrameworkV4)
                {
                    throw new InvalidOperationException(
                        $"Unsupported osu! runtime: {architecture}. This patcher requires .NET Framework 4.x.");
                }

                return;
            }

            System.Threading.Thread.Sleep(250);
        }

        var detail = lastStatus switch
        {
            ProcessStatus.NoRuntimeFound =>
                "The game's .NET Framework runtime was not detected. Make sure osu! has fully loaded and try again.",
            _ =>
                "Could not inspect the game process. Run the patcher and osu! at the same privilege level.",
        };
        throw new TimeoutException($"Cannot inject into osu! (PID {processId}): {detail} Status: {lastStatus}.");
    }

    private static byte[] ReadEmbeddedFile(System.Reflection.Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded injection file not found: {resourceName}");
        using var contents = new MemoryStream();
        stream.CopyTo(contents);
        return contents.ToArray();
    }

    private static void WaitForGameWindow(Process process)
    {
        var timeout = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < timeout)
        {
            process.Refresh();
            if (process.HasExited)
                throw new OsuExitedDuringStartupException("osu! exited before its game window appeared.");

            if (process.MainWindowHandle != IntPtr.Zero)
                return;

            System.Threading.Thread.Sleep(250);
        }

        throw new TimeoutException("Timed out waiting for the osu! window to appear.");
    }

    private sealed class OsuExitedDuringStartupException(string message) : InvalidOperationException(message)
    {
    }

    private sealed class Settings
    {
        public string? OsuExecutablePath { get; set; }
    }
}