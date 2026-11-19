using System;

namespace Osu.Patcher.Hook.Patches;

/// <summary>
///     A marker identifying a patch that should be applied by the hook.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class OsuPatch : Attribute;