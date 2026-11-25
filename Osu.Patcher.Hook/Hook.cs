using System;
using System.Diagnostics;
using System.IO;
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
    ///     Entry point into the hook called by the injector.
    /// </summary>
    [UsedImplicitly]
    public static int Initialize(string _)
    {
        var osuDir = Path.GetDirectoryName(OsuAssembly.Assembly.Location)!;
        var isStableRelease = IsStableRelease(osuDir);

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
            var allPatchesApplied = InitializePatches(_harmony, isStableRelease);

            if (!allPatchesApplied)
            {
                ShowErrorNotification();
            }

            if (isStableRelease && allPatchesApplied)
            {
                Notifications.ShowMessage(
                    "Inlayo-patcher launched!",
                    NotificationColor.Neutral,
                    5000,
                    () => { Process.Start(GithubUrl); });
            }
            else if (!isStableRelease)
            {
                Notifications.ShowMessage(
                    "AutoSave Relax Scores is Stable-only; it is disabled on Cutting Edge/Beta.",
                    NotificationColor.Warning,
                    8000);
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
    private static bool InitializePatches(Harmony harmony, bool isStableRelease)
    {
        var allPatchesApplied = true;

        foreach (var type in PatchOrder)
        {
            if (!type.IsDefined(typeof(OsuPatch), inherit: false))
                continue;

            if (type == typeof(AutoSaveRelaxScores) && !isStableRelease)
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

    private static bool IsStableRelease(string osuDir)
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

                var stream = line.Substring(separator + 1).Trim();
                return stream.Equals("Stable40", StringComparison.OrdinalIgnoreCase) ||
                       stream.Equals("Stable", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Fail closed if the installation's release stream cannot be verified.
        }

        return false;
    }

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
