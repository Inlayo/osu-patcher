using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Osu.Utils.IL;

public static class OpCodeMatcher
{
    /// <summary>
    ///     Search for a method inside the osu! assembly by an IL OpCode signature.
    /// </summary>
    /// <param name="searchType">The type to shrink searching to, otherwise all types will be searched.</param>
    /// <param name="signature">A set of sequential OpCodes to match.</param>
    /// <param name="entireMethod">Whether the signature is the entire method to search for.</param>
    /// <param name="methodFilter">An optional predicate to filter candidate methods before reading their IL.</param>
    /// <returns>The found method or null if none found.</returns>
    public static MethodInfo? FindMethodBySignature(
        Type? searchType,
        IReadOnlyList<OpCode> signature,
        bool entireMethod = false,
        Func<MethodInfo, bool>? methodFilter = null)
    {
        if (signature.Count <= 0) return null;

        var searchTypes = searchType == null ? OsuAssembly.Types : [searchType];

        foreach (var type in searchTypes)
        foreach (var method in type.GetMethods(BindingFlags.Instance
                                               | BindingFlags.Static
                                               | BindingFlags.Public
                                               | BindingFlags.NonPublic))
        {
            if (methodFilter != null && !methodFilter(method))
                continue;

            var instructions = method.GetMethodBody()?.GetILAsByteArray();
            if (instructions == null) continue;

            if (InstructionsMatchesSignature(instructions, signature, entireMethod))
                return method;
        }

        return null;
    }

    /// <summary>
    ///     Check if some IL byte instructions contain a certain set of OpCodes.
    /// </summary>
    /// <param name="ilInstructions">
    ///     Raw IL byte instruction data obtained through <see cref="MethodBody.GetILAsByteArray()" />.
    /// </param>
    /// <param name="signature">A set of sequential OpCodes to search for.</param>
    /// <param name="entireMethod">Whether the signature should be the entire method.</param>
    /// <returns>Whether the signature matches.</returns>
    private static bool InstructionsMatchesSignature(
        IReadOnlyList<byte> ilInstructions,
        IReadOnlyList<OpCode> signature,
        bool entireMethod)
    {
        var sequentialMatching = 0;
        foreach (var instruction in new OpCodeReader(ilInstructions).GetOpCodes())
        {
            if (instruction == signature[sequentialMatching])
                sequentialMatching++;
            else if (!entireMethod)
                sequentialMatching = 0;
            else return false;

            if (sequentialMatching == signature.Count)
                return true;
        }

        return false;
    }
}
