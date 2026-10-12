using HarmonyLib;
using KMod;

namespace Backrooms
{
    // Backrooms proof of concept. One building, the Kennel, on the two OniFramework.Library 1.2
    // surfaces it was written to prove: CritterLedger (live critters held in a Storage with their
    // brains stopped) and DepthBox (the room behind the wall). Needs the library only, not the SimDLL.
    //
    // Dev builds with BACKROOMS_RIGS (see Backrooms.csproj) also carry the rigs, which need OniFramework:
    //   --backrooms-kennel          KennelRig: 18 tame hatches in a ranch room, before/after brain count
    //   --backrooms-bench-vanilla   KennelBenchRig BENCHV: 306 hatches in 17 Stables, no Kennel
    //   --backrooms-bench-kennel    KennelBenchRig BENCHK: the same with a Kennel in every pen
    public class Mod : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            // base.OnLoad applies every [HarmonyPatch] in this assembly.
            base.OnLoad(harmony);

            // Library 1.3: DepthBox.FirstOpenBorderCell (placement and enclosure); 1.2 CritterLedger, DepthBox.
            OniFramework.LibraryVersion.Require(1, 3, "Backrooms");

            // The ledger's two patches: condo visit -> stored, and backspace cells for overcrowding.
            OniFramework.CritterLedger.Install(harmony);

#if BACKROOMS_RIGS
            // BOTH HALVES ARE NEEDED (see mod2-matter-environment/Mod.cs): BlueprintWorld and this
            // Install arm the WORLD; the RigRegistry.Register in Patches.cs schedules the RIG.
            OniFramework.BlueprintWorld.Install(harmony);
            OniFramework.BlueprintRig.Install(harmony, "Backrooms", "KENNEL",
                KennelRig.Flag, "blueprints/kennel.yaml", typeof(KennelRig));
            // The bench's two arms; each Install returns at once unless its own flag is given.
            OniFramework.BlueprintRig.Install(harmony, "Backrooms", "BENCHV",
                KennelBenchVanillaRig.Flag, "blueprints/bench-vanilla.yaml", typeof(KennelBenchRig));
            OniFramework.BlueprintRig.Install(harmony, "Backrooms", "BENCHK",
                KennelBenchKennelRig.Flag, "blueprints/bench-kennel.yaml", typeof(KennelBenchRig));
#endif
        }
    }
}
