using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace Osu.Utils.Extensions;

public static class TranspilerExtensions
{
    public static IEnumerable<CodeInstruction> NoopAfterSignature(
        this IEnumerable<CodeInstruction> instructions,
        OpCode[] signature,
        uint replaceAfterSignature,
        bool all = false)
    {
        var replacementRemaining = replaceAfterSignature;
        var sequentialMatching = 0;
        var found = false;

        foreach (var instruction in instructions)
        {
            if (signature.Length > 0 && replacementRemaining > 0 && sequentialMatching == signature.Length)
            {
                found = true;
                replacementRemaining--;

                instruction.opcode = OpCodes.Nop;
                instruction.operand = null;

                yield return instruction;
            }
            else
            {
                if (signature.Length > 0)
                {
                    if (replaceAfterSignature <= 0 && all)
                    {
                        replacementRemaining = replaceAfterSignature;
                        sequentialMatching = 0;
                    }

                    if (replacementRemaining == replaceAfterSignature &&
                        instruction.opcode == signature[sequentialMatching])
                        sequentialMatching++;
                    else
                        sequentialMatching = 0;
                }

                yield return instruction;
            }
        }

        if (signature.Length > 0 && !found)
            throw new Exception("Could not find the target signature in method!");

        if (found && replacementRemaining > 0 && replaceAfterSignature != replacementRemaining)
            throw new Exception("Not enough space in method to noop more instructions!");
    }
}
