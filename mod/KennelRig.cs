using System;
using System.Collections;
using System.Collections.Generic;
using OniFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backrooms
{
    /// <summary>
    /// KENNEL (--backrooms-kennel): the Backrooms proof of concept on its canvas (kennel.yaml, one
    /// 84-cell ranch room with the Kennel and a Grooming Station), narrated for --record.
    ///
    /// 18 tame hatches and one rancher are spawned in the room. The on-screen count of running
    /// creature brains is taken with all 18 roaming, then again with all 18 in the Kennel (scope
    /// §5). Between, the hatches are given a window to walk in on their own through vanilla's condo
    /// behaviour; whoever has not walked in by its end is stored by the rig, so the run is not paced
    /// by one-at-a-time condo visits. Then the ledger is watched doing a brain's work (meals, Coal,
    /// a grooming trip) and the Release all path is pressed. Save/load is NOT covered by this arm.
    /// </summary>
    public class KennelRig : RigHarness
    {
        public const string Flag = "--backrooms-kennel";

        private const int Hatches = KennelConfig.Capacity;
        private const int RoomX0 = 8, RoomX1 = 21, FloorY = 4;
        private const int RancherX = 15;
        private const float WalkInSeconds = 60f;
        private const float LifeSeconds = 180f;

        protected override string ModTag => "Backrooms";

        protected override string RigTag => "KENNEL";

        protected override string BlueprintFile => "blueprints/kennel.yaml";

        protected override IEnumerator Run()
        {
            DemoNarrator.Begin("Backrooms: the Kennel",
                "18 hatches behind the back wall, still eating, pooping and counting, with their brains off");
            DemoNarrator.SetPhaseCount(5);

            DemoNarrator.Phase("Setup", "A ranch room with a Kennel in its back wall and a Grooming Station.");
            Kennel kennel = Object.FindObjectOfType<Kennel>();
            if (kennel == null)
            {
                Fail("the canvas placed no Kennel (BackroomsKennel at 11," + FloorY + ")");
                yield break;
            }
            CritterLedger ledger = kennel.GetComponent<CritterLedger>();
            DepthBox box = kennel.GetComponent<DepthBox>();
            Check(ledger.Critters != null && ledger.Feed != null && ledger.Output != null
                && ledger.Critters != ledger.Feed && ledger.Feed != ledger.Output, "the Kennel wired three separate storages into its ledger");
            Check(DepthBox.Shader != null, "DepthBox found a shader (" + (DepthBox.Shader != null ? DepthBox.Shader.name : "none") + ")");
            Operational op = kennel.GetComponent<Operational>();
            Check(op != null && op.IsOperational, "the Kennel stands on drywall and is operational (OnBackWall: back wall under all 9 cells)");
            RevealTestArea(RoomX0 - 1, FloorY - 1, RoomX1 + 1, FloorY + 6);
            DemoNarrator.FocusCameraToFit(RoomCells(), 2f);
            if (SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused)
            {
                SpeedControlScreen.Instance.Unpause(false);
            }
            // Speed 3 from here on, so both frame-time windows run at the same speed.
            SetSpeed(2);

            GameObject feed = ElementLoader.FindElementByHash(SimHashes.SandStone).substance.SpawnResource(
                kennel.transform.GetPosition(), KennelConfig.FeedKg, 300f, byte.MaxValue, 0);
            ledger.Feed.Store(feed, hide_popups: true);
            Check(ledger.Feed.MassStored() >= KennelConfig.FeedKg - 0.01f, "the feed bin holds " + ledger.Feed.MassStored() + " kg Sandstone");

            DemoNarrator.Phase("Before", "18 tame hatches roam the room, every brain running.");
            // Shut until Walk in (Open, not the logic port: no release), so every brain still runs.
            // No outings and no grooming trips until "Life behind the wall", so the counts below
            // are of the 18 alone (0.1.4: two hatches sent out to be groomed during Walk in).
            kennel.Open = false;
            kennel.OutingsOn = false;
            int groomMax = ledger.MaxOutForGrooming;
            ledger.MaxOutForGrooming = 0;
            // Every release, with its reason, for the per-hatch record.
            ledger.OnReleased += (go, why) => Log("KENNEL: released " + Id(go) + " (" + why + ")");
            List<GameObject> hatches = new List<GameObject>();
            for (int i = 0; i < Hatches; i++)
            {
                int x = RoomX0 + 1 + i % (RoomX1 - RoomX0 - 1);
                GameObject h = GameUtil.KInstantiate(Assets.GetPrefab("Hatch"),
                    Grid.CellToPosCBC(Grid.XYToCell(x, FloorY), Grid.SceneLayer.Creatures), Grid.SceneLayer.Creatures);
                h.SetActive(true);
                hatches.Add(h);
            }
            yield return null;
            // Vanilla's own tame: value 0 AND straight to the tame state. Setting the amount alone
            // never tames: the wild state's effect adds +1/120 per second (EntityTemplates wildEffect)
            // before its SIM_1000ms transition checks IsWild (> 0), so the hatch stays wild.
            foreach (GameObject h in hatches)
            {
                h.GetSMI<WildnessMonitor.Instance>().DebugTame();
            }
            GameObject rancher = SpawnRancher();
            yield return WaitReal(5f);
            int wild = hatches.FindAll(h => h.HasTag(GameTags.Creatures.Wild)).Count;
            Check(wild == 0, "all 18 hatches are tame (" + wild + " still wild)");
            Check(rancher != null, "a rancher with Ranching1 was spawned");

            CritterLedger.CountCreatureBrains(out int totalBefore, out int runningBefore);
            DemoNarrator.Stat("brains running (before)", runningBefore + " / " + totalBefore);
            float frameBefore = 0f;
            yield return MeanFrame(5f, ms => frameBefore = ms);
            DemoNarrator.Stat("frame (before)", frameBefore.ToString("F2") + " ms");
            Check(runningBefore == Hatches, "all " + Hatches + " hatch brains ran before (" + runningBefore + ")");

            DemoNarrator.Phase("Walk in", "Hatches use the Kennel as a Critter Condo: through the flap, and stored.");
            int walkedIn = 0;
            Action<GameObject> counter = _ => walkedIn++;
            ledger.OnStored += counter;
            kennel.Open = true;
            float walkCap = Time.realtimeSinceStartup + WalkInSeconds;
            // A hatch on a condo visit drawn on BuildingUse is vanilla's climb into the condo, on the
            // Kennel's face (a hatch being groomed is on BuildingUse too, at the station: not counted).
            // Logged per hatch when it starts and stops, with why the ledger did not take it.
            int climbFrames = 0;
            HashSet<GameObject> climbing = new HashSet<GameObject>();
            while (ledger.Count < Hatches && Time.realtimeSinceStartup < walkCap)
            {
                DemoNarrator.Stat("walked in", walkedIn.ToString());
                bool any = false;
                foreach (GameObject h in hatches)
                {
                    KBatchedAnimController a = h.GetComponent<KBatchedAnimController>();
                    Chore chore = h.GetComponent<ChoreDriver>()?.GetCurrentChore();
                    bool now = a != null && a.enabled && a.sceneLayer == Grid.SceneLayer.BuildingUse
                        && chore != null && chore.choreType.Id.StartsWith("CritterCondoStates");
                    any |= now;
                    if (now && climbing.Add(h))
                    {
                        Log("KENNEL: climb start " + Describe(h, ledger));
                    }
                    else if (!now && climbing.Remove(h))
                    {
                        Log("KENNEL: climb end " + Describe(h, ledger));
                    }
                }
                climbFrames += any ? 1 : 0;
                yield return null;
            }
            ledger.OnStored -= counter;
            DemoNarrator.Stat("walked in", walkedIn.ToString()); // the loop's last pass ran before the last store
            Check(walkedIn >= 1, walkedIn + " hatch(es) walked in on their own within " + WalkInSeconds + " s");
            Check(climbFrames == 0, "no hatch was seen climbing the Kennel's face (" + climbFrames + " frame(s))");
            int byRig = 0;
            foreach (GameObject h in hatches)
            {
                if (!h.HasTag(CritterLedger.Held))
                {
                    if (ledger.Store(h))
                    {
                        byRig++;
                    }
                    else
                    {
                        Log("KENNEL: rig store refused " + Describe(h, ledger));
                    }
                }
            }
            // Same frame as the stores: the next Sim4000ms may already send two out to be groomed.
            Check(ledger.Count == Hatches, "the Kennel holds " + ledger.Count + " / " + Hatches);
            DemoNarrator.Note(walkedIn + " walked in, " + byRig + " stored by the rig to save time");
            yield return WaitReal(1f);

            DemoNarrator.Phase("After", "The same 18 hatches, held behind the wall.");
            CritterLedger.CountCreatureBrains(out int totalAfter, out int runningAfter);
            int heldRunning = 0, heldVisible = 0;
            foreach (GameObject h in ledger.HeldCritters)
            {
                heldRunning += h.GetComponent<Brain>().IsRunning() ? 1 : 0;
                heldVisible += h.GetComponent<KBatchedAnimController>().IsVisible() ? 1 : 0;
            }
            DemoNarrator.Stat("brains running (after)", runningAfter + " / " + totalAfter);
            DemoNarrator.Compare(runningBefore + " of " + totalBefore + " brains running",
                runningAfter + " of " + totalAfter + " brains running", "BrainScheduler skips a stopped brain");
            Check(heldRunning == 0, "no held hatch's brain runs (" + heldRunning + " of " + ledger.Count + ")");
            Check(heldVisible == 0, "no held hatch is drawn as a critter (" + heldVisible + ")");
            Check(totalAfter == totalBefore, "every hatch still exists as a creature (" + totalAfter + " / " + totalBefore + ")");
            OvercrowdingMonitor.Instance crowd = ledger.HeldCritters.Count > 0
                ? ledger.HeldCritters[0].GetSMI<OvercrowdingMonitor.Instance>() : null;
            if (crowd != null && crowd.cavity != null)
            {
                Check(crowd.RegionSize == crowd.cavity.NumCells + KennelConfig.BackspaceCells,
                    "the room counts " + crowd.RegionSize + " cells for overcrowding (" + crowd.cavity.NumCells
                    + " + " + KennelConfig.BackspaceCells + ")");
            }
            else
            {
                Fail("a held hatch has no overcrowding monitor or cavity");
            }
            float frameAfter = 0f;
            yield return MeanFrame(5f, ms => frameAfter = ms);
            DemoNarrator.Stat("frame (after)", frameAfter.ToString("F2") + " ms");
            Log("KENNEL: frame mean before " + frameBefore.ToString("F3") + " ms, after " + frameAfter.ToString("F3") + " ms");

            // The library's DepthBoxLook (library 1.2, the mod's floor) draws the room when its shader
            // loaded. Without it, DepthBox's own counters.
            DepthBoxLook look = kennel.GetComponent<DepthBoxLook>();
            Check(DepthBoxLook.Loaded && look != null, "DepthBoxLook loaded its shader and sits on the Kennel (Loaded=" + DepthBoxLook.Loaded + ")");
            Func<int> drawnFrames = () => look != null ? look.DrawnFrames : box.DrawnFrames;
            Func<int> drawnOccupants = () => look != null ? look.DrawnOccupants : box.DrawnOccupants;
            string drawer = look != null ? "DepthBoxLook" : "DepthBox";
            int framesAt = drawnFrames(), placedAt = box.Placements;
            yield return WaitReal(2f * box.ReplaceSeconds + 1f);
            Check(drawnFrames() > framesAt, "the room behind the wall is drawn by " + drawer + " (" + (drawnFrames() - framesAt) + " frames)");
            Check(drawnOccupants() > 0, "hatches stand in it (" + drawnOccupants() + " drawn)");
            Check(box.Placements >= placedAt + 2, "they were re-placed every ~" + box.ReplaceSeconds + " s ("
                + (box.Placements - placedAt) + " times)");
            yield return LookBeats(kennel);

            DemoNarrator.Phase("Life behind the wall", "The ledger feeds them, takes their Coal, sends them out to be groomed, and lets them out on their own.");
            ledger.MaxOutForGrooming = groomMax;
            kennel.OutingsOn = true;
            float lifeCap = Time.realtimeSinceStartup + LifeSeconds;
            while (Time.realtimeSinceStartup < lifeCap && !(ledger.Meals > 0 && ledger.Poops > 0 && ledger.GroomingTrips > 0
                && GroomedAndBack(hatches) && kennel.OutingsTaken > 0))
            {
                DemoNarrator.Stat("outings", kennel.OutingsTaken.ToString());
                DemoNarrator.Stat("meals", ledger.Meals.ToString());
                DemoNarrator.Stat("Coal kg", ledger.Output.GetMassAvailable(SimHashes.Carbon.CreateTag()).ToString("F1"));
                DemoNarrator.Stat("grooming trips", ledger.GroomingTrips.ToString());
                DemoNarrator.Stat("held", ledger.Count + " / " + Hatches);
                yield return null;
            }
            float coal = ledger.Output.GetMassAvailable(SimHashes.Carbon.CreateTag());
            Check(ledger.Meals > 0, "held hatches ate " + ledger.Meals + " meals from the bin");
            Check(ledger.Poops > 0 && coal > 0f, "their Coal is in the Kennel (" + ledger.Poops + " poops, " + coal.ToString("F1") + " kg)");
            Check(ledger.GroomingTrips > 0, ledger.GroomingTrips + " grooming trip(s) out of the Kennel");
            Check(GroomedAndBack(hatches), "a groomed hatch walked back in");
            Check(kennel.OutingsTaken > 0, kennel.OutingsTaken + " outing(s) on their own (at most "
                + Kennel.OutingsPerCycle + " per hatch per cycle)");
            // A rancher cannot fetch a held hatch: vanilla cannot wrangle a stored critter
            // (Capturable.IsCapturable: Stored -> false), so it steps out of the door instead.
            DemoNarrator.Note("Grooming: a held hatch steps out of the door to the station (a dupe cannot wrangle a stored critter).");
            int starving = 0;
            foreach (GameObject h in hatches)
            {
                CreatureCalorieMonitor.Instance cal = h.GetSMI<CreatureCalorieMonitor.Instance>();
                starving += cal != null && cal.calories.value <= 0f ? 1 : 0;
            }
            Check(starving == 0, "no hatch is out of calories (" + starving + ")");
            Log("KENNEL: meals " + ledger.Meals + ", poops " + ledger.Poops + ", eggs " + ledger.Eggs
                + ", grooming trips " + ledger.GroomingTrips + ", outings " + kennel.OutingsTaken + ", sim " + ElapsedSimSeconds.ToString("F0") + " s");

            DemoNarrator.Phase("Release all", "The button lets every hatch out; their brains start again.");
            // Shut first (rig-side, as in Before): an open Kennel takes them straight back, which is
            // the design (they come and go), so the release is measured with the door shut.
            kennel.Open = false;
            kennel.OutingsOn = false;
            ledger.ReleaseAll("KennelRig");
            yield return WaitReal(1f);
            CritterLedger.CountCreatureBrains(out int totalOut, out int runningOut);
            DemoNarrator.Stat("brains running (released)", runningOut + " / " + totalOut);
            Check(ledger.Count == 0 && runningOut == totalOut, "after Release all: " + ledger.Count + " held, "
                + runningOut + " / " + totalOut + " brains running");
            DemoNarrator.Note("Save/load is not covered by this arm.");
            DemoNarrator.End("Behind the wall a hatch costs no brain, and the ranch still works.");
        }

        // FX's three beats for the look's verdict: a still at the rig's zoom centred on the Kennel,
        // a still at the default zoom (ortho 20), and a 4 s pan from 8 cells left to 8 right.
        private IEnumerator LookBeats(Kennel kennel)
        {
            CameraController cc = CameraController.Instance;
            Camera cam = Camera.main;
            if (cc == null || cam == null)
            {
                Fail("no camera for the look beats");
                yield break;
            }
            Extents e = kennel.GetComponent<Building>().GetExtents();
            Vector3 centre = new Vector3(e.x + e.width * 0.5f, e.y + e.height * 0.5f, cc.transform.GetPosition().z);
            float rigZoom = cam.orthographicSize;
            DemoNarrator.Note("The room behind the wall, at the rig's zoom, at the default zoom, and panned.");
            cc.SnapTo(centre, rigZoom);
            yield return WaitReal(5f); // a held look at the room with the hatches spread (record)
            yield return Still("kennel-look-rig.png");
            cc.SnapTo(centre, 20f);
            yield return WaitReal(1.5f);
            yield return Still("kennel-look-ortho20.png");
            cc.SnapTo(centre + new Vector3(-8f, 0f, 0f), rigZoom);
            yield return WaitReal(1f);
            Log("KENNEL: pan start");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < t0 + 4f)
            {
                float k = (Time.realtimeSinceStartup - t0) / 4f;
                cc.SnapTo(centre + new Vector3(Mathf.Lerp(-8f, 8f, k), 0f, 0f), rigZoom);
                yield return null;
            }
            cc.SnapTo(centre + new Vector3(8f, 0f, 0f), rigZoom);
            Log("KENNEL: pan end");
            yield return WaitReal(1f);
            // FX's frame-verified slide: the Kennel near the left, then the right screen edge, with the
            // camera, ortho size and the Kennel centre's screen u (-1 left .. 1 right) logged.
            foreach (float u in new[] { -0.8f, 0.8f })
            {
                float halfWidth = rigZoom * cam.aspect;
                Vector3 at = new Vector3(centre.x - u * halfWidth, centre.y, centre.z);
                cc.SnapTo(at, rigZoom);
                yield return WaitReal(1.5f);
                string side = u < 0f ? "L" : "R";
                Vector3 cp = cam.transform.position;
                Log("KENNEL: pan still " + side + " cam=(" + cp.x.ToString("F2") + "," + cp.y.ToString("F2") + ") ortho="
                    + cam.orthographicSize.ToString("F2") + " u=" + ((centre.x - cp.x) / (cam.orthographicSize * cam.aspect)).ToString("F2"));
                yield return Still("kennel-look-pan" + side + ".png");
            }
            DemoNarrator.FocusCameraToFit(RoomCells(), 2f);
        }

        private IEnumerator Still(string name)
        {
            string path = RigSaves.ScreenshotPathFor(name);
            ScreenCapture.CaptureScreenshot(path);
            yield return WaitReal(1f);
            Log("KENNEL: still " + path);
        }

        private static string Id(GameObject go)
        {
            return go == null ? "null" : go.name + "#" + go.GetInstanceID();
        }

        // One hatch's state for the log: held, admittable, groomed, its chore and its anim.
        private static string Describe(GameObject h, CritterLedger ledger)
        {
            Klei.AI.Effects fx = h.GetComponent<Klei.AI.Effects>();
            ChoreDriver driver = h.GetComponent<ChoreDriver>();
            Chore chore = driver != null ? driver.GetCurrentChore() : null;
            KBatchedAnimController a = h.GetComponent<KBatchedAnimController>();
            return Id(h) + " cell " + Grid.PosToCell(h) + " held=" + h.HasTag(CritterLedger.Held)
                + " admit=" + ledger.WouldAdmit(h) + " ranched=" + (fx != null && fx.HasEffect("Ranched"))
                + " wild=" + h.HasTag(GameTags.Creatures.Wild) + " bagged=" + h.HasTag(GameTags.Creatures.Bagged)
                + " count=" + ledger.Count + " chore=" + (chore != null ? chore.choreType.Id : "none")
                + " anim=" + (a != null ? a.currentAnim + "/" + a.enabled + "/" + a.sceneLayer : "none");
        }

        // A hatch with the grooming effect that is held again: out, groomed, and back in.
        private static bool GroomedAndBack(List<GameObject> hatches)
        {
            foreach (GameObject h in hatches)
            {
                Klei.AI.Effects fx = h.GetComponent<Klei.AI.Effects>();
                if (h.HasTag(CritterLedger.Held) && fx != null && fx.HasEffect("Ranched"))
                {
                    return true;
                }
            }
            return false;
        }

        private GameObject SpawnRancher()
        {
            try
            {
                GameObject dupe = new MinionStartingStats(is_starter_minion: false)
                    .Deliver(Grid.CellToPosCBC(Grid.XYToCell(RancherX, FloorY), Grid.SceneLayer.Move));
                MinionResume resume = dupe.GetComponent<MinionResume>();
                resume.ForceAddSkillPoint();
                resume.MasterSkill(Db.Get().Skills.Ranching1.Id);
                return dupe;
            }
            catch (Exception e)
            {
                Log("could not spawn the rancher: " + e.Message);
                return null;
            }
        }

        private static void SetSpeed(int speed)
        {
            if (SpeedControlScreen.Instance != null)
            {
                SpeedControlScreen.Instance.SetSpeed(speed);
            }
        }

        private static IEnumerator WaitReal(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        private static IEnumerator MeanFrame(float seconds, Action<float> result)
        {
            float until = Time.realtimeSinceStartup + seconds, sum = 0f;
            int n = 0;
            while (Time.realtimeSinceStartup < until)
            {
                sum += Time.unscaledDeltaTime;
                n++;
                yield return null;
            }
            result(n > 0 ? 1000f * sum / n : 0f);
        }

        private static IEnumerable<int> RoomCells()
        {
            List<int> cells = new List<int>();
            for (int y = FloorY - 1; y <= FloorY + 6; y++)
            {
                for (int x = RoomX0 - 1; x <= RoomX1 + 1; x++)
                {
                    cells.Add(Grid.XYToCell(x, y));
                }
            }
            return cells;
        }
    }
}
