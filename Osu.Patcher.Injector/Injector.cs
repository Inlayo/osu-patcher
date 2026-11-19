using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using HoLLy.ManagedInjector;

namespace Osu.Patcher.Injector;

[SupportedOSPlatform("windows")]
internal static class Injector
{
    private const string ServerAddress = "inlayo.com";
    private const string InjectionFilesBaseUrl = "https://assets.inlayo.com/patcher/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
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
            EnsureOsuNotRunning();
            SaveSettings(osuPath);
            var injectionDirectory = DownloadInjectionFiles();
            LoadInjectorDependencies(injectionDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = osuPath,
                WorkingDirectory = Path.GetDirectoryName(osuPath)!,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-devserver");
            startInfo.ArgumentList.Add(ServerAddress);

            LaunchAndInject(startInfo, injectionDirectory);
        }
        catch (UserCancelledException)
        {
            return;
        }
        catch (Exception e)
        {
            MessageBox.Show(
                GetUserFacingError(e),
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
                "osu! is already updating or an update-blocking file exists. Close osu!, remove the _staging file from its folder, and try again.");

        var createdStagingFile = false;
        try
        {
            using (new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                createdStagingFile = true;
            }

            File.SetAttributes(stagingPath, FileAttributes.ReadOnly);

            using var osuProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows could not start osu!. Check the selected osu!.exe and try again.");

            try
            {
                WaitForGameWindow(osuProcess);
                WaitForSupportedTarget(osuProcess);
            }
            catch (OsuExitedDuringStartupException e)
            {
                throw new InvalidOperationException(
                    "osu! closed before it was ready. Start the patcher again and use a supported osu! Stable version.",
                    e);
            }

            try
            {
                InjectIntoProcess(osuProcess.Id, injectionDirectory);
            }
            catch (Exception e)
            {
                throw new InjectionFailedException(
                    "Injection failed. Close osu!, make sure both programs run with the same permissions, and use a supported osu! Stable version.",
                    e);
            }
        }
        finally
        {
            if (createdStagingFile && File.Exists(stagingPath))
            {
                File.SetAttributes(stagingPath, FileAttributes.Normal);
                File.Delete(stagingPath);
            }
        }
    }

    private static void InjectIntoProcess(int processId, string injectionDirectory)
    {
        using var proc = new InjectableProcess((uint)processId);
        var dllPath = Path.Combine(injectionDirectory, "osu!.hook.dll");
        proc.Inject(dllPath, "Osu.Patcher.Hook.Hook", "Initialize");
    }

    private static void EnsureOsuNotRunning()
    {
        var running = false;
        foreach (var process in Process.GetProcessesByName("osu!"))
        {
            using (process)
                running |= !process.HasExited;
        }

        if (running)
            throw new InvalidOperationException(
                "osu! is already running. Close osu! completely, then start Inlayo-patcher again.");
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
            throw new UserCancelledException();

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

    private static string DownloadInjectionFiles()
    {
        var files = Task.WhenAll(new[]
        {
            "osu!.hook.dll",
            "osu!.stubs.dll",
            "utils.dll",
            "0Harmony.dll",
            "JetBrains.Annotations.dll",
            "ManagedInjector.Lib.dll",
            "Iced.dll",
        }.Select(async file => (
            Name: file,
            Contents: await Http.GetByteArrayAsync(new Uri(new Uri(InjectionFilesBaseUrl), file))
                .ConfigureAwait(false))))
            .GetAwaiter()
            .GetResult();

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

    private static string GetUserFacingError(Exception error)
    {
        if (error is InjectionFailedException injectionError)
            return injectionError.Message;

        if (error is HttpRequestException httpError)
        {
            if (httpError.StatusCode == HttpStatusCode.NotFound)
                return "The patcher server could not find the required files (HTTP 404). The server may be offline or the files may not be uploaded. Please try again later.";

            if (httpError.StatusCode is { } statusCode)
            {
                if ((int)statusCode >= 500)
                    return $"The patcher server is temporarily unavailable (HTTP {(int)statusCode}). Please try again later.";

                return $"The patcher server rejected the request (HTTP {(int)statusCode}). Please contact the maintainer.";
            }

            return "Could not connect to the patcher server. It may be offline, or your internet connection may be unavailable. Please try again later.";
        }

        if (error is TaskCanceledException)
            return "The patcher server did not respond in time. Check your internet connection and try again.";

        if (error is UnauthorizedAccessException)
            return "Access was denied. Close osu! and make sure Inlayo-patcher has permission to access the osu! folder. Run both programs with the same permission level.";

        if (error is Win32Exception { NativeErrorCode: 5 })
            return "Windows denied access. Run osu! and Inlayo-patcher with the same permission level, then try again.";

        if (error is Win32Exception { NativeErrorCode: 2 })
            return "osu!.exe could not be found. Select the correct osu! installation and try again.";

        if (error is TimeoutException)
            return "osu! did not become ready in time. Close it and try again with a supported osu! Stable version.";

        if (error is FileNotFoundException or InvalidOperationException)
            return error.Message;

        return $"An unexpected error occurred: {error.Message}";
    }

    private static void LoadInjectorDependencies(string injectionDirectory)
    {
        AssemblyLoadContext.Default.LoadFromAssemblyPath(
            Path.Combine(injectionDirectory, "Iced.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(
            Path.Combine(injectionDirectory, "ManagedInjector.Lib.dll"));
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
                    "This patcher requires 32-bit osu!. Install or select the 32-bit osu! version and try again.");
            }

            if (lastStatus == ProcessStatus.Ok)
            {
                var architecture = target.GetArchitecture();
                if (architecture != ProcessArchitecture.NetFrameworkV4)
                {
                    throw new InvalidOperationException(
                        "This patcher requires osu! running on .NET Framework 4.x. Use a supported osu! Stable version.");
                }

                return;
            }

            System.Threading.Thread.Sleep(250);
        }

        var detail = lastStatus switch
        {
            ProcessStatus.NoRuntimeFound =>
                "The .NET Framework runtime was not detected. Make sure osu! has finished starting and try again.",
            _ =>
                "Could not inspect osu!. Run Inlayo-patcher and osu! with the same permission level.",
        };
        throw new TimeoutException(detail);
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

        throw new TimeoutException("The osu! window did not appear in time. Close osu! and try again.");
    }

    private sealed class OsuExitedDuringStartupException(string message) : InvalidOperationException(message)
    {
    }

    private sealed class InjectionFailedException(string message, Exception innerException)
        : InvalidOperationException(message, innerException)
    {
    }

    private sealed class UserCancelledException : Exception
    {
    }

    private sealed class Settings
    {
        public string? OsuExecutablePath { get; set; }
    }
}