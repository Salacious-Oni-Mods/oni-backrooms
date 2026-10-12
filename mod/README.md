# Backrooms (preview)

Rooms that go back into the screen, for Oxygen Not Included. A Backrooms building sits on a patch
of backwall and draws a room that seems to continue behind the 2D plane: one-point perspective that
shifts with the camera, the ceiling, side walls and floor drawn with the backwall building the room
is set in (drywall, tempshift plate). The room lights itself, darkening with distance from the
opening; light in the world at the opening makes it brighter and reach further in. The world's own
light does not travel into the room.

The first and only room in this preview is the **Kennel**, a critter kennel built into the back
wall. Adult critters that would visit a Critter Condo walk in through its door instead. Inside they
are no longer full critters: their brains and pathfinding stop, they are drawn as sprites standing
in the room, and a ledger keeps them alive at a lower rate (feeding from the Kennel's feed bin,
droppings into its output storage), so they never come out to eat. They come out only to be
groomed and on at most two outings a cycle. That makes Backrooms a visual mod and a performance mod.

Version 0.1.33. Status: proof of concept, preview for SDK release `0.1.0-alpha.2`.

## What the preview shows

Measured in the KENNEL rig (18 hatches, canvas test world):

- All 18 hatches walk in by themselves, and none is left with a brain running while inside.
- Frame time at 18 hatches is within run-to-run noise: 8.50 → 8.64 ms on the record run,
  7.11 → 7.04 and 8.03 → 6.88 ms on earlier runs. 18 hatches is too few to show a saving.
- **At 306 hatches it is.** In a test ranch of 17 Stables (84 cells each, a Grooming Station and a
  Critter Feeder in each), 18 hatches a pen and 30 ranchers, adding one Kennel to each pen cut the
  median frame time from 15.87 to 12.37 ms (-22%, about 63 → 81 fps) at speed 3. Three runs per
  arm, alternating, no recorder: vanilla 15.45 / 15.92 / 15.87 ms, Kennel 12.37 / 12.13 / 12.41 ms.
  Both held speed 3. The hatches still came and went (42-85 of 306 brains running). One PC, a test
  world with no other colony, and pens overcrowded in both arms; your colony will differ. Rigs:
  `--backrooms-bench-vanilla`, `--backrooms-bench-kennel`.
- Held hatches go out for grooming at the Grooming Station and walk back in.
- "Release all" puts every critter back in the world with its brain running.

## Known limits

- **Needs to be enclosed.** It stands on a floor and needs a tile or a backwall building (drywall,
  tempshift plate, ...) in every cell beside it and above it; the build tool says so when one is
  missing, and a built Kennel shows *Not Enclosed* and stops while one is missing.
- **Faces copy what borders the opening.** The floor shows the tile under the Kennel, the ceiling
  what is above it, each wall what is beside it: a solid tile's element texture, else the backwall
  building's own art. Built tiles show their element's texture, not the tile art. Mixed borders
  along one side are not handled: the middle cell decides the whole face.
- **Outings: at most 2 per critter per cycle.** Held critters leave on their own now and then, at
  most twice a cycle each.
- **No bagged carry.** A held critter that needs grooming is let out at the door and walks to the
  station. A rancher cannot carry a held critter there in a bag.
- **Save/load is not verified.** Do not rely on the Kennel in a save you care about.
- **Kennel only, one room type.** No other Backrooms buildings yet.
- **Feed.** The Kennel requests Sandstone for its feed bin. With none loose in the colony it shows
  the vanilla *Materials Unavailable* warning.
- **Cage front: two lights and three doors.** Each says one thing:
  - The top lights (header and corner plates) are yellow while the Kennel has room and red while it is full.
  - The light strips beside the door are green while hatches can walk in and red while they cannot
    (logic port red, not enclosed).
  - The door shows the paw. It shows a red paw while the feed bin is empty (held hatches cannot eat),
    and a padlock while the logic port reads red. The red paw wins if both apply. The door slides down
    for each visit.
- **Art.** The room's faces show Klei's own backwall art, read from the game at runtime. The mod
  ships none of it. The cage front is MitBoy42's art at three times the texture density, with its
  colours pre-corrected for the game's daytime screen grade and for the building shader's light
  tint as measured in a lit room by day, so it matches his sheet there. At night, in the dark or
  in another biome's tint, it shifts as every building does.

## Requirements

- The `OniFramework.Library` mod, 1.3 or later. Backrooms does not need the replacement `SimDLL.dll`
  or the `OniFramework` mod.

## License

MIT, see `LICENSE`.

## Credits

The Kennel art is by [MitBoy42](https://github.com/MitBoy42), made for this mod and used with his
permission (permission given in writing on 2026-10-11; the record is held privately by the project owner).

Development of this project uses AI coding assistants. All changes are reviewed, tested and released
by the maintainer.

Oxygen Not Included is developed and published by Klei Entertainment. This project is not
affiliated with or endorsed by Klei.
