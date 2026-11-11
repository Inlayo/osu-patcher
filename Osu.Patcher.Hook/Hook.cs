using System;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.Patcher.Hook.Patches;

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
#if DEBUG
        ConsoleHook.InitializeConsoleOutput();
        DebugHook.Initialize();
#endif

        Console.WriteLine("Inlayo-patcher hook started.");

        try
        {
            var successful = InitializePatches(new Harmony("osu!patcher"), out var patchCount);
            Console.WriteLine(
                successful
                    ? $"Initialized {patchCount} patches."
                    : $"Initialized {patchCount} patches with errors.");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return 0;
    }

    /// <summary>
    ///     For every type annotated with <c>[HarmonyPatch]</c> in this assembly, initialize the patch.
    /// </summary>
    /// <param name="harmony">Harmony patcher instance</param>
    private static bool InitializePatches(Harmony harmony, out int patchCount)
    {
        var successful = true;
        patchCount = 0;

        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Hook).Assembly))
        {
            if (!OsuPatchProcessor.IsOsuPatch(type))
                continue;

            patchCount++;
            Console.WriteLine($"Installing patch {type.Name}...");

            try
            {
                new OsuPatchProcessor(harmony, type).Patch();
                Console.WriteLine($"Installed patch {type.Name}.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to launch patch {type.Name}");
                Console.WriteLine(e);
                successful = false;
            }
        }

        return successful;
    }
}