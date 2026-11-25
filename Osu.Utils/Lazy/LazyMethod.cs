using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using JetBrains.Annotations;
using Osu.Utils.IL;

namespace Osu.Utils.Lazy;

/// <summary>
///     A reference to a method that gets located at runtime and invoked reflectively.
/// </summary>
[PublicAPI]
public class LazyMethod : ILazy<MethodInfo>
{
    private readonly Lazy<MethodInfo> _lazy;

    public LazyMethod(string name, Func<MethodInfo> action)
    {
        Name = name;
        _lazy = new Lazy<MethodInfo>(action);
    }

    public string Name { get; }

    public MethodInfo Reference => this.GetReference(Name, _lazy);

    public override string ToString() => $"{nameof(LazyMethod)}({Name})";

    public void Invoke(object? instance = null, object?[]? parameters = null) =>
        Reference.Invoke(instance, parameters);

    public static LazyMethod ByPartialSignature(string name, IReadOnlyList<OpCode> signature, Type? type = null) =>
        new(name, () => OpCodeMatcher.FindMethodBySignature(type, signature)!);
}
