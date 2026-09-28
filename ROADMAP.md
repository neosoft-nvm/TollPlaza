# Toll Plaza — Roadmap

## Done
- **Textures in game**: real CC0 textures, matte finishes, lettered signs, lane numbers, speed limit signs.
- **1:1:1 road design**: an N-booth plaza has N lanes in and N lanes out, dead straight, with the islands on the lane lines. Sizes 2–6.
- **No road names across the plaza**: the plaza's road pieces are nameless copies.
- **Toll collection**: every vehicle passing through is charged by class (car/truck/bus). Confirmed in game (0 unpaid).
- **Control panel**: toll rates, booth speeds (+/−), revenue, vehicle counts, and a plaza list with click-to-go.
- **Plaza info section**: click a plaza to see this month vs. last month for the plaza and each booth, and switch a booth between cash and E-Toll.

## Deployed, needs in-game testing (build of 2026-09-27 ~10 PM)
- 6-booth plaza no longer clones asset-pack bridges (no suspension towers).
- Approach 50% longer (1.95× the exit).
- 4 delineator posts per island end, with a solid white line underneath.
- Speed signs raised on posts above the crash cushions (the delineators were hiding them).
- Speeds: E-Toll 15 mph, cash 10 mph, approach/exit zone 25 mph.
- Roof sign on every plaza size.

## Deployed, needs in-game testing (build of 2026-09-28 morning)
- **Braking fix**: cause confirmed in game code: cars brake at ~1 g (10 m/s²) at the last possible moment for any lower limit ahead, and the approach was only 19.5 m. The approach is now 100 m in 8 pieces whose limits step down evenly (in speed squared) from the entry highway's limit to 25 mph, then the booths. Only newly placed plazas get it: bulldoze and re-place existing ones.
- **Braking probe**: the log reports each plaza's approach steps, and a "Braking in plazas" line (average/max peak deceleration, % over 6 m/s²) to compare before/after.
- **Booth stats**: a vehicle counts for the booth lane it drives through, not the lane it entered on.
- **Saved with the city, outside the save file**: on every game save a snapshot goes to `ModsData/TollPlaza/Cities/<city>/<frame>.json`; loading a save restores the snapshot made at that save's frame (older saves get their own numbers back). Saves with no snapshot keep the newest rates/speeds/lanes, stats from zero. Removing the mod leaves saves untouched.
- **City load reset**: stats no longer carry over from one city to another; vehicles already inside a plaza at load aren't counted as unpaid.
- **Movable control panel**: drag it by its title bar; it reopens where it was left (until the game restarts).

## Built, not deployed yet (game was running), 2026-09-28 late morning
- **Test of the 100 m approach**: no change. The probe showed every car hitting 12–16 m/s², i.e. a dead stop, and the approach log read "from 149 mph".
- **Speed scale**: lanes store twice the speed the game shows (base highways log 66.7 m/s = 149 mph). Booth/zone speeds were half what was intended (10 mph cash was really crawling). Now doubled via `TollCollectionSystem.LaneSpeed`.
- **Exit 10 m → 60 m**: the medians block lane changes between the booths, so cars that need another lane for the player's (usually narrower) road had to cross lanes in one car length after the booths. The game makes a car stop and reverse when its steering target is >45° off its heading (CarNavigationSystem), which matches the swerve-stop-reverse seen at the exits.
- **Stop probe**: logs where cars brake hard or reverse (approach step / booths / exit / after), what blocked them, and their lane and lane change.

## Deployed, needs in-game testing (build of 2026-09-28 afternoon)
- **Test of the late-morning build**: exit swerving settled (no hard stops logged at the exits). Save/load restore confirmed ("10 of 10 plazas matched"). Remaining hard stops were in the last 25 m of the approach: normal braking for the 25 mph zone, queueing, and cars re-picking the shortest queue (`UpdateOptimalLane`) at the last second.
- **Channelized approach, first try (deployed 10:14)**: the last 3 approach pieces were the raised-median road as 3 separate pieces. Hard stops left the last 25 m, but each joint between median-road pieces became a junction (turn arrows, X's on the inner lanes, dark junction paving) and cars avoided the inner lanes.
- **Channelized approach, fix**: the channel (37.5 m) and the islands are now one continuous road piece, with no joint anywhere in the median road. It runs at the booth speeds (15/10 mph), and the speed steps (5 pieces) end at 25 mph where it starts. Stop log place "channel" = the median road before the islands. Only newly placed plazas get it. Deployed 2026-09-28 ~10:45.

## Built, not deployed yet (game running), 2026-09-28 ~12:15–12:45
- **One-piece channel confirmed in game**: cars use every lane and slow down by lane; no hard stops at the channel, booths or exit.
- **Charged at the booth**: a vehicle used to be charged (and counted for its plaza) on entering the approach, but counted for a booth only on reaching it, so cars queuing on the approach made the plaza total run ahead of its booths (about 13 on a busy new plaza). Now it's charged, counted for the plaza and for its booth all at once, on the booth piece. Fallback: a vehicle first seen past the booths pays there (plaza total only).
- **Old plazas**: plazas placed before today's builds credit only ~5% of vehicles to booths (their roads don't match the current layout). Bulldoze and re-place them. With the fallback they still collect at the exit.
- **Plaza names (panels)**: named after the district the plaza stands in, else the road it's on, else the city ("Oak Hills Toll Plaza"); duplicates numbered ("... 2"). Rename in the plaza's info panel (empty name / "Auto" goes back to automatic). Names are kept with the toll data, not in the save. Roof sign unchanged.
- **Outdated plazas flagged**: "old layout, re-place" in the control panel list and a warning in the info panel (a plaza whose median road isn't the current one-piece channel + islands).
- **Layout log**: each plaza's road pieces are logged once ("Layout of …", plaza coordinates, roads meeting at each end), to diagnose the 6-booth plaza.

## Built, not deployed yet (game running), 2026-09-28 ~12:50
- **Test of the 12:30 build**: names work (district, road, city, numbered duplicates); outdated flags match the logged layouts. Roof sign doesn't follow renames (expected, to-do 5).
- **6-booth road wedges**: the new 6-booth plaza's pieces are placed straight (layout log), but its open road was 6 copies of the 1-lane highway's outer "Transport Option" lane, which drew as wedges. Now plain highway lanes with the outer lane last, like the game's 5-lane highway. Re-place 6-booth plazas.
- **Panel text**: the game's UI ignores number-format options ("$2587.5") and drops spaces between text pieces ("(5booths)", "145vehicles"); formatted by hand now. The "old layout" note moved under the plaza name so it doesn't overflow.

- **6-booth, second try (13:10)**: plain lanes didn't help (same wedges, plus a blob beside the booths, only on 6-booth plazas). The one difference left was the base road: 6 lanes was a copy of the 1-lane highway. Now the widest real one-way highway (5 lanes) is extended with extra lanes, which also covers 8/10/12 later. Two 3-lane roads side by side were ruled out: the player's highway joins the plaza at one node per end.

- **6-booth confirmed fixed (new city, 13:26)**: two 6-booth plazas on 6-lane highways, straight road through, no wedges, traffic flowing. Bigger plazas (8/10/12) can use the same extension of the 5-lane highway.


## Deployed 2026-09-28 ~14:15
- **Name order is now district, else city, else road**: road names didn't match the map labels (two plazas on Grove and Hamilton Highway both became "Emerson Peak"). Roads are only used if the city has no name; previews and removed pieces are skipped, and the log says where each name came from ("Plaza … named … from …").
- **Panel text**: booth tiles stack number/type/amount (the game's Button lays children in a row), "10 mph", "Switch to Cash".

## Built, not deployed yet, 2026-09-28 ~14:30
- **Names stick**: a plaza's automatic name is picked once (district, else city) and kept, so drawing a district later doesn't rename it. "Auto name" button next to Rename picks a fresh one on demand (an empty rename does the same). Kept names are saved with the toll data.
- **Vehicles/min**: measured over the last minute of simulation time (3,600 frames, a minute at normal speed; pause and fast-forward don't skew it), for the whole plaza, each booth tile and the booth detail. Not saved: the first minute after loading shows "N in the first X s".
- **Mods folder wiped by a deploy while the game ran** (14:20): the build deletes the folder first; only the locked Burst DLL survived. Redeploy before the next launch.

## Built, not deployed yet, 2026-09-28 ~15:10
- **Crash on clicking a booth tile (15:03)**: the game closed with an access violation inside its UI engine (cohtml.WindowsDesktop.dll, crash dump in `.cache/backtrace/crashpad/reports`), with no error in the mod's logs. The tiles were the game's Button with a nested full-width stack (new in the 14:15/14:31 builds). They're now plain clickable divs, with the selection border always present (transparent until selected) so the layout doesn't change on click.
- **Crashed again with plain tiles (15:22)**: same address in cohtml. The stack runs JS → game C# → JS → crash, so something the booth details create calls into the game; nothing in the log shows booth clicks ever working. The one element unique to the booth details was the game's Button ("Switch to …"), created by the click: now a plain div too. The section is also wrapped in an error boundary that shows any script error in the panel instead of passing it to the game.
- **Rolled back to ff7175b (16:15)**: still crashing, and by 16:12 on opening a plaza's panel at all, with a fresh basic playset (Mert's Toolbox not loaded, same cohtml address). All code changes since ff7175b are in `git stash` ("Since ff7175b: …"): sticky names, Auto name, vehicles/min, name order, panel text, the tile attempts. Bring them back one at a time once the rollback is confirmed stable.
- **Rollback confirmed (16:20)**: panel opens, booth clicks work.
- **Step 1 deployed (16:22)**: names (sticky, Auto name, district > city > road) and panel text (stacked tiles in the game's Button, "Switch to …", "10 mph"). No vehicles/min, no plain-div tiles, no error boundary. Step 2 = vehicles/min alone.
- **Step 1 confirmed (16:30)**: everything works. So the crash came with vehicles/min. Theory: its extra rows (plaza row, 4th tile line, booth row) made the info panel tall enough to crash the UI engine once a booth's details were opened.
- **Step 2 built (16:35)**: vehicles/min counting and data back, shown inside existing headings ("Whole plaza · 12 vehicles/min", "Booth 2 — Cash · 3/min"), no new rows or tile lines.
- **Real cause found (16:40)**: step 2 showed "undefined"/"NaN" everywhere, then crashed. The plaza panel's stats and the control panel's city totals were both written as type "TollPlaza.Stats", and only the plaza's had perMinute/measuredSeconds. The UI engine (cohtml) keeps one property layout per type name, so the plaza data was read with the wrong layout: garbage values, and a crash when it read past the end. The panel's height had nothing to do with it. Fix: the plaza's stats are "TollPlaza.PlazaStats". Rule: every distinct shape written to the UI needs its own type name.
- **Fix confirmed (16:40)**: real values back. Vehicles/min then moved back to its own "Measured, last minute" row (plaza and booth details) and a "/min" line on each booth tile, as first designed.

## Deployed 2026-09-28 ~16:56, confirmed in game
- **Budget line** (shows as the last Income row, under Service Trade, and grows as tolls come in): "Toll Plazas" under Income on the Budget screen, with the plaza icon, in the chart, the total income and the monthly balance. Amount = last month's tolls, or this month's so far until a month with tolls has passed. The game's income sources are a fixed list, so `TollBudgetSystem` adds a 15th item (source index 14) to the budget screen's item list in memory, and wraps two of `BudgetUISystem`'s private bindings by reflection (values: 14 + tolls; total income: + tolls). If the game's code changes, it logs a warning and leaves the Budget screen alone. Nothing saved; the tolls themselves were already paid into city money as vehicles pass.

## Built, not deployed yet, 2026-09-28 ~17:15
- **Plaza names on the roof sign** (to-do 5): `PlazaSignSystem` draws a second face just in front of each plaza's baked "TOLL PLAZA" billboard with the plaza's own name (uppercase, shrunk to fit, same green/border/lettering as the baked sign), redrawn on rename or Auto name. The artwork is drawn in game by `SignArt` (System.Drawing, which the game ships; same code as TextureBaker), shown with HDRP/Lit via Graphics.DrawMesh each frame, the way the game draws its road-name labels. Not an entity, never saved; if anything fails it logs a warning and the baked sign stays. Road plazas only (decorative ones have no name).
- **Roof name confirmed in game (17:14)**: sharp, lit like the rest of the plaza.

## Built, not deployed yet, 2026-09-28 ~17:40
- **Live plaza signs** (to-do 6 and 7), same drawn-face approach as the roof name (`PlazaSignSystem`, artwork by `SignArt`):
  - lane signs follow the booth's Cash/E-Toll switch (drawn as a shell slightly bigger than the baked panel, so the old colour can't show);
  - speed signs show the control panel's cash/E-Toll speeds;
  - a lane switched to cash gets an exit gate (a cash lane switched to E-Toll keeps its baked gate, raised);
  - the roof sign's second line says where the cash lanes are ("... CASH LANES RIGHT", "CASH LANES LEFT ...", "E-TOLL ONLY · NO CASH", "ALL LANES TAKE CASH", or "CASH AND E-TOLL LANES AS SIGNED");
  - the toll-rate sign shows the control panel's rates.
  Artwork is redrawn only when something changes and freed when no plaza shows it; signs refresh 4 times a second.
- **Live signs confirmed in game (17:28)**: lane signs, speed signs (20 mph), rate sign ($3.00/$6.50/$4.75) and the roof's cash-lane line all follow the panels. Fix: the added gate's arm was solid red (stripe texture clamped instead of repeating along the arm).
- **Gates follow the switch both ways**: the working plaza (with road) now has its own model without exit gates, and `PlazaSignSystem` draws a gate on every lane that currently takes cash, so a cash lane switched to E-Toll loses its gate. The decorative plaza keeps its built-in gates. Costs one extra copy of each plaza model at load.
- **Gate light red until a vehicle pays** (built, not deployed): a cash lane's signal shows red, and green for 4 s (simulation time) after its booth charges a vehicle (`PlazaStats.LastFrame`, not saved). The arm stays raised, since vehicles would drive through a lowered one. Every lane now has the signal (E-Toll lanes the signal alone, cash lanes the gate arm too).
- **Lane closure**: not working yet. The red X lane signal exists in the model code (`BuildLaneSign(closed: true)`) but nothing closes a lane. To-do 9.

## Next build

## To do
2. **Preview export broken**: tools/PreviewExport fails loading a texture source (`TextureBaker.LoadDetail`, System.Drawing "Parameter is not valid"), so preview/plaza.html still shows the old short approach.
3. ~~6-booth plaza broken~~: fixed 2026-09-28 (built from the 5-lane highway, not the 1-lane one).
4. **Last-minute lane changes**: check the channelized approach in game; lengthen it (`PlazaLayout.ChannelPieces`) if cars still cut across just before it.
5. **Plaza names on the roof sign**: "<District> Toll Plaza" (city name if no district), editable per plaza. Hard, because the sign mesh and texture are shared between plazas.
6. **Booth signs follow the cash/E-Toll toggle**: the lane sign, speed sign and exit gate should update when a booth is switched.
7. **Speed signs follow the panel speeds**: the signs are baked at 15/10 and don't change when speeds are edited.
8. **Plaza deleted silently**: the plaza disappears when a road is drawn through it. Warn or protect it instead.
9. **Closed lanes**: a red X lane signal, with traffic avoiding the lane.
10. **Bigger plazas**: 8, 10 and 12 booths.
11. **Commit and push** to GitHub once stable (everything since fe00117 is uncommitted).

## Future (community requests)
- **Open-road tolling gantries**: overhead readers, no booths, no slowdown.
- **Dynamic pricing**: rates that change with time of day or congestion.
- **Maintenance funding**: toll revenue that offsets highway upkeep, which the base game economy doesn't connect.
