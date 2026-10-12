using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Klei.AI;
using OniFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backrooms
{
    /// <summary>
    /// The pen layout of bench-vanilla.yaml and bench-kennel.yaml. MUST MATCH
    /// blueprints/make-bench.py, which writes both canvases from the same numbers.
    /// </summary>
    internal static class BenchLayout
    {
        public const int Pens = 17;
        public const int PenWidth = 14, PenHeight = 6, RowPitch = 7;
        public const int ShaftX = 17;
        public const int CanvasWidth = 36, CanvasHeight = 66;

        public static int PenX0(int pen) => pen % 2 == 0 ? 2 : 19;

        public static int FloorY(int pen) => 2 + RowPitch * (pen / 2);

        /// <summary>The Kennel's origin (its bottom-centre cell) from the pen's left edge.</summary>
        public const int KennelLx = 3;

        /// <summary>The pen whose interior holds <paramref name="cell"/>, or -1.</summary>
        public static int PenOf(int cell)
        {
            Grid.CellToXY(cell, out int x, out int y);
            for (int pen = 0; pen < Pens; pen++)
            {
                int x0 = PenX0(pen), y0 = FloorY(pen);
                if (x >= x0 && x < x0 + PenWidth && y >= y0 && y < y0 + PenHeight)
                {
                    return pen;
                }
            }
            return -1;
        }
    }

    /// <summary>
    /// KENNEL BENCH: the vanilla-vs-Kennel frame-time benchmark for the release notes.
    /// Two arms on two canvases that differ only in one Kennel per pen:
    ///
    ///   --backrooms-bench-vanilla  BENCHV, bench-vanilla.yaml: 17 Stables, no Kennel
    ///   --backrooms-bench-kennel   BENCHK, bench-kennel.yaml: the same 17 Stables, a Kennel in each
    ///
    /// Both arms run the same timeline: 306 tame hatches (18 per pen) and 17 ranchers (one a pen) are spawned,
    /// the game runs at speed 3 for a fixed warm-up (Kennel arm: the hatches walk in through
    /// vanilla's condo behaviour, and whoever has not by the end is stored by the rig, so both arms
    /// measure the same steady state), then frame times are collected for a fixed window and logged
    /// as one BENCH line. Outings and grooming trips stay on in the Kennel arm: a held critter costs
    /// what it costs in a player's ranch, not less.
    ///
    /// Measured runs carry no narration and no HUD (IsPresentation hides the HUD; the narrator is
    /// only started with --mod1-record), so the frame times are the game's alone. A recorded take
    /// narrates the same timeline; its frame times include the recorder and are not the bench's.
    ///
    /// A recorded take is also lockstepped at <see cref="TourFps"/> (Time.captureFramerate, with the
    /// real frame rate capped below it so the recorder takes every frame) and ends with a camera
    /// tour: zoom in on a Kennel, circle it, pan along the pens with the circle still going, zoom
    /// out. Every recorded frame is then 1/60 s of game time, so the video is smooth at 60 fps
    /// whatever the real frame rate was; encode it at a uniform rate, not from frames.txt.
    ///
    /// The ranchers do not breathe (an AirConsumptionRate x0 modifier): 30 dupes took 3 kg of
    /// oxygen a second, which would empty the pens within a run, and breathing is the same cost in
    /// both arms. Save/load is not covered.
    /// </summary>
    public abstract class KennelBenchRig : RigHarness
    {
        public const int HatchesPerPen = KennelConfig.Capacity;
        public const int Hatches = BenchLayout.Pens * HatchesPerPen;
        public const int Ranchers = BenchLayout.Pens;   // one rancher per pen

        /// <summary>Each rancher's Ranching level; Ranching1's perk adds its bonus on top.</summary>
        public const int RancherRanching = 15;
        private const float WarmSeconds = 60f;
        private const float SettleSeconds = 5f;
        private const float MeasureSeconds = 60f;
        private const float RoomWaitSeconds = 15f;
        private const float FeederKg = 200f;

        protected override string ModTag => "Backrooms";

        protected abstract bool WithKennels { get; }

        private static bool Narrated => RigRegistry.AnyFlag("--mod1-record");

        /// <summary>Frames per second of game time in a recorded take (the recorder's --mod1-record-fps).</summary>
        public const int TourFps = 60;

        protected override IEnumerator Run()
        {
            string arm = WithKennels ? "Kennel" : "vanilla";
            if (Narrated)
            {
                // Lockstep: every frame advances TourFps-th of a second, and the real rate is capped
                // under TourFps so the recorder's real-time schedule never skips one.
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = TourFps - 10;
                Time.captureFramerate = TourFps;
                Log("BENCH: lockstep at " + TourFps + " fps of game time, real rate capped at " + (TourFps - 10));
                DemoNarrator.Begin("Backrooms bench: " + arm + " arm",
                    "306 hatches in 17 Stables, 17 ranchers" + (WithKennels ? ", a Kennel in every pen" : ", no Kennel"));
                DemoNarrator.SetPhaseCount(5);
                DemoNarrator.Phase("Setup", "17 Stables of 84 cells, each with a Grooming Station and a Critter Feeder"
                    + (WithKennels ? " and a Kennel." : "."));
            }
            Log("BENCH: arm " + arm + ", narrated " + Narrated);

            // 1. The world: 17 Stables, the Kennels (Kennel arm), the feeders.
            RevealTestArea(0, 0, BenchLayout.CanvasWidth - 1, BenchLayout.CanvasHeight - 1);
            DemoNarrator.FocusCameraToFit(CanvasCells(), 1f);
            if (SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused)
            {
                SpeedControlScreen.Instance.Unpause(false);
            }
            int stables = 0;
            float roomCap = Time.realtimeSinceStartup + RoomWaitSeconds;
            while (Time.realtimeSinceStartup < roomCap && (stables = CountStables()) < BenchLayout.Pens)
            {
                yield return null;
            }
            if (!Require(stables == BenchLayout.Pens, stables + " / " + BenchLayout.Pens + " pens are Stables"))
            {
                yield break;
            }

            Kennel[] kennelByPen = new Kennel[BenchLayout.Pens];
            foreach (Kennel k in Object.FindObjectsOfType<Kennel>())
            {
                int pen = BenchLayout.PenOf(Grid.PosToCell(k));
                if (pen >= 0)
                {
                    kennelByPen[pen] = k;
                }
            }
            int kennels = Array.FindAll(kennelByPen, k => k != null).Length;
            if (!Require(kennels == (WithKennels ? BenchLayout.Pens : 0), kennels + " Kennel(s), one per pen in the Kennel arm, none in the vanilla arm"))
            {
                yield break;
            }
            if (WithKennels)
            {
                int operational = Array.FindAll(kennelByPen, k => k.GetComponent<Operational>().IsOperational).Length;
                Check(operational == BenchLayout.Pens, operational + " / " + BenchLayout.Pens + " Kennels are operational");
                foreach (Kennel k in kennelByPen)
                {
                    CritterLedger ledger = k.GetComponent<CritterLedger>();
                    ledger.Feed.Store(Sandstone(k.transform.GetPosition(), KennelConfig.FeedKg), hide_popups: true);
                }
            }
            int feeders = 0;
            foreach (CreatureFeeder f in Object.FindObjectsOfType<CreatureFeeder>())
            {
                f.GetComponent<Storage>().Store(Sandstone(f.transform.GetPosition(), FeederKg), hide_popups: true);
                feeders++;
            }
            Check(feeders == BenchLayout.Pens, feeders + " Critter Feeders, each given " + FeederKg + " kg Sandstone");

            // 2. 306 tame hatches, 18 a pen, and one rancher a pen.
            List<GameObject> hatches = new List<GameObject>();
            int[] penOfHatch = new int[Hatches];
            for (int pen = 0; pen < BenchLayout.Pens; pen++)
            {
                for (int i = 0; i < HatchesPerPen; i++)
                {
                    int cell = Grid.XYToCell(BenchLayout.PenX0(pen) + 1 + i % 12, BenchLayout.FloorY(pen));
                    GameObject h = GameUtil.KInstantiate(Assets.GetPrefab("Hatch"),
                        Grid.CellToPosCBC(cell, Grid.SceneLayer.Creatures), Grid.SceneLayer.Creatures);
                    h.SetActive(true);
                    h.GetComponent<Facing>()?.SetFacing(UnityEngine.Random.value < 0.5f);
                    penOfHatch[hatches.Count] = pen;
                    hatches.Add(h);
                }
            }
            yield return null;
            // Vanilla's own tame (see KennelRig: the amount alone never tames).
            foreach (GameObject h in hatches)
            {
                h.GetSMI<WildnessMonitor.Instance>().DebugTame();
            }
            int ranchers = 0;
            for (int i = 0; i < Ranchers; i++)
            {
                int pen = i % BenchLayout.Pens;
                int x = BenchLayout.PenX0(pen) + (i < BenchLayout.Pens ? 6 : 13);
                ranchers += SpawnRancher(Grid.XYToCell(x, BenchLayout.FloorY(pen))) ? 1 : 0;
            }
            Check(ranchers == Ranchers, ranchers + " / " + Ranchers + " ranchers with Ranching1 and Ranching " + RancherRanching + " spawned");
            yield return WaitReal(2f);
            int wild = hatches.FindAll(h => h.HasTag(GameTags.Creatures.Wild)).Count;
            Check(wild == 0, "all " + Hatches + " hatches are tame (" + wild + " still wild)");
            CritterLedger.CountCreatureBrains(out int brainsTotal0, out int brainsRunning0);
            Check(brainsTotal0 == Hatches && brainsRunning0 == Hatches, brainsRunning0 + " / " + brainsTotal0 + " hatch brains run at the start");

            // 3. Warm-up at speed 3, the same length in both arms.
            SetSpeed(2);
            if (Narrated)
            {
                DemoNarrator.Phase("Warm-up", WarmSeconds + " s at speed 3" + (WithKennels
                    ? ": hatches walk into the Kennels on their own; the rest are stored at the end." : "."));
            }
            int walkedIn = 0;
            Action<GameObject> counter = _ => walkedIn++;
            if (WithKennels)
            {
                foreach (Kennel k in kennelByPen)
                {
                    k.GetComponent<CritterLedger>().OnStored += counter;
                }
            }
            float warmCap = Time.realtimeSinceStartup + WarmSeconds;
            while (Time.realtimeSinceStartup < warmCap)
            {
                if (Narrated && WithKennels)
                {
                    DemoNarrator.Stat("walked in", walkedIn.ToString());
                }
                yield return null;
            }
            int byRig = 0, refused = 0;
            if (WithKennels)
            {
                foreach (Kennel k in kennelByPen)
                {
                    k.GetComponent<CritterLedger>().OnStored -= counter;
                }
                for (int i = 0; i < hatches.Count; i++)
                {
                    GameObject h = hatches[i];
                    if (h == null || h.HasTag(CritterLedger.Held))
                    {
                        continue;
                    }
                    if (kennelByPen[penOfHatch[i]].GetComponent<CritterLedger>().Store(h))
                    {
                        byRig++;
                    }
                    else
                    {
                        refused++;
                    }
                }
                Log("BENCH: walked in " + walkedIn + ", stored by the rig " + byRig + ", refused " + refused);
                Check(walkedIn >= 1, walkedIn + " hatch(es) walked in on their own in " + WarmSeconds + " s");
                // A Kennel keeps out a hatch it sent to be groomed until it is groomed (CritterLedger
                // .WouldAdmit), at most MaxOutForGrooming a Kennel: those are the only refusals.
                // A smoke run: 32 refused, 274 held. Outings take more out on their own,
                // so "all 306 held" is not the steady state; the BENCH line reports what is held.
                Check(refused <= BenchLayout.Pens * MaxOutForGrooming(kennelByPen), refused
                    + " refused by the rig's store, within the grooming allowance (" + BenchLayout.Pens * MaxOutForGrooming(kennelByPen) + ")");
            }
            yield return WaitReal(SettleSeconds);

            // 4. The measured window.
            if (Narrated)
            {
                DemoNarrator.Phase("Measure", MeasureSeconds + " s of frames at speed 3.");
            }
            int heldStart = WithKennels ? HeldTotal(kennelByPen) : 0;
            CritterLedger.CountCreatureBrains(out _, out int runningStart);
            List<float> ms = new List<float>(16384);
            float simStart = GameClock.Instance.GetTime();
            float measureCap = Time.realtimeSinceStartup + MeasureSeconds;
            long gcStart = GC.CollectionCount(0);
            float lastReal = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < measureCap)
            {
                yield return null;
                // Lockstepped, unscaledDeltaTime is the fixed 1/TourFps; the real frame is the clock's.
                float real = Time.realtimeSinceStartup;
                ms.Add(Narrated ? 1000f * (real - lastReal) : 1000f * Time.unscaledDeltaTime);
                lastReal = real;
            }
            float simSeconds = GameClock.Instance.GetTime() - simStart;
            long gcs = GC.CollectionCount(0) - gcStart;
            int heldEnd = WithKennels ? HeldTotal(kennelByPen) : 0;
            CritterLedger.CountCreatureBrains(out int brainsTotal, out int runningEnd);

            // 5. The result.
            float mean = 0f;
            int over33 = 0;
            foreach (float f in ms)
            {
                mean += f;
                over33 += f > 33.3f ? 1 : 0;
            }
            mean = ms.Count > 0 ? mean / ms.Count : 0f;
            ms.Sort();
            int groomed = 0, starving = 0;
            foreach (GameObject h in hatches)
            {
                if (h == null)
                {
                    continue;
                }
                Effects fx = h.GetComponent<Effects>();
                groomed += fx != null && fx.HasEffect("Ranched") ? 1 : 0;
                CreatureCalorieMonitor.Instance cal = h.GetSMI<CreatureCalorieMonitor.Instance>();
                starving += cal != null && cal.calories.value <= 0f ? 1 : 0;
            }
            string line = "BENCH: arm=" + (WithKennels ? "K" : "V")
                + " frames=" + ms.Count
                + " median=" + Pct(ms, 0.5f).ToString("F3")
                + " p10=" + Pct(ms, 0.1f).ToString("F3")
                + " p90=" + Pct(ms, 0.9f).ToString("F3")
                + " p99=" + Pct(ms, 0.99f).ToString("F3")
                + " mean=" + mean.ToString("F3")
                + " over33=" + over33
                + " sim_s=" + simSeconds.ToString("F1")
                + " sim_rate=" + (simSeconds / MeasureSeconds).ToString("F3")
                + " gen0_gcs=" + gcs
                + " brains_running=" + runningStart + "->" + runningEnd + "/" + brainsTotal
                + " held=" + heldStart + "->" + heldEnd
                + " walked_in=" + walkedIn + " stored_by_rig=" + byRig
                + " groomed=" + groomed + " ranchers=" + ranchers + " narrated=" + (Narrated ? 1 : 0);
            Log(line);
            if (Narrated)
            {
                DemoNarrator.Phase("Result", "Frame time over the measured window.");
                DemoNarrator.Stat("frame median", Pct(ms, 0.5f).ToString("F2") + " ms");
                DemoNarrator.Stat("frame p10..p90", Pct(ms, 0.1f).ToString("F2") + " .. " + Pct(ms, 0.9f).ToString("F2") + " ms");
                DemoNarrator.Stat("sim speed", (simSeconds / MeasureSeconds).ToString("F2") + " x");
                DemoNarrator.Stat("brains running", runningEnd + " / " + brainsTotal);
                if (WithKennels)
                {
                    DemoNarrator.Stat("held", heldEnd + " / " + Hatches);
                }
                DemoNarrator.Note("A recorded take: these frame times include the recorder and are not the bench's.");
            }

            Check(ms.Count >= 100, ms.Count + " frames measured in " + MeasureSeconds + " s");
            Check(simSeconds > 0f, "the game clock advanced " + simSeconds.ToString("F1") + " s in the window");
            Check(brainsTotal == Hatches, "all " + Hatches + " hatches still exist as creatures (" + brainsTotal + ")");
            Check(starving == 0, "no hatch is out of calories (" + starving + ")");
            // Every hatch is either held (brain stopped) or out with its brain running: none lost
            // between the two, at the start and the end of the window.
            Check(heldStart + runningStart == Hatches, "held + running = " + heldStart + " + " + runningStart
                + " = " + Hatches + " when the window opened");
            Check(heldEnd + runningEnd == Hatches, "held + running = " + heldEnd + " + " + runningEnd
                + " = " + Hatches + " when it closed");
            if (WithKennels)
            {
                Check(heldStart > 0, heldStart + " / " + Hatches + " hatches held when the window opened");
            }
            if (Narrated)
            {
                yield return Tour();
                DemoNarrator.End(WithKennels ? "306 hatches behind the wall: the frame above is the Kennel arm's."
                    : "306 hatches in their pens: the frame above is vanilla's.");
            }
        }

        // The camera tour of a recorded take, one step per lockstepped frame (1/TourFps s), at speed 1.
        // Pen 8 (left column, row 4) and pen 9 (right column, same row) are the middle of the canvas.
        private IEnumerator Tour()
        {
            CameraController cc = CameraController.Instance;
            Camera cam = Camera.main;
            if (cc == null || cam == null)
            {
                Log("BENCH: no camera, no tour");
                yield break;
            }
            SetSpeed(0);
            DemoNarrator.Phase("Tour", "Close on a Kennel, around it, along the pens and back out, at "
                + TourFps + " fps.");
            Vector3 whole = cam.transform.position;
            float wide = cam.orthographicSize;
            Vector3 k8 = KennelCentre(8), k9 = KennelCentre(9), k15 = KennelCentre(15);
            // Close is far enough out, and the circle wide enough, that the room's depth shows as the
            // camera swings round (closer in, it barely shows).
            const float Close = 2.6f, Mid = 4f;
            Log("BENCH: tour start, wide ortho " + wide.ToString("F2"));
            ShowOverlays(false);
            int frames = 0, leg = 0;
            string[] legNames = { "zoom-in", "circle", "pan-across", "pan-up", "zoom-out", "hold" };
            // Each leg: seconds, then position and zoom as a function of t in 0..1 and seconds in.
            foreach ((float secs, Func<float, float, Vector3> at, Func<float, float> zoom) in new (float, Func<float, float, Vector3>, Func<float, float>)[]
            {
                (5f, (t, s) => Vector3.Lerp(whole, k8, Ease(t)), t => LogLerp(wide, Close, Ease(t))),
                (CircleSeconds, (t, s) => k8 + Circle(s, CircleSeconds, 1.4f, 1f, 0.7f), t => Close),
                (12f, (t, s) => Vector3.Lerp(k8, k9, Ease(t)) + Circle(s, 12f, 1.2f, 2f), t => LogLerp(Close, Mid, Ease(Mathf.Clamp01(t * 3f)))),
                (10f, (t, s) => Vector3.Lerp(k9, k15, Ease(t)) + Circle(s, 10f, 1.2f, 2f), t => Mid),
                (6f, (t, s) => Vector3.Lerp(k15, whole, Ease(t)), t => LogLerp(Mid, wide * 1.15f, Ease(t))),
                (3f, (t, s) => whole, t => wide * 1.15f),
            })
            {
                bool circle = legNames[leg] == "circle";
                if (circle)
                {
                    yield return HoldForVisit(cc, new Vector3(k8.x, k8.y, whole.z), Close);
                }
                int n = Mathf.RoundToInt(secs * TourFps);
                // realtime matches frames.jsonl's, so a cut (make-tour-gif.sh) can find the leg's frames.
                Log("BENCH: tour leg " + legNames[leg++] + " realtime=" + Time.realtimeSinceStartup.ToString("F4") + " frames=" + (n + 1));
                for (int f = 0; f <= n; f++)
                {
                    float t = (float)f / n, s = (float)f / TourFps;
                    Vector3 p = at(t, s);
                    cc.SnapTo(new Vector3(p.x, p.y, whole.z), zoom(t));
                    frames++;
                    if (circle && doorFrame < 0 && DoorMoving())
                    {
                        doorFrame = f;
                        Log("BENCH: tour door opens at circle frame " + f + " of " + n);
                    }
                    if (circle)
                    {
                        WatchTourClimb("circle frame " + f);
                        ShowRedFronts(f, s);
                        if (fullFrame < 0 && TopLight() == "red")
                        {
                            fullFrame = f;
                            Log("BENCH: tour top lights turn red at circle frame " + f + ", front " + FrontAnim());
                        }
                    }
                    yield return null;
                }
                if (circle && doorFrame < 0)
                {
                    Log("BENCH: tour door did not open during the circle");
                }
                if (circle && tourKennel != null)
                {
                    // The tour's own checks: the circle shows a hatch at the door and the top lights
                    // going from yellow (room left) to red (full), as drawn by the front-layer controller.
                    Check(doorFrame >= 0, "the tour Kennel's door opened during the circle (frame " + doorFrame + ")");
                    Check(tourTopAtStart == "yellow", "the tour Kennel's top lights were yellow when the circle began (" + tourTopAtStart + ")");
                    Check(fullFrame >= 0, "the tour Kennel filled and its top lights turned red during the circle (frame " + fullFrame + ")");
                    Check(tourClimbFrames == 0, "no hatch was seen climbing the tour Kennel's face (" + tourClimbFrames + " frame(s), "
                        + tourClimbersLogged.Count + " hatch(es))");
                    CritterLedger tl = tourKennel.GetComponent<CritterLedger>();
                    tl.OnStored -= TourStored;
                    tl.OnReleased -= TourReleased;
                    tourKennel.OutingsOn = true;
                }
                if (circle && tourKennel != null && !tourKennel.Open)
                {
                    tourKennel.Open = true;
                    Log("BENCH: tour Kennel reopened at the end of the circle");
                }
                if (circle && alertFrame >= 0 && unalertFrame < 0)
                {
                    RefeedTourKennel();
                    Log("BENCH: tour Kennel feed back at the end of the circle");
                }
            }
            ShowOverlays(true);
            Log("BENCH: tour end, " + frames + " frames, realtime=" + Time.realtimeSinceStartup.ToString("F4"));
            SetSpeed(2);
        }

        private readonly List<KBatchedAnimController> hiddenDupes = new List<KBatchedAnimController>();

        // The tour is shot clean: the narrator's panels, the dupes themselves, their name labels and
        // the status icons would cover the Kennel the shot is about. A dupe is hidden by disabling its
        // anim controllers, which deregisters them from the draw batch and the culling listener (a
        // SetVisiblity(false) is undone by culling as soon as the moving camera re-shows its cell);
        // the bench's checks have all been made before the tour, so a dupe paused mid-chore is fine. Status icons and the world-space canvas go through
        // Klei's own screenshot-mode switch (DebugHandler.UpdateUI, without the free camera and input
        // lock its toggle adds); the name canvas re-enables itself every LateUpdate, so its GameObject
        // is switched off instead; the narrator is an OnGUI behaviour, so disabling it stops its draw.
        private void ShowOverlays(bool show)
        {
            DebugHandler.ScreenshotMode = !show;
            Traverse.Create(typeof(DebugHandler)).Method("UpdateUI").GetValue();
            if (NameDisplayScreen.Instance != null)
            {
                Canvas names = Traverse.Create(NameDisplayScreen.Instance).Field("nameDisplayCanvas").GetValue<Canvas>();
                names?.gameObject.SetActive(show);
            }
            if (!show)
            {
                foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
                {
                    foreach (KBatchedAnimController kbac in dupe.GetComponentsInChildren<KBatchedAnimController>())
                    {
                        if (kbac.enabled)
                        {
                            kbac.enabled = false;
                            hiddenDupes.Add(kbac);
                        }
                    }
                }
            }
            else
            {
                foreach (KBatchedAnimController kbac in hiddenDupes)
                {
                    if (kbac != null)
                    {
                        kbac.enabled = true;
                    }
                }
                hiddenDupes.Clear();
            }
            GameObject narrator = GameObject.Find("Mod1ThermoFluidNarrator");
            if (narrator != null)
            {
                foreach (Behaviour b in narrator.GetComponents<Behaviour>())
                {
                    b.enabled = show;
                }
            }
            Log("BENCH: tour overlays " + (show ? "shown" : "hidden") + " (narrator " + (narrator != null ? "found" : "MISSING")
                + ", " + hiddenDupes.Count + " dupe anim controllers hidden)");
        }

        // The circle should show a hatch walking in and the door sliding. Hatches
        // visit on their own schedule, so before the circle the camera holds close on pen 8's Kennel
        // until a hatch reserves it (the condo visit has started: it is walking to the door), at
        // most HoldForVisitSeconds of game time. The door then opens inside the circle (CircleSeconds).
        private const int HoldForVisitSeconds = 60;
        private Kennel tourKennel;
        private int doorFrame = -1, fullFrame = -1;
        private string tourTopAtStart = "";

        // Diagnostics for the 0.1.29 take 2 hatch seen on the tour Kennel's door: a condo visitor
        // the ledger refuses at the door (CritterLedger.AtCondoDoor) plays vanilla's climb on
        // SceneLayer.BuildingUse. Each one is logged once with every reason WouldAdmit could have.
        private CreatureBrain[] tourBrains = new CreatureBrain[0];
        private readonly HashSet<int> tourClimbersLogged = new HashSet<int>();
        private int tourClimbFrames;

        private void TourStored(GameObject go)
        {
            CritterLedger l = tourKennel.GetComponent<CritterLedger>();
            Log("BENCH: tour Kennel stored " + go.name + "#" + go.GetInstanceID() + ", " + l.Count + "/" + l.Capacity);
        }

        private void TourReleased(GameObject go, string why)
        {
            CritterLedger l = tourKennel.GetComponent<CritterLedger>();
            Log("BENCH: tour Kennel released " + go.name + "#" + go.GetInstanceID() + " (" + why + "), " + l.Count + "/" + l.Capacity);
        }

        // Counts this frame's climbers on the tour Kennel's face and logs each new one's refusal reasons.
        private void WatchTourClimb(string when)
        {
            if (tourKennel == null)
            {
                return;
            }
            CritterLedger l = tourKennel.GetComponent<CritterLedger>();
            bool any = false;
            foreach (CreatureBrain b in tourBrains)
            {
                if (b == null)
                {
                    continue;
                }
                KBatchedAnimController a = b.GetComponent<KBatchedAnimController>();
                CritterCondoStates.Instance smi = b.GetSMI<CritterCondoStates.Instance>();
                if (a == null || !a.enabled || a.sceneLayer != Grid.SceneLayer.BuildingUse || smi == null
                    || smi.sm.targetCondo.Get(smi) != tourKennel.gameObject)
                {
                    continue;
                }
                any = true;
                if (tourClimbersLogged.Add(b.GetInstanceID()))
                {
                    GameObject go = b.gameObject;
                    List<GameObject> outForGrooming = Traverse.Create(l).Field("outForGrooming").GetValue<List<GameObject>>();
                    List<GameObject> pending = Traverse.Create(l).Field("pending").GetValue<List<GameObject>>();
                    Effects fx = go.GetComponent<Effects>();
                    Log("BENCH: tour Kennel CLIMB " + when + ": " + go.name + "#" + go.GetInstanceID()
                        + " WouldAdmit=" + l.WouldAdmit(go) + " count=" + l.Count + "/" + l.Capacity
                        + " Open=" + tourKennel.Open + " AcceptsVisits=" + tourKennel.AcceptsVisits
                        + " held=" + go.HasTag(CritterLedger.Held) + " pending=" + (pending != null && pending.Contains(go))
                        + " outForGrooming=" + (outForGrooming != null && outForGrooming.Contains(go))
                        + " groomed=" + (fx != null && fx.HasEffect(l.GroomedEffect))
                        + " species=" + b.species + " wild=" + go.HasTag(GameTags.Creatures.Wild)
                        + " baby=" + (go.GetSMI<BabyMonitor.Instance>() != null)
                        + " bagged=" + go.HasTag(GameTags.Creatures.Bagged) + " dead=" + go.HasTag(GameTags.Dead));
                }
            }
            if (any)
            {
                tourClimbFrames++;
            }
        }

        private IEnumerator HoldForVisit(CameraController cc, Vector3 at, float zoom)
        {
            tourKennel = null;
            float best = float.MaxValue;
            foreach (Kennel k in Object.FindObjectsOfType<Kennel>())
            {
                float d = (k.transform.position - new Vector3(at.x, at.y, k.transform.position.z)).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    tourKennel = k;
                }
            }
            if (tourKennel != null)
            {
                // Outings would let held hatches out during the circle (0.1.29 take 2: 17/18 went to
                // 16/18 and never filled), so the tour Kennel takes none until the circle ends.
                tourKennel.OutingsOn = false;
                CritterLedger tl = tourKennel.GetComponent<CritterLedger>();
                tl.OnStored += TourStored;
                tl.OnReleased += TourReleased;
                tourBrains = Object.FindObjectsOfType<CreatureBrain>();
                Log("BENCH: tour Kennel outings off, its stores and releases logged");
            }
            // Its hatches have eaten the bin empty by now (0.1.24 take: "feed out (0.0 kg)", the front
            // on "alert" the whole circle), so top it up: the circle shows the lit front first and
            // empties the bin itself for the alert.
            if (tourKennel != null)
            {
                Log("BENCH: tour Kennel feed topped up by " + TopUpTourFeed().ToString("F1") + " kg, front " + FrontAnim());
                // The circle shows the top lights go from yellow to red: the Kennel starts one short
                // of full, and the hatch that walks in through the door fills it. A full Kennel lets
                // one out; a Kennel further down (0.1.27 take: 12/18) takes the nearest free hatches
                // straight in, leaving the last place for a walk-in.
                CritterLedger ledger = tourKennel.GetComponent<CritterLedger>();
                if (ledger.Count >= ledger.Capacity)
                {
                    foreach (GameObject go in ledger.HeldCritters)
                    {
                        ledger.Release(go, "the tour, so the top lights show not full");
                        break;
                    }
                    Log("BENCH: tour Kennel let one out, front " + FrontAnim());
                }
                else if (ledger.Count < ledger.Capacity - 1)
                {
                    Vector3 kp = tourKennel.transform.GetPosition();
                    List<GameObject> free = new List<GameObject>();
                    foreach (CreatureBrain b in Object.FindObjectsOfType<CreatureBrain>())
                    {
                        if (b.species == GameTags.Creatures.Species.HatchSpecies && !b.HasTag(CritterLedger.Held))
                        {
                            free.Add(b.gameObject);
                        }
                    }
                    free.Sort((a, b) => (a.transform.GetPosition() - kp).sqrMagnitude.CompareTo((b.transform.GetPosition() - kp).sqrMagnitude));
                    // The nearest hatch the Kennel would admit stays out to walk in (0.1.28 take 1:
                    // storing the 17th left only a hatch it would not admit, so none came).
                    GameObject walker = free.Find(go => ledger.WouldAdmit(go));
                    int stored = 0;
                    foreach (GameObject go in free)
                    {
                        if (ledger.Count >= ledger.Capacity - 1)
                        {
                            break;
                        }
                        if (go != walker && ledger.Store(go))
                        {
                            stored++;
                        }
                    }
                    Log("BENCH: tour Kennel took " + stored + " in to be one short of full, "
                        + (walker != null ? "one admitted hatch left out to walk in" : "NO admitted hatch left out") + ", front " + FrontAnim());
                }
                tourTopAtStart = TopLight();
            }
            KPrefabID id = tourKennel != null ? tourKennel.GetComponent<KPrefabID>() : null;
            // Each store plays the door (Kennel.OpenDoor on OnStored); let that finish so the hold
            // below waits for a real visit (0.1.28 take 1 read the stores' door as one).
            while (tourKennel != null && DoorMoving())
            {
                cc.SnapTo(at, zoom);
                yield return null;
            }
            int held = 0;
            while (id != null && !id.HasTag(GameTags.Creatures.ReservedByCreature) && !DoorMoving()
                && held < HoldForVisitSeconds * TourFps)
            {
                cc.SnapTo(at, zoom);
                WatchTourClimb("hold frame " + held);
                held++;
                yield return null;
            }
            bool visit = id != null && (id.HasTag(GameTags.Creatures.ReservedByCreature) || DoorMoving());
            Log("BENCH: tour held " + held + " frames for a visit: " + (visit ? "a hatch is on its way in" : "none came, circling anyway"));
        }

        // The circle also shows the other two doors. Once the door has moved and closed again, the
        // Kennel is shut the way the logic port shuts it, without the release, for RedSeconds: the
        // padlock door ("locked") and red jamb strips. Then its feed bin is emptied into its output
        // bin for RedSeconds: the red paw door ("alert"). Then the feed goes back. Eighteen hungry
        // hatches empty a full bin within seconds (0.1.26 take: 200 kg topped up, "front alert" 7 s
        // later), so outside that window the bin is kept topped up.
        private const float CircleSeconds = 16f, LockAfterSeconds = 5f, AlertAfterSeconds = 9.5f, RedSeconds = 3.5f;
        private int shutFrame = -1, reopenFrame = -1, alertFrame = -1, unalertFrame = -1;

        private void ShowRedFronts(int f, float s)
        {
            if (tourKennel == null)
            {
                return;
            }
            int red = Mathf.RoundToInt(RedSeconds * TourFps);
            if (alertFrame < 0 || unalertFrame >= 0)
            {
                TopUpTourFeed();
            }
            if (shutFrame < 0 && s >= LockAfterSeconds && !DoorMoving())
            {
                shutFrame = f;
                tourKennel.Open = false;
                Log("BENCH: tour Kennel shut at circle frame " + f + ", front " + FrontAnim());
            }
            else if (shutFrame >= 0 && reopenFrame < 0 && f - shutFrame >= red)
            {
                reopenFrame = f;
                tourKennel.Open = true;
                Log("BENCH: tour Kennel reopened at circle frame " + f + ", front " + FrontAnim());
            }
            else if (reopenFrame >= 0 && alertFrame < 0 && s >= AlertAfterSeconds && !DoorMoving())
            {
                alertFrame = f;
                CritterLedger ledger = tourKennel.GetComponent<CritterLedger>();
                float kg = ledger.Feed.Transfer(ledger.Output, KennelConfig.FeedTag, ledger.Feed.MassStored(), hide_popups: true);
                Log("BENCH: tour Kennel feed out (" + kg.ToString("F1") + " kg) at circle frame " + f + ", front " + FrontAnim());
            }
            else if (alertFrame >= 0 && unalertFrame < 0 && f - alertFrame >= red)
            {
                unalertFrame = f;
                RefeedTourKennel();
                Log("BENCH: tour Kennel feed back at circle frame " + f + ", front " + FrontAnim());
            }
        }

        private void RefeedTourKennel()
        {
            CritterLedger ledger = tourKennel.GetComponent<CritterLedger>();
            ledger.Output.Transfer(ledger.Feed, KennelConfig.FeedTag, ledger.Output.GetMassAvailable(KennelConfig.FeedTag), hide_popups: true);
            TopUpTourFeed();
        }

        private float TopUpTourFeed()
        {
            CritterLedger ledger = tourKennel.GetComponent<CritterLedger>();
            float topUp = KennelConfig.FeedKg - ledger.Feed.MassStored();
            if (topUp < KennelConfig.FeedKg / 2f)
            {
                return 0f;
            }
            ledger.Feed.Store(Sandstone(tourKennel.transform.GetPosition(), topUp), hide_popups: true);
            return topUp;
        }

        // The door anim playing and which lights show, e.g. "locked, top yellow, strips red, 18/18 held".
        private string FrontAnim()
        {
            KBatchedAnimController anim = tourKennel != null ? tourKennel.GetComponent<KBatchedAnimController>() : null;
            if (anim == null || anim.CurrentAnim == null)
            {
                return "none";
            }
            CritterLedger ledger = tourKennel.GetComponent<CritterLedger>();
            // The lights are read on the controller that draws them (Kennel.FrontController), not
            // the building's own, which never draws FG symbols.
            KAnimControllerBase front = Kennel.FrontController(anim);
            return anim.CurrentAnim.name
                + ", top " + Lit(front, "kennel_cage_face_lamp_top", "kennel_cage_face_lamp_top_red")
                + ", strips " + Lit(front, "kennel_cage_face_indicator", "kennel_cage_face_indicator_red")
                + ", " + ledger.Count + "/" + ledger.Capacity + " held"
                + (front == anim ? ", NO front-layer controller" : "");
        }

        // The top light as drawn: "yellow", "red", "both" or "none" ("" with no tour Kennel).
        private string TopLight()
        {
            KBatchedAnimController anim = tourKennel != null ? tourKennel.GetComponent<KBatchedAnimController>() : null;
            return anim == null ? "" : Lit(Kennel.FrontController(anim), "kennel_cage_face_lamp_top", "kennel_cage_face_lamp_top_red");
        }

        private static string Lit(KAnimControllerBase anim, string normal, string red)
        {
            bool n = anim.GetSymbolVisiblity(normal), r = anim.GetSymbolVisiblity(red);
            return n && r ? "both" : n ? (normal.EndsWith("indicator") ? "green" : "yellow") : r ? "red" : "none";
        }

        private bool DoorMoving()
        {
            KBatchedAnimController anim = tourKennel != null ? tourKennel.GetComponent<KBatchedAnimController>() : null;
            string current = anim != null && anim.CurrentAnim != null ? anim.CurrentAnim.name : "";
            return current.StartsWith("access");
        }

        private static Vector3 KennelCentre(int pen)
        {
            return new Vector3(BenchLayout.PenX0(pen) + BenchLayout.KennelLx + 0.5f, BenchLayout.FloorY(pen) + 1.5f, 0f);
        }

        // An offset going round a circle `loops` times in `secs`, its radius easing in over the first
        // second and out over the last, so a leg spirals out from its path's centre line and back.
        // `squash` < 1 makes it an ellipse, flatter vertically, so a wide swing keeps the Kennel in frame.
        private static Vector3 Circle(float s, float secs, float radius, float loops, float squash = 1f)
        {
            float r = radius * Ease(Mathf.Clamp01(s)) * Ease(Mathf.Clamp01(secs - s));
            float a = 2f * Mathf.PI * loops * s / secs;
            return new Vector3(r * Mathf.Sin(a), r * squash * Mathf.Cos(a), 0f);
        }

        private static float Ease(float t)
        {
            return t * t * (3f - 2f * t);
        }

        // Zoom in log space, so it reads as a steady zoom rather than slowing as it closes.
        private static float LogLerp(float a, float b, float t)
        {
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(a), Mathf.Log(b), t));
        }

        private static GameObject Sandstone(Vector3 at, float kg)
        {
            return ElementLoader.FindElementByHash(SimHashes.SandStone).substance.SpawnResource(at, kg, 300f, byte.MaxValue, 0);
        }

        private static int MaxOutForGrooming(Kennel[] kennels)
        {
            return kennels[0].GetComponent<CritterLedger>().MaxOutForGrooming;
        }

        private static int HeldTotal(Kennel[] kennels)
        {
            int n = 0;
            foreach (Kennel k in kennels)
            {
                n += k.GetComponent<CritterLedger>().Count;
            }
            return n;
        }

        private static int CountStables()
        {
            int n = 0;
            for (int pen = 0; pen < BenchLayout.Pens; pen++)
            {
                CavityInfo cavity = Game.Instance.roomProber.GetCavityForCell(
                    Grid.XYToCell(BenchLayout.PenX0(pen) + 1, BenchLayout.FloorY(pen) + 4));
                n += cavity != null && cavity.room != null && cavity.room.roomType == Db.Get().RoomTypes.CreaturePen ? 1 : 0;
            }
            return n;
        }

        private static float Pct(List<float> sorted, float q)
        {
            return sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
        }

        private bool SpawnRancher(int cell)
        {
            try
            {
                GameObject dupe = new MinionStartingStats(is_starter_minion: false)
                    .Deliver(Grid.CellToPosCBC(cell, Grid.SceneLayer.Move));
                MinionResume resume = dupe.GetComponent<MinionResume>();
                resume.ForceAddSkillPoint();
                resume.MasterSkill(Db.Get().Skills.Ranching1.Id);
                // The same Ranching level for every rancher, not the random starting level.
                dupe.GetComponent<AttributeLevels>().SetLevel(Db.Get().Attributes.Ranching.Id, RancherRanching);
                dupe.GetAttributes().Add(new AttributeModifier(Db.Get().Attributes.AirConsumptionRate.Id, -1f,
                    "Kennel bench: ranchers do not breathe", is_multiplier: true));
                return true;
            }
            catch (Exception e)
            {
                Log("could not spawn a rancher: " + e.Message);
                return false;
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

        private static IEnumerable<int> CanvasCells()
        {
            List<int> cells = new List<int>();
            for (int y = 1; y < BenchLayout.CanvasHeight - 1; y++)
            {
                for (int x = 1; x < BenchLayout.CanvasWidth - 1; x++)
                {
                    cells.Add(Grid.XYToCell(x, y));
                }
            }
            return cells;
        }
    }

    /// <summary>BENCHV: 17 Stables, no Kennel.</summary>
    public sealed class KennelBenchVanillaRig : KennelBenchRig
    {
        public const string Flag = "--backrooms-bench-vanilla";

        protected override string RigTag => "BENCHV";

        protected override string BlueprintFile => "blueprints/bench-vanilla.yaml";

        protected override bool WithKennels => false;
    }

    /// <summary>BENCHK: the same 17 Stables with one Kennel in each.</summary>
    public sealed class KennelBenchKennelRig : KennelBenchRig
    {
        public const string Flag = "--backrooms-bench-kennel";

        protected override string RigTag => "BENCHK";

        protected override string BlueprintFile => "blueprints/bench-kennel.yaml";

        protected override bool WithKennels => true;
    }
}
