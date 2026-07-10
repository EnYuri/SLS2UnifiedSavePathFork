using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Modding;
using System;

namespace UnifiedSavePath;

[ModInitializer("Initialize")]
public static class UnifiedSavePathMod
{
    public static void Initialize()
    {
        var harmony = new Harmony("com.unifiedsavepath.sts2");
        harmony.PatchAll(typeof(UnifiedSavePathMod).Assembly);

        // Also force the backing field to false in case it was already set
        UserDataPathProvider.IsRunningModded = false;
    }
}

// Patch the getter in case it's called directly
[HarmonyPatch(typeof(UserDataPathProvider), "get_IsRunningModded")]
public static class PatchGetIsRunningModded
{
    [HarmonyPrefix]
    public static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

// Patch the setter so it can never be set to true
[HarmonyPatch(typeof(UserDataPathProvider), "set_IsRunningModded")]
public static class PatchSetIsRunningModded
{
    [HarmonyPrefix]
    public static bool Prefix(ref bool value)
    {
        value = false;
        return true; // run original setter with value=false
    }
}

// Patch GetProfileDir directly as a safety net against JIT inlining.
[HarmonyPatch(typeof(UserDataPathProvider), nameof(UserDataPathProvider.GetProfileDir), new Type[] { typeof(int) })]
public static class PatchGetProfileDirByProfileId
{
    [HarmonyPrefix]
    public static bool Prefix(int profileId, ref string __result)
    {
        __result = $"profile{profileId}";
        return false;
    }
}

// Beta builds can explicitly request a modded or unmodded profile directory.
// Force those calls back to the unmodded path instead of bypassing IsRunningModded.
[HarmonyPatch(typeof(UserDataPathProvider), nameof(UserDataPathProvider.GetProfileDir), new Type[] { typeof(int), typeof(bool?) })]
public static class PatchGetProfileDirWithForcedModState
{
    [HarmonyPrefix]
    public static bool Prefix(ref bool? forceModState)
    {
        forceModState = false;
        return true;
    }
}

[HarmonyPatch(typeof(UserDataPathProvider), nameof(UserDataPathProvider.GetAccountDir), new Type[] { typeof(bool?) })]
public static class PatchGetAccountDirWithForcedModState
{
    [HarmonyPrefix]
    public static bool Prefix(ref bool? forceModState)
    {
        forceModState = false;
        return true;
    }
}
