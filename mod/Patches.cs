using HarmonyLib;
#if BACKROOMS_RIGS
using OniFramework;
#endif

namespace Backrooms
{
#if BACKROOMS_RIGS
    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn
    {
        // Game.OnSpawn runs again for every load in the same process; RigRegistry.Register does
        // not de-duplicate.
        private static bool registered, benchV, benchK;

        private static void Postfix()
        {
            if (!registered)
            {
                registered = RigRegistry.Register<KennelRig>("BackroomsKennelRig", KennelRig.Flag);
            }
            if (!benchV)
            {
                benchV = RigRegistry.Register<KennelBenchVanillaRig>("BackroomsBenchVanillaRig", KennelBenchVanillaRig.Flag);
            }
            if (!benchK)
            {
                benchK = RigRegistry.Register<KennelBenchKennelRig>("BackroomsBenchKennelRig", KennelBenchKennelRig.Flag);
            }
        }
    }
#endif

    [HarmonyPatch(typeof(GeneratedBuildings), nameof(GeneratedBuildings.LoadGeneratedBuildings))]
    internal static class GeneratedBuildings_LoadGeneratedBuildings
    {
        private static void Prefix()
        {
            KennelConfig.InstallStrings();
            // Next to the Critter Condo in Food > ranching. No tech yet: the PoC is placed by its rig.
            ModUtil.AddBuildingToPlanScreen(new HashedString("Food"), KennelConfig.Id, "ranching", "CritterCondo");
        }
    }

    // The Kennel's placement rule beyond OnFloor: a tile or backwall building in every cell beside it
    // and above it (DepthBox.FirstOpenBorderCell), with its own reason in the build tool's tooltip.
    [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.IsValidBuildLocation),
        new[] { typeof(UnityEngine.GameObject), typeof(int), typeof(Orientation), typeof(bool), typeof(string) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    internal static class BuildingDef_IsValidBuildLocation
    {
        private static void Postfix(BuildingDef __instance, int cell, ref bool __result, ref string fail_reason)
        {
            if (!__result || __instance.PrefabID != KennelConfig.Id)
            {
                return;
            }
            Grid.CellToXY(cell, out int x, out int y);
            int x0 = x - (__instance.WidthInCells - 1) / 2;
            if (OniFramework.DepthBox.FirstOpenBorderCell(x0, y, __instance.WidthInCells, __instance.HeightInCells, false) >= 0)
            {
                __result = false;
                fail_reason = KennelConfig.NotEnclosedReason;
            }
        }
    }
}
