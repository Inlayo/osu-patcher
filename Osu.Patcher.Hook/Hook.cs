using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.Patcher.Hook.Patches;
using Osu.Patcher.Hook.Patches.Relax;
using Osu.Stubs.Wrappers;
using Osu.Utils;

namespace Osu.Patcher.Hook;

[UsedImplicitly]
public static class Hook
{
    private const string GithubUrl = "https://github.com/Inlayo/osu-patcher";
    private static Harmony _harmony = null!;
    private static readonly Type[] PatchOrder =
    [
        typeof(AutoSaveRelaxScores),
        typeof(AllowRelaxLowHpGlow),
        typeof(AllowRelaxDrawMisses),
        typeof(AllowRelaxComboBreakSound),
    ];

    /// <summary>
    ///     Warning-only entry point used when the injector detects an unsupported release stream.
    /// </summary>
    [UsedImplicitly]
    public static int NotifyUnsupportedRelease(string _)
    {
        var osuDir = Path.GetDirectoryName(OsuAssembly.Assembly.Location)!;
        ShowUnsupportedReleaseWarning(GetReleaseStream(osuDir));
        return 0;
    }

    /// <summary>
    ///     Full patch entry point. The injector calls this only for Stable/Stable40.
    /// </summary>
    [UsedImplicitly]
    public static int Initialize(string _)
    {
        var osuDir = Path.GetDirectoryName(OsuAssembly.Assembly.Location)!;
        var releaseStream = GetReleaseStream(osuDir);

        if (!IsSupportedReleaseStream(releaseStream))
        {
            ShowUnsupportedReleaseWarning(releaseStream);
            return 0;
        }

        try
        {
            Notifications.ShowMessage(
                "Loading Inlayo-patcher...",
                NotificationColor.Neutral,
                2500);
        }
        catch
        {
        }

        try
        {
            _harmony = new Harmony("Inlayo-patcher");
            var allPatchesApplied = InitializePatches(_harmony);

            if (!allPatchesApplied)
            {
                ShowErrorNotification();
            }

            if (allPatchesApplied)
            {
                Notifications.ShowMessage(
                    "Inlayo-patcher launched!",
                    NotificationColor.Neutral,
                    5000,
                    () => { Process.Start(GithubUrl); });
            }
        }
        catch
        {
            ShowErrorNotification();
        }

        return 0;
    }

    /// <summary>
    ///     For every type annotated with <c>[HarmonyPatch]</c> in this assembly, initialize the patch.
    /// </summary>
    /// <param name="harmony">Harmony patcher instance</param>
    private static bool InitializePatches(Harmony harmony)
    {
        var allPatchesApplied = true;

        foreach (var type in PatchOrder)
        {
            if (!type.IsDefined(typeof(OsuPatch), inherit: false))
                continue;

            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch
            {
                allPatchesApplied = false;
            }
        }

        return allPatchesApplied;
    }

    private static string? GetReleaseStream(string osuDir)
    {
        var configPath = Path.Combine(osuDir, "osu!.cfg");

        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var separator = line.IndexOf('=');
                if (separator < 0 ||
                    !line.Substring(0, separator).Trim().Equals("_ReleaseStream", StringComparison.OrdinalIgnoreCase))
                    continue;

                return line.Substring(separator + 1).Trim();
            }
        }
        catch
        {
            // Fail closed if the installation's release stream cannot be verified.
        }

        return null;
    }

    private static bool IsSupportedReleaseStream(string? releaseStream) =>
        string.Equals(releaseStream, "Stable", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(releaseStream, "Stable40", StringComparison.OrdinalIgnoreCase);

    private static void ShowUnsupportedReleaseWarning(string? releaseStream)
    {
        var detectedStream = string.IsNullOrWhiteSpace(releaseStream)
            ? "unknown (could not read osu!.cfg)"
            : releaseStream;
        var message = $"Unsupported osu! release stream: {detectedStream}. Please use the Stable version of osu!.";

        try
        {
            Notifications.ShowMessage(message, NotificationColor.Warning, 5000);
        }
        catch
        {
            // The game notification API may not be ready yet; still make the safety block visible.
            try
            {
                MessageBox(IntPtr.Zero, message, "Inlayo-patcher", 0x30);
            }
            catch
            {
            }
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>
    ///     Show a generic error notification to the user.
    /// </summary>
    private static void ShowErrorNotification()
    {
        try
        {
            Notifications.ShowMessage(
                "Inlayo-patcher failed. Click here for help.",
                NotificationColor.Error,
                20000,
                () => { Process.Start($"{GithubUrl}/issues"); });
        }
        catch
        {
        }
    }
}
