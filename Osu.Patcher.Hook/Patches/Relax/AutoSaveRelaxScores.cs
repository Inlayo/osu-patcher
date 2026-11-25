using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.Utils.IL;

namespace Osu.Patcher.Hook.Patches.Relax;

/// <summary>
///     Removes the following code from <c>osu.GameModes.Ranking:loadLocalUserScore(bool showRankingDialog)</c>
///     to enable automatically saving Relax* scores after finishing playing.
///     <br />
///     <code><![CDATA[
///         Mods mods = Player.currentScore.EnabledMods;  
///         if ((mods & Mods.Relax) <= Mods.None)
///         {
///             mods = Player.currentScore.EnabledMods;
///             if ((mods & Mods.Relax2) <= Mods.None)
///             {
///     ]]></code>
/// </summary>
[OsuPatch]
[HarmonyPatch]
[UsedImplicitly]
internal static class AutoSaveRelaxScores
{
    // #=zG9n2xn5fBJ3KmhYrFhPv_ouHnledvs2AJ1Dwx_c=:#=zPWtjIx_tsaf1
    private static readonly OpCode[] Signature =
    [
        OpCodes.Ldarg_0,
        OpCodes.Ldfld,
        OpCodes.Ldfld,
        OpCodes.Brfalse,
        OpCodes.Ldarg_0,
        OpCodes.Ldfld,
        OpCodes.Brtrue,
        OpCodes.Ldsfld, // <--------------
        OpCodes.Ldfld,
        OpCodes.Call,
        OpCodes.Ldc_I4,
        OpCodes.Stloc_2,
        OpCodes.Stloc_1,
        OpCodes.Ldloc_1,
        OpCodes.Ldloc_2,
        OpCodes.And,
        OpCodes.Ldc_I4_0,
        OpCodes.Cgt,
        OpCodes.Brtrue, // Relax early return
        OpCodes.Ldsfld,
        OpCodes.Ldfld,
        OpCodes.Call,
        OpCodes.Ldc_I4,
        OpCodes.Stloc_2,
        OpCodes.Stloc_1,
        OpCodes.Ldloc_1,
        OpCodes.Ldloc_2,
        OpCodes.And,
        OpCodes.Ldc_I4_0,
        OpCodes.Cgt,
        OpCodes.Brtrue, // Relax2 early return
    ];

    [UsedImplicitly]
    [HarmonyTargetMethod]
    private static MethodBase Target()
    {
        var target = OpCodeMatcher.FindMethodBySignature(
            null,
            Signature,
            methodFilter: IsTargetCandidate);
        if (target == null)
            throw new MissingMethodException("Could not find the AutoSaveRelaxScores target method.");

        return target;
    }

    private static bool IsTargetCandidate(MethodInfo method)
    {
        if (method.IsStatic || method.ReturnType != typeof(void))
            return false;

        var parameters = method.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
    }

    [UsedImplicitly]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();

        var signatureStart = -1;
        for (var start = 0; start <= code.Count - Signature.Length; start++)
        {
            var matches = true;
            for (var offset = 0; offset < Signature.Length; offset++)
            {
                if (code[start + offset].opcode != Signature[offset])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                signatureStart = start;
                break;
            }
        }

        if (signatureStart < 0)
            throw new InvalidOperationException("Could not find the full AutoSaveRelaxScores signature.");

        // The two brtrue instructions are the early returns for Relax and Relax2.
        // Their condition leaves one bool on the evaluation stack. Pop that bool
        // instead of emitting a new branch label; this keeps all original labels,
        // branch operands, and exception-block metadata untouched.
        foreach (var offset in new[] { 18, 30 })
        {
            var instruction = code[signatureStart + offset];
            instruction.opcode = OpCodes.Pop;
            instruction.operand = null;
        }

        return code;
    }
}
