using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace UnifiedSavePath;

/// <summary>
/// Forces Slay the Spire 2 to resolve the unmodded save path even while mods are loaded, so vanilla
/// and modded play share one profile directory.
///
/// The game reports "Assembly DLL for mod {id} failed to initialize!" whenever <see cref="Initialize"/>
/// throws, and the mod then contributes nothing at all - not even the patches that would have worked.
/// Harmony's PatchAll is all-or-nothing in exactly that way: one target it cannot resolve on the
/// player's game build takes down every other patch with it. So targets are resolved by name at
/// runtime and applied one at a time, and nothing here is allowed to escape as an exception.
/// </summary>
[ModInitializer("Initialize")]
public static class UnifiedSavePathMod
{
    private const string HarmonyId = "com.unifiedsavepath.sts2";
    private const string ProviderTypeName = "MegaCrit.Sts2.Core.Saves.UserDataPathProvider";

    private static bool _initialized;

    public static void Initialize()
    {
        try
        {
            Apply();
        }
        catch (Exception e)
        {
            LogError($"Initialization failed, save paths are unchanged: {e}");
        }
    }

    private static void Apply()
    {
        if (_initialized || Harmony.HasAnyPatches(HarmonyId))
        {
            LogInfo("Patches are already installed; skipping. The mod is probably installed twice.");
            return;
        }

        _initialized = true;

        var provider = AccessTools.TypeByName(ProviderTypeName);
        if (provider == null)
        {
            LogError($"Could not find {ProviderTypeName}; this game build moved the save API. Mod is inactive.");
            return;
        }

        var harmony = new Harmony(HarmonyId);
        var applied = 0;

        // Every save path funnels through GetAccountDir, which returns "modded" only when the mod
        // state is true, so pinning that state to false at each entry point is what unifies the
        // directories. The patches overlap on purpose: the setter covers the assignment the game
        // makes after mod loading, the getter covers everything that reads the state afterwards,
        // and the forceModState patches cover beta callers that ask for a mod state explicitly.
        applied += TryPatch(harmony, AccessTools.PropertyGetter(provider, "IsRunningModded"),
            nameof(Patches.ForceGetterFalse), "IsRunningModded getter");

        applied += TryPatch(harmony, AccessTools.PropertySetter(provider, "IsRunningModded"),
            nameof(Patches.ForceSetterFalse), "IsRunningModded setter");

        applied += TryPatch(harmony, AccessTools.Method(provider, "GetProfileDir", new[] { typeof(int) }),
            nameof(Patches.ForceUnmoddedProfileDir), "GetProfileDir(int)");

        applied += TryPatch(harmony, AccessTools.Method(provider, "GetProfileDir", new[] { typeof(int), typeof(bool?) }),
            nameof(Patches.ForceModStateOfArg1), "GetProfileDir(int, bool?)");

        applied += TryPatch(harmony, AccessTools.Method(provider, "GetAccountDir", new[] { typeof(bool?) }),
            nameof(Patches.ForceModStateOfArg0), "GetAccountDir(bool?)");

        // Belt and braces in case the getter patch was the one that failed: clear the stored state
        // too. Harmless when the getter patch did land, since nothing reads the backing field.
        try
        {
            AccessTools.PropertySetter(provider, "IsRunningModded")?.Invoke(null, new object[] { false });
        }
        catch (Exception e)
        {
            LogError($"Could not reset IsRunningModded: {e.Message}");
        }

        if (applied == 0)
        {
            LogError("No patch could be applied; vanilla and modded saves stay separate.");
        }
        else
        {
            LogInfo($"Applied {applied} of 5 patches. Modded runs now use the vanilla profile directory.");
        }
    }

    /// <summary>
    /// Applies one prefix, reporting rather than throwing when the target is absent on this game
    /// build or Harmony rejects the patch. Returns 1 when the patch landed.
    /// </summary>
    private static int TryPatch(Harmony harmony, MethodBase? target, string prefixName, string label)
    {
        if (target == null)
        {
            LogInfo($"Skipping {label}: not present in this game build.");
            return 0;
        }

        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches), prefixName)));
            return 1;
        }
        catch (Exception e)
        {
            LogError($"Could not patch {label}: {e}");
            return 0;
        }
    }

    private static void LogInfo(string message) => Write("Info", message);

    private static void LogError(string message) => Write("Error", message);

    /// <summary>
    /// Writes to the game log without binding to it at compile time, so a moved logging API costs a
    /// log line instead of the whole mod.
    /// </summary>
    private static void Write(string level, string message)
    {
        var text = "[UnifiedSavePath] " + message;

        try
        {
            var logType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Logging.Log");
            var method = logType == null
                ? null
                : AccessTools.Method(logType, level, new[] { typeof(string), typeof(int) });

            if (method != null)
            {
                method.Invoke(null, new object[] { text, 2 });
                return;
            }
        }
        catch
        {
            // Fall through to stdout, which Godot captures.
        }

        Console.WriteLine(text);
    }
}

/// <summary>
/// Prefix bodies. These deliberately take no Slay the Spire 2 types, so the assembly holds no
/// compile-time reference to the save API and cannot fail to load when that API changes.
///
/// Arguments are injected positionally (<c>__0</c>, <c>__1</c>) rather than by name: a parameter
/// rename in a future game build would otherwise turn a resolvable target into a patch-time
/// exception.
/// </summary>
internal static class Patches
{
    /// <summary>UserDataPathProvider.IsRunningModded getter -> always false.</summary>
    internal static bool ForceGetterFalse(ref bool __result)
    {
        __result = false;
        return false;
    }

    /// <summary>UserDataPathProvider.IsRunningModded setter -> store false whatever was passed.</summary>
    internal static void ForceSetterFalse(ref bool __0)
    {
        __0 = false;
    }

    /// <summary>
    /// UserDataPathProvider.GetProfileDir(int) -> the unmodded directory, computed directly.
    ///
    /// This wrapper is small enough for the JIT to inline GetAccountDir into it, which would make
    /// the getter patch above invisible here, so the result is produced outright. It matches the
    /// original: GetAccountDir(false) is "" and "".PathJoin($"profile{id}") is "profile{id}".
    /// </summary>
    internal static bool ForceUnmoddedProfileDir(int __0, ref string __result)
    {
        __result = "profile" + __0;
        return false;
    }

    /// <summary>
    /// UserDataPathProvider.GetProfileDir(int, bool?) -> rewrites the forceModState argument and
    /// lets the original build the path, so other mods patching GetAccountDir still take effect.
    /// </summary>
    internal static void ForceModStateOfArg1(ref bool? __1)
    {
        __1 = false;
    }

    /// <summary>UserDataPathProvider.GetAccountDir(bool?) -> same, with forceModState first.</summary>
    internal static void ForceModStateOfArg0(ref bool? __0)
    {
        __0 = false;
    }
}
