using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Osu.Utils.Extensions;
using Osu.Utils.Lazy;

namespace Osu.Stubs.Tests;

internal abstract class StubAggregator
{
    public static IEnumerable<ILazy<MemberInfo>> CollectPlainStubs()
    {
        var types = Assembly.GetAssembly(typeof(Stub))!.GetTypes();
        return types
            .SelectMany(CollectStubFields)
            .Where(field => field.IsStatic)
            .Select(field => field.GetValue<ILazy<MemberInfo>>(null))
            .ToArray();
    }

    private static IEnumerable<FieldInfo> CollectStubFields(Type type) => type
        .GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
        .Where(field => field.GetCustomAttribute(typeof(Stub)) != null);
}
