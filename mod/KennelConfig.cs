using System.Collections.Generic;
using OniFramework;
using TUNING;
using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// The Kennel: a 3x3 hole in a ranch room's back wall with a critter flap in its bottom-centre
    /// cell. It is a Critter Condo to the critters (vanilla's <c>CritterCondoInteractMontior</c>
    /// finds it and walks a hatch to the flap), a <see cref="CritterLedger"/> to the framework (a
    /// finished visit stores the hatch, brain stopped) and a <see cref="DepthBox"/> to the eye.
    ///
    /// It sits on the BUILDING object layer, drawn at the backwall scene layer. Not the backwall
    /// object layer the scope sketched: <c>RoomProber</c> lists a cavity's buildings from object
    /// layers 1 (Building) and 38 only, so a backwall-layer condo is never in
    /// <c>cavity.buildings</c> and the condo monitor (requireCavity) never finds it.
    /// </summary>
    public sealed class KennelConfig : IBuildingConfig
    {
        public const string Id = "BackroomsKennel";

        /// <summary>Green or unconnected admits; red releases all and admits none.</summary>
        public static readonly HashedString PortId = "BackroomsKennelOpen";

        /// <summary>Ruling D3 a: 2 critters per backspace cell, 3x3 = 9 cells.</summary>
        public const int BackspaceCells = 9;

        public const int Capacity = 2 * BackspaceCells;

        public const float FeedKg = 200f;

        public static readonly Tag FeedTag = SimHashes.SandStone.CreateTag();

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(Id, 3, 3, "kennel_cage_face_kanim", 30, 30f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER4, MATERIALS.ALL_METALS, 1600f, BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.NONE, noise: NOISE_POLLUTION.NONE);
            def.SceneLayer = Grid.SceneLayer.Backwall;
            // No backwall of its own: the room's faces are drawn from what
            // borders the Kennel (DepthBox.IsBorderCell), so it stands on a floor (OnFloor, watched
            // after it is built by RequiresFoundation) and needs a tile or backwall building in every
            // cell beside it and above it: refused at placement with NotEnclosedReason
            // (Patches.BuildingDef_IsValidBuildLocation), and after it is built Kennel shows
            // NotEnclosed and stops while one is missing.
            def.ContinuouslyCheckFoundation = true;
            def.PermittedRotations = PermittedRotations.FlipH; // as vanilla CritterCondoConfig: CritterCondoStates.SetFacing reads the condo's Rotatable
            def.Floodable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.LogicInputPorts = new List<LogicPorts.Port>
            {
                LogicPorts.Port.InputPort(PortId, new CellOffset(0, 0), "Kennel Open",
                    "Green Signal: critters may go in", "Red Signal: every critter is let out and none go in"),
            };
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            Prioritizable.AddRef(go);
            // Kennel.OnPrefabInit takes these three in this order: critters, feed, output.
            Storage critters = go.AddComponent<Storage>();
            critters.capacityKg = 10000f;
            critters.showInUI = false;
            critters.allowItemRemoval = false;
            critters.SetDefaultStoredItemModifiers(new List<Storage.StoredItemModifier>
            {
                Storage.StoredItemModifier.Hide,
                Storage.StoredItemModifier.Seal,
            });

            Storage feed = go.AddComponent<Storage>();
            feed.capacityKg = FeedKg;
            feed.showInUI = true;
            feed.storageFilters = new List<Tag> { FeedTag };
            feed.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);
            ManualDeliveryKG delivery = go.AddComponent<ManualDeliveryKG>();
            delivery.SetStorage(feed);
            delivery.RequestedItemTag = FeedTag;
            delivery.capacity = FeedKg;
            delivery.refillMass = FeedKg / 2f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.RanchingFetch.IdHash;

            Storage output = go.AddComponent<Storage>();
            output.capacityKg = 2000f;
            output.showInUI = true;
            output.allowItemRemoval = true;
            output.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            CritterLedger ledger = go.AddOrGet<CritterLedger>();
            ledger.Capacity = Capacity;
            ledger.BackspaceCells = BackspaceCells;
            // The cage face kanim draws its own bars; the DepthBox shader's bars would double them.
            go.AddOrGet<DepthBox>().Bars = false;
            go.AddOrGet<Kennel>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            // A Critter Condo to every adult that has the condo behaviour (CritterCondoConfig's own
            // lines, without its room requirement: the Kennel is the room's back wall).
            go.GetComponent<KPrefabID>().AddTag("CritterCondo");
            CritterCondo.Def def = go.AddOrGetDef<CritterCondo.Def>();
            def.IsCritterCondoOperationalCb = smi => smi.GetComponent<Kennel>().AcceptsVisits;
            def.moveToStatusItem = new StatusItem("CRITTERCONDO.MOVINGTO", "CREATURES", "", StatusItem.IconType.Info,
                NotificationType.Neutral, allow_multiples: false, OverlayModes.None.ID);
            def.interactStatusItem = new StatusItem("CRITTERCONDO.INTERACTING", "CREATURES", "", StatusItem.IconType.Info,
                NotificationType.Neutral, allow_multiples: false, OverlayModes.None.ID);
            def.effectId = "InteractedWithCritterCondo";
        }

        /// <summary>Why a Kennel cannot be placed (the build tool's red tooltip).</summary>
        internal const string NotEnclosedReason = "Must have a tile or backwall building in every cell beside it and above it";

        internal const string NotEnclosedId = "BackroomsKennelNotEnclosed";

        internal static void InstallStrings()
        {
            string status = "STRINGS.BUILDING.STATUSITEMS." + NotEnclosedId.ToUpperInvariant() + ".";
            Strings.Add(status + "NAME", "Not Enclosed");
            Strings.Add(status + "TOOLTIP", "The room behind this Kennel is drawn from the tiles and backwalls around it.\n\n"
                + "Build a tile or backwall building in every cell beside it and above it.");
            string prefix = "STRINGS.BUILDINGS.PREFABS." + Id.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Kennel");
            Strings.Add(prefix + "DESC", "Critters behind the wall are out of the way, but still part of the ranch.");
            Strings.Add(prefix + "EFFECT", "Holds up to " + Capacity + " tame hatches behind the room's back wall. "
                + "They eat Sandstone from its bin, and their Coal and eggs collect in it. They come out to be groomed. "
                + "Adds " + BackspaceCells + " cells to the room for overcrowding.");
        }
    }
}
