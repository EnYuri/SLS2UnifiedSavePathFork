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
    // Distinct from the upstream mod's "com.unifiedsavepath.sts2". Sharing it would make the
    // HasAnyPatches guard below mistake upstream's patches for our own and skip installing ours -
    // exactly wrong when upstream's PatchAll applied some patches and then threw.
    private const string HarmonyId = "com.unifiedsavepathforked.sts2";
    private const string ProviderTypeName = "MegaCrit.Sts2.Core.Saves.UserDataPathProvider";
    private const string ModManagerTypeName = "MegaCrit.Sts2.Core.Modding.ModManager";
    private const int PatchCount = 6;

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

        var accountDir = AccessTools.Method(provider, "GetAccountDir", new[] { typeof(bool?) });
        applied += TryPatch(harmony, accountDir, nameof(Patches.ForceModStateOfArg0), "GetAccountDir(bool?)");

        // The game copies vanilla saves into modded/ the first time it runs modded. With the paths
        // above unified, every "copy" is a file onto itself, and the game then tells the player
        // their saves were moved to a separate modded location. The prefix decides at call time,
        // from the live path results, so a build where the path patches did not land keeps the copy.
        Patches.AccountDir = accountDir;
        Patches.ProfileDir = AccessTools.Method(provider, "GetProfileDir", new[] { typeof(int), typeof(bool?) });
        var modManager = AccessTools.TypeByName(ModManagerTypeName);
        applied += TryPatch(harmony,
            modManager == null ? null : AccessTools.Method(modManager, "CopyUnmoddedSaveFilesIfNeeded"),
            nameof(Patches.SkipSelfCopyOfSaves), "ModManager.CopyUnmoddedSaveFilesIfNeeded");

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
            LogInfo($"Applied {applied} of {PatchCount} patches. Modded runs now use the vanilla profile directory.");
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

    internal static void LogInfo(string message) => Write("Info", message);

    internal static void LogError(string message) => Write("Error", message);

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

    /// <summary>GetAccountDir(bool?) and GetProfileDir(int, bool?), invoked through their patches.</summary>
    internal static MethodInfo? AccountDir;
    internal static MethodInfo? ProfileDir;

    /// <summary>
    /// ModManager.CopyUnmoddedSaveFilesIfNeeded -> skipped while the game reads the vanilla
    /// directory. Its source and target are then the same file (or a modded/ copy nothing reads),
    /// and it would otherwise raise the "saves were copied" popup for a copy that separated nothing.
    ///
    /// This is the one prefix that replaces its target outright: there is no argument to rewrite,
    /// and the copy is the very split this mod exists to prevent. It still runs the original
    /// whenever the paths are not unified, or the check itself fails, so it only ever suppresses a
    /// copy nothing would read. The try/catch matters: an exception from a prefix would escape
    /// into ModManager.Initialize.
    /// </summary>
    internal static bool SkipSelfCopyOfSaves()
    {
        try
        {
            if (ModdedPathsAreUnified())
            {
                UnifiedSavePathMod.LogInfo("Skipping the game's first-time copy of saves into modded/: both paths are already the same directory.");
                return false;
            }
        }
        catch (Exception e)
        {
            UnifiedSavePathMod.LogError($"Could not check save paths; letting the game copy saves: {e.Message}");
        }

        return true;
    }

    /// <summary>
    /// True when the game's normal save lookups (forceModState left null) resolve to the vanilla
    /// directory, so a copy into modded/ is either a file onto itself or a copy nothing will read.
    /// Called through reflection, so it sees the patched methods exactly as the game does.
    /// </summary>
    private static bool ModdedPathsAreUnified()
    {
        if (AccountDir == null || AccountDir.Invoke(null, new object?[] { null }) as string != "")
        {
            return false;
        }

        // Absent on builds that predate forceModState on GetProfileDir; GetAccountDir alone decides there.
        return ProfileDir == null || ProfileDir.Invoke(null, new object?[] { 1, null }) as string == "profile1";
    }
}
