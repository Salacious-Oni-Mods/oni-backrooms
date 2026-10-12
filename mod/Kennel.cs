using System.Collections.Generic;
using OniFramework;
using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// The Kennel's glue: wires its three storages into the <see cref="CritterLedger"/>, decides who
    /// may go in (tame adult hatches), keeps the <see cref="DepthBox"/>'s sprites in step
    /// with who is inside, and answers the logic port and the Release all button.
    /// Held critters also go out on their own, at most <see cref="OutingsPerCycle"/> times a cycle each,
    /// and walk back in through the condo. It admits nobody while it is not
    /// operational, which is how vanilla's OnBackWall rule reports a missing back wall.
    /// </summary>
    public sealed class Kennel : KMonoBehaviour, ISim4000ms
    {
        /// <summary>Outings a held critter takes on its own per cycle.</summary>
        public const int OutingsPerCycle = 2;

        // 150 ticks of 4 s in a 600 s cycle: this chance per tick spends the budget over about a cycle.
        private const float OutingChance = OutingsPerCycle / 150f;

        private static readonly EventSystem.IntraObjectHandler<Kennel> OnLogicDelegate =
            new EventSystem.IntraObjectHandler<Kennel>((k, data) => k.OnLogic(data));

        private static readonly EventSystem.IntraObjectHandler<Kennel> OnMenuDelegate =
            new EventSystem.IntraObjectHandler<Kennel>((k, data) => k.OnRefreshUserMenu());

        // The condo's own state change replays "on"/"off" on these, so the look is put back a frame later.
        private static readonly EventSystem.IntraObjectHandler<Kennel> OnCondoStateDelegate =
            new EventSystem.IntraObjectHandler<Kennel>((k, data) =>
                GameScheduler.Instance.ScheduleNextFrame("Kennel look", _ => { if (k != null) { k.RefreshLook(); } }));

        private static readonly EventSystem.IntraObjectHandler<Kennel> OnStorageDelegate =
            new EventSystem.IntraObjectHandler<Kennel>((k, data) => k.RefreshLookIfStale());

        // Ending a condo visit plays "on" (CritterCondoStates.UnreserveCondo); a visitor the ledger
        // turns away at the door is not stored, so no OpenDoor puts the look back. A frame later
        // the stale front is replaced; a door already moving is left alone.
        private static readonly EventSystem.IntraObjectHandler<Kennel> OnTagsDelegate =
            new EventSystem.IntraObjectHandler<Kennel>((k, data) =>
            {
                if (data is Boxed<TagChangedEventData> b && !b.value.added && b.value.tag == GameTags.Creatures.ReservedByCreature)
                {
                    GameScheduler.Instance.ScheduleNextFrame("Kennel look", _ => { if (k != null) { k.RefreshLookIfStale(); } });
                }
            });

        private CritterLedger ledger;
        private DepthBox box;
        private Operational operational;
        // Outings taken this cycle, by critter instance id; not saved (a load resets the budget).
        private readonly Dictionary<int, int> outings = new Dictionary<int, int>();
        private int outingsCycle = -1;

        /// <summary>False stops outings (the KENNEL rig, while it counts).</summary>
        internal bool OutingsOn = true;

        /// <summary>Outings taken since spawn (for the rig).</summary>
        internal int OutingsTaken { get; private set; }

        /// <summary>False while the logic port reads red (the KENNEL rig also shuts it, without a release).</summary>
        public bool Open
        {
            get { return open; }
            internal set
            {
                if (open != value)
                {
                    open = value;
                    RefreshLook();
                }
            }
        }

        private bool open = true;

        /// <summary>The condo callback: a hatch may start the walk in.</summary>
        public bool AcceptsVisits
        {
            get { return Open && IsOperational && ledger != null && ledger.Count < ledger.Capacity; }
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Storage[] s = GetComponents<Storage>();
            ledger = GetComponent<CritterLedger>();
            ledger.Critters = s[0];
            ledger.Feed = s[1];
            ledger.Output = s[2];
            ledger.Admits = Admits;
            box = GetComponent<DepthBox>();
            operational = GetComponent<Operational>();
        }

        private static readonly Operational.Flag EnclosedFlag = new Operational.Flag("backrooms_enclosed", Operational.Flag.Type.Functional);
        private static StatusItem notEnclosed;
        private System.Guid notEnclosedHandle;

        // Off while RequiresFoundation finds no floor (OnFloor) or a cell beside or above it is open.
        private bool IsOperational
        {
            get { return operational == null || operational.IsOperational; }
        }

        // Tiles and backwalls around it can be dug out or deconstructed after it is built.
        private void CheckEnclosed()
        {
            Extents a = box.Area;
            bool ok = DepthBox.FirstOpenBorderCell(a.x, a.y, a.width, a.height, false) < 0;
            operational?.SetFlag(EnclosedFlag, ok);
            KSelectable sel = GetComponent<KSelectable>();
            if (sel == null)
            {
                return;
            }
            if (notEnclosed == null)
            {
                notEnclosed = new StatusItem(KennelConfig.NotEnclosedId, "BUILDING", "status_item_missing_foundation",
                    StatusItem.IconType.Custom, NotificationType.BadMinor, false, OverlayModes.None.ID);
            }
            notEnclosedHandle = sel.ToggleStatusItem(notEnclosed, notEnclosedHandle, !ok);
        }

        public void Sim4000ms(float dt)
        {
            CheckEnclosed();
            RefreshLookIfStale();
            int cycle = GameClock.Instance.GetCycle();
            if (cycle != outingsCycle)
            {
                outingsCycle = cycle;
                outings.Clear();
            }
            // Only through an open, working door, so they can walk back in.
            if (!OutingsOn || !Open || !IsOperational)
            {
                return;
            }
            foreach (GameObject go in ledger.HeldCritters)
            {
                int id = go.GetInstanceID();
                outings.TryGetValue(id, out int used);
                if (used < OutingsPerCycle && Random.value < OutingChance)
                {
                    outings[id] = used + 1;
                    OutingsTaken++;
                    ledger.Release(go, "an outing");
                }
            }
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            ledger.OnStored += _ => { Refresh(); OpenDoor(); };
            ledger.OnReleased += (critter, __) =>
            {
                Refresh();
                OpenDoor();
                // Out of the door either way round, not all facing the condo's side.
                critter.GetComponent<Facing>()?.SetFacing(Random.value < 0.5f);
            };
            Subscribe((int)GameHashes.LogicEvent, OnLogicDelegate);
            Subscribe((int)GameHashes.RefreshUserMenu, OnMenuDelegate);
            Subscribe((int)GameHashes.OperationalChanged, OnCondoStateDelegate);
            Subscribe((int)GameHashes.UpdateRoom, OnCondoStateDelegate);
            Subscribe((int)GameHashes.OnStorageChange, OnStorageDelegate);
            Subscribe((int)GameHashes.TagsChanged, OnTagsDelegate);
            CheckEnclosed();
            Refresh();
            KBatchedAnimController anim = GetComponent<KBatchedAnimController>();
            if (anim != null)
            {
                SetLights(anim);
                // Again a frame later, in case the front-layer controller did not exist yet.
                GameScheduler.Instance.ScheduleNextFrame("Kennel lights", _ => { if (this != null && anim != null) { SetLights(anim); } });
            }
        }

        private bool Admits(GameObject critter)
        {
            // The species is a CreatureBrain field, never a tag (EntityTemplates.AddCreatureBrain).
            CreatureBrain brain = critter.GetComponent<CreatureBrain>();
            return Open && IsOperational && brain != null && brain.species == GameTags.Creatures.Species.HatchSpecies
                && !critter.HasTag(GameTags.Creatures.Wild) && critter.GetSMI<BabyMonitor.Instance>() == null;
        }

        private void Refresh()
        {
            List<Sprite> sprites = new List<Sprite>();
            foreach (GameObject go in ledger.HeldCritters)
            {
                Sprite s = Def.GetUISprite(go).first;
                if (s != null)
                {
                    sprites.Add(s);
                }
            }
            box.SetOccupants(sprites);
        }

        // The cage door slides open and shut for every critter that goes in or comes out. Vanilla
        // plays cc_working_* on the condo during the visit (CritterCondoStates.PlayCondoBuildingAnim),
        // but the ledger stores the critter at the threshold, before interact.pre, and ending the
        // visit plays "on" (UnreserveCondo), so nothing would ever open it. A door already moving is
        // left to finish: one cycle covers a burst (Release all, several walking in at once).
        private void OpenDoor()
        {
            KBatchedAnimController anim = GetComponent<KBatchedAnimController>();
            if (anim == null)
            {
                return;
            }
            SetLights(anim);
            HashedString current = anim.CurrentAnim != null ? anim.CurrentAnim.hash : HashedString.Invalid;
            if (current == AccessPre || current == Access)
            {
                return;
            }
            anim.Play(AccessPre);
            anim.Queue(Access);
            anim.Queue(AccessPst);
            anim.Queue(Look, KAnim.PlayMode.Loop);
        }

        // The door the Kennel shows when it is still, most urgent first: the red paw
        // ("alert") while the feed bin is empty, since held hatches cannot eat; the padlock ("locked")
        // while the logic port has shut it; else the paw, under the name the condo's state plays.
        // The lights are not part of it: SetLights shows them over whichever door is playing.
        private HashedString Look
        {
            get
            {
                if (ledger != null && ledger.Feed != null && ledger.Feed.MassStored() < MinFeedKg)
                {
                    return AlertAnim;
                }
                if (!Open)
                {
                    return LockedAnim;
                }
                return AcceptsVisits ? OnAnim : OffAnim;
            }
        }

        // The bin's last crumbs after a meal are not feed.
        private const float MinFeedKg = 0.01f;

        // Feed eaten or delivered changes the look without any condo event, and the condo's visit
        // replays "on" when it ends (UnreserveCondo); this puts the look back when the door is still.
        private void RefreshLookIfStale()
        {
            KBatchedAnimController anim = GetComponent<KBatchedAnimController>();
            if (anim == null || anim.CurrentAnim == null)
            {
                return;
            }
            SetLights(anim);
            HashedString current = anim.CurrentAnim.hash;
            if (current != AccessPre && current != Access && current != AccessPst && current != Look)
            {
                RefreshLook();
            }
        }

        // Vanilla CritterCondo's state machine moves between "on" and "off" only on OperationalChanged
        // and UpdateRoom, and plays just those two. Open (the logic port, the rigs) changes
        // AcceptsVisits without either, so a red port left the front lit and its strips green. This
        // moves the condo to the state AcceptsVisits says and shows Look; a full ledger needs nothing
        // here, OpenDoor queues Look for it.
        private void RefreshLook()
        {
            KBatchedAnimController anim = GetComponent<KBatchedAnimController>();
            CritterCondo.Instance smi = gameObject.GetSMI<CritterCondo.Instance>();
            if (anim == null)
            {
                return;
            }
            SetLights(anim);
            if (smi == null)
            {
                return;
            }
            bool accepts = AcceptsVisits;
            HashedString end = Look;
            HashedString current = anim.CurrentAnim != null ? anim.CurrentAnim.hash : HashedString.Invalid;
            if (current == AccessPre || current == Access || current == AccessPst)
            {
                // Let the door finish, then end on the new look.
                anim.ClearQueue();
                if (current == AccessPre)
                {
                    anim.Queue(Access);
                }
                if (current != AccessPst)
                {
                    anim.Queue(AccessPst);
                }
                anim.Queue(end, KAnim.PlayMode.Loop);
                return;
            }
            StateMachine.BaseState target = accepts ? (StateMachine.BaseState)smi.sm.operational : smi.sm.inoperational;
            if (!smi.IsInsideState(target))
            {
                // Entering the state plays its own "on"/"off".
                smi.GoTo(target);
            }
            if (anim.CurrentAnim == null || anim.CurrentAnim.hash != end)
            {
                anim.Play(end, KAnim.PlayMode.Loop);
            }
        }

        // The front's two lights. The top lights (header and corner plates) say
        // whether the Kennel is full: yellow while it is not, red while it is. The jamb strips are
        // the door light: green while hatches can walk in (Open and operational), red while they
        // cannot. Every world anim carries both colours of each; this hides the ones that are off.
        // Hidden symbols belong to the controller, so they hold across anims
        // (KAnimControllerBase.SetSymbolVisiblity).
        // The lights are FG symbols, and the game does not draw FG symbols with the building's own
        // controller: KAnimLayering.HideSymbols hides them there and draws them with a child
        // controller on the front layer. Hiding them on the building's controller changed nothing on
        // screen, so every light showed and the red twins, drawn after
        // the yellow and green ones, covered them: red top lights and red strips all the time.
        private void SetLights(KBatchedAnimController anim)
        {
            bool full = ledger != null && ledger.Count >= ledger.Capacity;
            bool door = Open && IsOperational;
            KAnimControllerBase front = FrontController(anim);
            front.SetSymbolVisiblity(LampTop, !full);
            front.SetSymbolVisiblity(LampBottom, !full);
            front.SetSymbolVisiblity(LampTopRed, full);
            front.SetSymbolVisiblity(LampBottomRed, full);
            front.SetSymbolVisiblity(Strips, door);
            front.SetSymbolVisiblity(StripsRed, !door);
        }

        /// <summary>The controller that draws the Kennel's FG symbols: KAnimLayering's front-layer
        /// child of <paramref name="anim"/>, or <paramref name="anim"/> itself while there is none.</summary>
        public static KAnimControllerBase FrontController(KBatchedAnimController anim)
        {
            foreach (KBatchedAnimController c in anim.GetComponentsInChildren<KBatchedAnimController>(true))
            {
                if (c != anim && c.GetLayering() != null && c.GetLayering().GetIsLayer())
                {
                    return c;
                }
            }
            return anim;
        }

        private static readonly KAnimHashedString LampTop = "kennel_cage_face_lamp_top";
        private static readonly KAnimHashedString LampBottom = "kennel_cage_face_lamp_bottom";
        private static readonly KAnimHashedString LampTopRed = "kennel_cage_face_lamp_top_red";
        private static readonly KAnimHashedString LampBottomRed = "kennel_cage_face_lamp_bottom_red";
        private static readonly KAnimHashedString Strips = "kennel_cage_face_indicator";
        private static readonly KAnimHashedString StripsRed = "kennel_cage_face_indicator_red";

        private static readonly HashedString AccessPre = "access_pre";
        private static readonly HashedString Access = "access";
        private static readonly HashedString AccessPst = "access_pst";
        private static readonly HashedString OnAnim = "on";
        private static readonly HashedString OffAnim = "off";
        private static readonly HashedString AlertAnim = "alert";
        private static readonly HashedString LockedAnim = "locked";

        private void OnLogic(object data)
        {
            LogicValueChanged change = (LogicValueChanged)data;
            if (change.portID != KennelConfig.PortId)
            {
                return;
            }
            Open = LogicCircuitNetwork.IsBitActive(0, change.newValue);
            if (!Open)
            {
                ledger.ReleaseAll("the logic port went red");
            }
        }

        private void OnRefreshUserMenu()
        {
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo("action_empty_contents",
                "Release all", () => ledger.ReleaseAll("Release all"),
                tooltipText: "Let every critter out of the Kennel. They walk back in on their own."));
        }
    }
}
