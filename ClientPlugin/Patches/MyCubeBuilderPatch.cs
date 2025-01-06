using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Sandbox.Game.Entities;

namespace ClientPlugin.Patches
{
    [HarmonyPatch(typeof(MyCubeBuilder))]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    // ReSharper disable once UnusedType.Global
    public static class MyCubeBuilderPatch
    {
        [HarmonyPrefix]
        [HarmonyAfter("Sections")]
        [HarmonyPatch(nameof(MyCubeBuilder.HandleGameInput))]
        private static bool HandleGameInputPrefix()
        {
            return Logic.Logic.Static.HandleGameInputPrefix();
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MyCubeBuilder.Draw))]
        private static bool DrawPrefix(MyCubeBuilder __instance)
        {
            return Logic.Logic.Static.DrawPrefix(__instance);
        }
    }
}