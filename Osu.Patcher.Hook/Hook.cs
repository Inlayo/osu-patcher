using System;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.Patcher.Hook.Patches;
using Osu.Stubs.Wrappers;

namespace Osu.Patcher.Hook;

[UsedImplicitly]
public static class Hook
{
    /// <summary>
    ///     Entry point into the hook called by the injector.
    /// </summary>
    [UsedImplicitly]
    public static int Initialize(string _)
    {
        try
        {
            if (InitializePatches(new Harmony("osu!patcher")))
                ShowNotification("Inlayo-patcher launched!", NotificationColor.Neutral);
            else
                ShowNotification(
                    "Inlayo-patcher could not initialize all patches. Use a supported osu! Stable version.",
                    NotificationColor.Warning);
        }
        catch
        {
            ShowNotification(
                "Inlayo-patcher could not initialize. Use a supported osu! Stable version.",
                NotificationColor.Error);
        }

        return 0;
    }

    private static void ShowNotification(string message, NotificationColor color, int duration = 5000)
    {
        try
        {
            Notifications.ShowMessage(message, color, duration);
        }
        catch
        {
        }
    }

    /// <summary>
    ///     For every type annotated with <c>[HarmonyPatch]</c> in this assembly, initialize the patch.
    /// </summary>
    /// <param name="harmony">Harmony patcher instance</param>
    private static bool InitializePatches(Harmony harmony)
    {
        var successful = true;

        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Hook).Assembly))
        {
            if (type.GetCustomAttribute<OsuPatch>() == null)
                continue;

            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch
            {
                successful = false;
            }
        }

        return successful;
    }
}