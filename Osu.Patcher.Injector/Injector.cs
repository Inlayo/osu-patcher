using System;
using System.ComponentModel;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HoLLy.ManagedInjector;

namespace Osu.Patcher.Injector;

[SupportedOSPlatform("windows")]
internal static class Injector
{
    public static void Main()
    {
        try
        {
            var processId = GetOsuPid(out var lastVersion);
            var entryPoint = IsStableVersion(lastVersion) ? "Initialize" : "NotifyUnsupportedRelease";
            using var proc = new InjectableProcess(processId);
            proc.Inject(ExtractHookAssembly(), "Osu.Patcher.Hook.Hook", entryPoint);
        }
        catch (UserFacingException e)
        {
            ShowError(e.Message);
        }
        catch (Exception)
        {
            ShowError("Oops, something went wrong. Please try again.");
        }
    }

    private static string ExtractHookAssembly()
    {
        var extractionDirectory = Path.Combine(
            Path.GetTempPath(),
            "Inlayo-patcher",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionDirectory);

        foreach (var fileName in new[]
                 {
                     "0Harmony.dll",
                     "JetBrains.Annotations.dll",
                     "osu!.hook.dll",
                     "osu!.stubs.dll",
                     "utils.dll",
                 })
        {
            using var resource = typeof(Injector).Assembly.GetManifestResourceStream(fileName)
                ?? throw new InvalidOperationException($"Embedded resource '{fileName}' was not found.");
            using var output = File.Create(Path.Combine(extractionDirectory, fileName));
            resource.CopyTo(output);
        }

        return Path.Combine(extractionDirectory, "osu!.hook.dll");
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private static void ShowError(string message) =>
        MessageBox(IntPtr.Zero, message, "Inlayo-patcher", 0x10);

    private sealed class UserFacingException(string message) : Exception(message);

    private static bool IsStableVersion(string? lastVersion) =>
        IsStableVersionFormat(lastVersion);

    private static bool IsStableVersionFormat(string? lastVersion)
    {
        if (lastVersion == null || lastVersion.Length < 2 || char.ToLowerInvariant(lastVersion[0]) != 'b')
            return false;

        var previousWasDigit = false;
        for (var index = 1; index < lastVersion.Length; index++)
        {
            var character = lastVersion[index];
            if (character >= '0' && character <= '9')
            {
                previousWasDigit = true;
                continue;
            }

            if (character != '.' || !previousWasDigit || index == lastVersion.Length - 1)
                return false;

            previousWasDigit = false;
        }

        return previousWasDigit;
    }

    private static string? GetLastVersion(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        var osuDir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(osuDir))
            return null;

        var configPath = Path.Combine(osuDir, $"osu!.{Environment.UserName}.cfg");

        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var separator = line.IndexOf('=');
                if (separator < 0 ||
                    !line.Substring(0, separator).Trim().Equals("LastVersion", StringComparison.OrdinalIgnoreCase))
                    continue;

                return line.Substring(separator + 1).Trim();
            }
        }
        catch
        {
            // Unknown versions are routed to the warning-only hook entry point.
        }

        return null;
    }

    /// <summary>
    ///     Find a <c>osu!.exe</c> process that has a <c>devserver</c> in the cli arguments. (Not connected to Bancho)
    /// </summary>
    /// <returns>The process id of the first matching process.</returns>
    /// <exception cref="Exception">If found invalid osu! process or no process at all.</exception>
    private static uint GetOsuPid(out string? lastVersion)
    {
        lastVersion = null;
        using var mgmt = new ManagementClass("Win32_Process");
        using var processes = mgmt.GetInstances();

        foreach (var process in processes)
        {
            var exe = (string)process["Name"];
            var pid = (uint)process["ProcessId"];
            var cli = (string)process["CommandLine"];

            if (exe != "osu!.exe") continue;

            // Make sure devserver arg is present and not pointing to ppy.sh
            var args = ParseCommandLine(cli);
            if (args is not [_, "-devserver", { Length: > 3 }] || args is [_, "-devserver", "ppy.sh"])
                throw new UserFacingException(
                    "osu! is connected to Bancho. Launch it with a custom -devserver and try again.");

            lastVersion = GetLastVersion(process["ExecutablePath"] as string);
            return pid;
        }

        throw new UserFacingException("osu! isn't running. Start the game and try again.");
    }

    private static string[] ParseCommandLine(string commandLine)
    {
        var argumentsPointer = CommandLineToArgvW(commandLine, out var argumentCount);
        if (argumentsPointer == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var arguments = new string[argumentCount];
            for (var index = 0; index < argumentCount; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argumentsPointer, index * IntPtr.Size);
                arguments[index] = Marshal.PtrToStringUni(argumentPointer) ?? string.Empty;
            }

            return arguments;
        }
        finally
        {
            _ = LocalFree(argumentsPointer);
        }
    }

    [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
