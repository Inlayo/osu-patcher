using System;

namespace Osu.Patcher.Hook.Patches;

/// <summary>
///     A marker for patch classes that target methods in osu!.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class OsuPatch : Attribute;
