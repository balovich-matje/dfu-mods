# Idle Adventurer: implementation plan

Working title. A side game mode for Daggerfall Unity 1.1.1: one hero goes on expeditions by themselves; the player
manages gear, spells, supplies and orders between (and during) runs. Separate from normal saves. Progress continues
while the game is closed, from the system clock.

Status: M0 (spike) and M1 (simulation core, balance pass) are done. Decisions already made with the user are marked
**[decided]**.

## 0. Changes since the first version of this plan

- **Time scale:** one simulation second is one real second at 1x, in fights and out. Travel and exploring are
  compressed stretches of that same clock, and time away from the game is replayed 1:1 (capped at 12 hours).
- **Expedition view [decided]:** first person, on rails. We assemble our own dungeons from the game's dungeon blocks
  and store a route through each block, so the camera never needs to find its way. The simulation stays the single
  source of truth; the 3D view replays its events. The over-the-shoulder sprite view is kept as a possible later
  option. A spike before M2 has to prove two things: rendering a block from the main menu with no game loaded, and
  getting route data per block (a walk-and-record tool is the sure way; DFU has no navigation mesh of its own).
- **Rails spike (done, `src/IdleRails.cs`, key V in the hub):** both unknowns passed. A real block (Privateer's
  Hold, S0000999.RDB) is built from the main menu with no game loaded in about 0.45 s, drawn by our own camera into
  a texture. Unity's navigation module is in DFU's build: a navmesh is baked at run time from the block's colliders
  and gives a 181 m route in about 0.13 s, so no hand-recorded routes are needed. The route is re-centred away from
  walls. An enemy is a flat sprite standing in the world; the weapon is the game's own flat image over the view.
  Doors are simply not created. Not done yet: joining several blocks, the game's dungeon texture sets, enemy
  sprites at the game's own scale, weapon image offsets, lifts and water (blocks that need them are to be avoided).
- **M2 so far (`src/IdleView.cs`):** the expedition view is driven by the simulation. One floor is one dungeon
  block (a random "N" block picked from the expedition's seed, with a random classic texture set); blocks whose
  longest route is under 80 m are skipped. The camera's place on the route comes from the time walked on the floor
  (3 m/s, back and forth), so a view can be opened at any moment. Fights stand the enemy 2.3 m ahead at the game's
  own sprite size; hits, misses and damage come from simulation events. Speed 1x/4x/16x, a "call home" order, and
  mouse buttons in the hub and the view (keys still work). Enemies show their struck frame when hit; a killed one
  drops and leaves the game's corpse sprite. An enemy is placed where the hero has a clear line to it (closer, or
  behind, when a corner is in the way) and the camera tilts towards it. The game's own sounds play for hits,
  misses, enemy barks, kills, gold, level-ups, floor changes and footsteps (fewer at 4x, almost none at 16x).
  Floors fade to black and back. M2 is done apart from a mouse-only run, which cannot be tested in the background.
- **M3, first part (`src/IdleMap.cs`, shop in `src/IdleHero.cs`):**
  - *Destinations (M in the hub):* one real dungeon per dungeon type, the nearest of that type to Daggerfall city
    in the game's map data, with travel time from the distance (12 to 90 s each way). The card lists who lives
    there from shallow to deep and marks enemies the hero's weapon cannot hurt. Chosen while away, a destination
    applies from the next run. Target floor and deepest cleared floor are kept per dungeon type.
  - *Skip cleared floors (K, on by default):* a run starts two floors above its target, never deeper than one
    past the deepest floor cleared there, so a run is about three floors however deep the hero is.
  - *Shop (S on the hero screen):* four weapons and four armor pieces at full value, restocked every time the
    hero comes home, better as hero level and deepest floor rise. Bought items go to the stash.
  - *Bigger pack (P):* +25 kg per step, 8 steps, 400 gold then four times more each step.
  - *Test:* an hour replayed in one step equals the same hour in 0.3 s steps (the M3 check). Every dungeon type is
    run 20 times; a new hero does best in the human and barbarian strongholds and worst in the giant stronghold.
    200 runs now take about 17 hours of game time instead of 122 (level 17, floor 30).
  - *Still open in M3:* the map picture with dots and regions, region difficulty, a smith screen (repairs are
    automatic), and gold still piles up late (2 million after 200 runs).
- **M4, first part (`src/IdleOrders.cs`, O in the hub):**
  - *Orders:* six "if CONDITION then ACTION" rules, first match wins on each of the hero's turns. Conditions:
    always, health below 50/30/15%, my weapon cannot hurt it, it is a champion or boss, its health is below 30%,
    off. Actions: attack, drink a healing potion, flee, power attack (-20 to hit, x1.5 damage), guard (no attack,
    enemy -40 to hit once). Defaults: potion below 30%, flee below 30%, flee when the weapon cannot hurt, attack.
    The old fixed flee threshold (`fleePercent`) is no longer used.
  - *Healing potions:* a count, not items. Heal 30% + 10. Bought in town before repairs up to "carry N"
    (15 + 3 x level gold). Between fights a hurt hero drinks before deciding to go home.
  - *Champions:* min(25%, 4% + floor) of fights, one trait (Swift, Armoured, Regenerating, Venomous, Vampiric,
    Enraged), x1.6 health, two loot rolls, double gold. Name and sprite tinted by trait.
  - *Bosses:* the last fight on every fifth floor, from two steps further down the table, two traits, x3 health,
    sprite 25% larger, three loot rolls and a magic item.
  - *Test:* first ten runs, 40 heroes: 0.6 deaths with orders and potions, 2.9 with neither. 200 runs: level 19,
    floor 30, 42 deaths (bosses bite late).
- **M4, spells (`src/IdleSpells.cs`, L in the hub):**
  - *Hero spells:* Shock (known from the start), Wizard's Fire, Ice Bolt, Toxic Cloud, Force Bolt, Heal, Shield,
    Paralysis, Spell Shield. Names, schools, elements and magnitudes from SPELLS.STD (Shield's raised); bought
    once from the spellbook screen (120 to 3000 gold). Cost is ours: tied to average magnitude, lowered by the
    school skill, which every cast trains.
  - *Magicka:* Intelligence + 4 per level. Full in town, +1.5% a second (more with Willpower) while walking.
  - *Orders:* new actions cast a damage spell (the strongest known one the enemy does not resist, from its
    career's tolerances), cast Heal / Shield / Paralysis / Spell Shield; new conditions "it casts spells" and
    "my magicka is above half". New defaults add Heal below 50% and damage spells when the weapon cannot hurt
    and on champions; saves still on the old defaults are moved to the new ones. Between fights Heal comes
    before potions.
  - *Enemy casters:* the game's own enemy spell lists (EnemyEntity), reduced to damage, drain magicka,
    paralyse, silence, absorb health, invisibility and Spell Shield. 35% of their turns while casts last
    (1 + level/6, bosses +2). Spells skip armour; the hero's Willpower and Luck give a saving throw, much better
    under Spell Shield; Shield absorbs them first.
  - *Test:* first ten runs, 40 heroes: 0.5 deaths (0.3 and floor 6.8 knowing every spell). 200 runs: level 21,
    floor 32, 56 deaths, 2669 spells cast against 400 enemy spells.
  - *Still open in M4:* racial resistances, material warnings outside the map card.
- **Casting hands:** the view plays the game's own casting animation (FIRE/FRST/POIS/SHOK/MJIC00C6.CIF, frames
  0-5-0 as in `FPSSpellCasting`) for the spell's element; Heal and Shield use the magic one. X in the view plays it
  as a test.
- **Main menu (`src/IdleMenu.cs`):** the stock start window is replaced (window factory, plus a swap if the stock
  one is already up) by a subclass of it: the game's night scene CHGN00I0 scaled to fill any width, and New Game,
  Load Game, Idle Adventurer, Settings (the game's advanced settings, menu drawn dimmed behind) and Exit, with
  arrows, Enter and N/L/I/O/E.
- **M5, affixes and rarity:** dropped weapons and armor can roll a prefix and/or suffix (Keen, Cruel, Sturdy,
  Nimble, Arcane; of the Leech, of Swiftness, of Toughness, of Mending, of Warding, of Sorcery). Chance
  min(40, 8 + floor)% (+20 on champions); Fine has one affix, Rare two, Heroic two at 1.5x (boss magic items).
  Values grow 6% a floor. Affixes live in our save beside each item list (`gearAffixes`...), keyed in memory by
  the item object. Price x1.5/2.2/3.2; names coloured by rarity; card lists the affixes; auto-equip counts them.
  The shop sells a Fine item now and then. Test: 200 runs reach floor 35 (32 before) in daedric gear.
  - *Still open in M5:* soul gems and the enchanter, guild ranks, trainer, house (gold sinks: gold still piles up,
    1.2 million after 200 runs).
- **Blocks:** all 93 "N" blocks give a route (140 to 430 m) when tested one by one with B in the view. A rejected
  block must be destroyed at once (`DestroyImmediate`): left until the end of the frame, its colliders end up in
  the next block's navmesh and the hero walks in the void where it used to be.
- **Item IDs:** the game's item IDs restart at every launch, so the hero's items can share one. Items are therefore
  saved one by one, never through `ItemCollection` (which is keyed by ID and silently dropped the hero's sword).
- **Hero screen (`src/IdleHero.cs`, key C in the hub):** attributes, combat skills, the nine worn slots, and a stash
  of 24 spare weapons and armor pieces. Usable weapons and armor the hero brings back (and anything auto-equip
  replaces) go to the stash instead of being sold; when it is full the cheapest piece is sold. Equip, take off and
  sell work by keys (arrows, Enter, X) and by mouse, at any time, also while the hero is away. Auto-equip only looks
  at new loot, so a manual choice stays until something better is found; it can be switched off (A). Bows are shown
  as unusable. The balance test gives about the same numbers as before (200 runs: level 22, floor 33).
- **M1 as built:** orc stronghold only, generic warrior hero, melee only. Orders so far: target floor, retreat
  below 35% health after a fight, flee below 25% during one, auto-repeat, go one floor deeper after a clear.
- **M1 balance (test with T in the hub, 40 heroes):** first run on floor 1: 60% clear, 35% retreat, 5% die. First
  ten runs: 0.8 deaths, level 2.7, 26 minutes. 50 runs: level 12, floor 15, about 7 hours. 200 runs: level 25,
  floor 34, about 130 hours, all Daedric.
- **Known open balance problems:** gold has nothing to be spent on yet, so it piles up (22 million after 130 hours;
  loot sells at a quarter of its value). Deep runs replay every floor from 1, so they take over an hour; a
  checkpoint or "start at floor N" belongs in M3. The carry limit throws away most deep loot.

## 1. Pillars

1. **Preparation is the game.** Every expedition asks a question the player answers with gear, spells and orders.
2. **Daggerfall's own content, not new art.** Its items, materials, enchantments, spells, bestiary, dungeon types and
   maps are the content. New systems sit on top (affixes, champions, depth).
3. **Watchable but skippable.** Fights are animated with the game's sprites; speed 1x / 4x / instant.
4. **Never touches a real playthrough.** Own save file, own hero, started from the main menu. **[decided]**

Non-goals for the first versions: a party, 3D spectating, quests with dialogue, the full skill list mattering.

## 2. What the engine gives us (scouted in the 1.1.1 source)

- **Enemies:** `EnemyBasics.Enemies` has 62 `MobileEnemy` definitions: health, level, armor, three damage ranges,
  `MinMetalToHit`, loot table key, soul points, and animation frame lists. 19 are human classes (ids 128-146), each
  with male and female texture archives and melee, bow and spell animations.
- **Sprites:** every enemy texture archive has 5 facings per state (records 0-4 move, 5-9 attack, 10-14 hurt,
  15-19 idle, 20-24 ranged or spell). `ImageReader.GetImageData("TEXTURE.nnn", record, frame, hasAlpha: true)`
  returns frames as textures, so a 2D battle scene needs no 3D objects. Facing 0 looks at the camera, facing 4 is seen
  from behind, facing 2 is a side view (mirror it for the other side).
- **Hero sprite [decided]:** no paperdoll animation. The hero is drawn with one of the human class sprites, picked
  from weapon and stats (section 6).
- **Encounter tables:** `RandomEncounters.EncounterTables` has a 20-enemy table per dungeon type (crypt, orc
  stronghold, mine, coven, vampire haunt, laboratory, harpy nest, spider nest, giant stronghold, dragon's den,
  volcanic caves, scorpion nest, cemetery...), ordered weak to strong. 19 dungeon types = 19 themed zones for free.
- **World data:** `MapsFile.GetRegion(i).MapTable` lists every location with type, dungeon type and
  longitude/latitude; `MapsFile.LongitudeLatitudeToMapPixel` gives map positions. The travel map art
  (`TRAV0I00.IMG`, the per-region `FMAP0I..` images) can be drawn in our own panel.
- **Items:** `ItemBuilder.CreateRandomWeapon(level)`, `CreateRandomArmor(level, gender, race)`,
  `CreateRandomMagicItem(...)`, potions, gems, soul traps, ingredients are static and take a level, so loot needs no
  live player. `LootTables.GetMatrix(key)` gives each enemy's drop chances. Items serialise with
  `ItemCollection.SerializeItems()` / `DeserializeItems()` (`ItemData_v1`).
- **Combat formulas:** `FormulaHelper.CalculateAttackDamage` reads `GameManager.Instance.PlayerEntity` and the
  on-screen weapon, so it cannot be called for a simulated fight. **We port the maths** (to-hit, armor per body part,
  material modifiers, skill and stat bonuses) into our own resolver. This is also what makes instant and offline
  simulation possible.
- **Entry point:** the main menu is `DaggerfallStartWindow`, a normal window with three buttons on `NativePanel`.
  Mods are initialised at the `Start` state, so we can add a fourth button to it at run time.
- **Reusable from the Junk and Loot Filter mod:** the modern UI kit (`Skin`, `FramedPanel`, `ItemTable`, `RowTable`,
  `NumField`, `ChipRow`, `TextBlock`, `Caption`, `KeyValueList`, `Meter`, `PopupMenu`, `Metrics`, `Ui`). Mods are
  compiled into separate assemblies and cannot reference each other, so the kit moves to `dfu-mods/common/` and the
  build script compiles it into both mods.

To verify first (milestone 0): that item creation, the effect broker and image loading all work at the main menu
with no game loaded. If not, the mode starts a hidden "void" game state instead.

## 3. Architecture

```
dfu-mods/
  common/ModernUiKit.cs          shared widgets (moved out of junk-loot-filter)
  idle-adventurer/
    src/IdleCore.cs              entry, save/load, clock, mode switch
    src/IdleSim.cs               pure simulation: hero, expedition, combat, loot (no Unity time, no UI)
    src/IdleData.cs              tuning tables: zones, depth scaling, affixes, champions, prices
    src/IdleScreens.cs           hub, map, battle view, hero sheet, shop/services, orders editor
    tools/build_mod.py           same donor-bundle approach; our own junk-loot-filter.dfmod works as donor
```

**Rule: the simulation is a pure, deterministic state machine.** `IdleSim.Advance(state, seconds)` moves the world
forward and appends events (fight started, hit, loot, level up, died, returned). It never reads `Time`, never
touches UI, and uses its own seeded random generator stored in the save. Consequences:

- Watching = advance a little each frame and play the events as animations.
- 4x = advance four times as much. Instant = advance in big steps and show only the summary.
- Offline = on load, `Advance(state, min(now - lastSeen, cap))`. **[decided: system clock]**
- Balance testing = run 10,000 expeditions in a debug command and dump a CSV.

Compiler limits (DFU's runtime compiler): C# 6 at most, no enums of our own (use int constants), no nested
serialisable classes for `JsonUtility`, no tuples. Save = one flat JSON file plus `ItemData_v1[]` blobs.

## 4. Game loop and pacing

```
Town (prepare) -> Travel -> Dungeon: floor 1..N (encounters, loot, boss) -> Return -> Town (sell, repair, upgrade)
```

- **Time:** simulation runs on game minutes. Baseline 1 real second = 2 game minutes while watching at 1x. Target:
  a fight 5-15 s, a floor 1-2 min, an early expedition 5-8 min, a late one 20-30 min, all at 1x.
- **Town phase is not timed.** The hero waits until sent. With "auto-repeat" on, the hero re-supplies from a
  shopping list and leaves again; this is what makes away progress meaningful.
- **Offline cap:** 12 hours of simulated progress per absence, and only if auto-repeat was on or an expedition was
  under way. A "while you were away" summary lists runs, loot kept, deaths.
- **Push or retreat:** per-expedition orders set a target depth and retreat conditions (health potions left,
  inventory full, hero below X% health with no potions). Loot is banked only on returning to town.
- **Death:** the hero is carried home, loses the unbanked loot and some gold, and gear loses condition. No
  permadeath by default (option for a hardcore save later).

## 5. World

- **Map screen:** region picker, then the region map with town and dungeon dots (from `MapTable`). The hero is a
  marker that walks the line from the home town to the dungeon; days tick, and road events can fire (ambush, find,
  weather delay). Travel time scales with real distance, compressed (log-style) so far dungeons cost more supplies
  and risk, not ten times the waiting.
- **Zones:** a dungeon's type picks its encounter table; its region sets base difficulty (starting regions easy,
  the far corners hard). So "a vampire haunt in Wrothgaria" is a different proposition from one in Daggerfall.
- **Depth:** each dungeon has floors. Floor n rolls enemies from further down the type's table and scales enemy
  level, health and damage. Depth is endless past the table's end (scaling + champions), which is the long tail.
- **Champions:** an enemy with one modifier: Swift, Armoured, Regenerating, Venomous, Spell-reflecting, Vampiric,
  Enraged. Better loot. Implemented as multipliers and hooks in the resolver, no new art (tint the sprite).
- **Bosses:** last floor of each dungeon tier: a named, scaled enemy from the type's top end (lich, vampire
  ancient, daedra lord, dragonling) with two modifiers and a guaranteed rare item.
- **Unlocking:** clearing a dungeon's boss unlocks the next region tier and raises guild standing.

## 6. Hero

- **Creation:** name, race, gender, one of the game's classes (reuse the stock class list for starting stats,
  skills, forbidden gear). No custom-class editor at first.
- **Growth:** Daggerfall style. Skills rise by use in the simulation (weapon skill on swings, dodging, the magic
  schools on casts); enough rises give a level and attribute points the player assigns.
- **Stats that matter in the sim:** health, magicka, fatigue; Strength, Agility, Speed, Endurance, Willpower,
  Intelligence, Luck; weapon skills, Dodging, Critical Strike, Backstabbing, the six schools, Medical (rest
  healing), Mercantile (prices), Stealth (chance of a free first strike). Everything else is shown but inert at
  first.
- **Battle sprite [decided]:** chosen by gear and stats, with the hero's gender:
  bow equipped -> Archer or Ranger; spell-first orders or magicka-heavy -> Mage, Sorcerer, Battlemage;
  dagger or short blade and light armor -> Thief, Assassin, Nightblade; hand-to-hand -> Monk;
  plate and shield -> Knight; axe or blunt -> Barbarian; otherwise Warrior.

## 7. Combat resolver

Ticks of one game second. Each combatant has an action timer from Speed and weapon weight.

- **To hit:** ported from the stock formula: attacker weapon skill, Agility and Luck, minus defender Dodging and
  the armor value of a randomly struck body part.
- **Damage:** weapon base range + material modifier + Strength bonus; hand-to-hand from skill; criticals from
  Critical Strike; enemy three-part attacks as in their definitions.
- **Material gate:** `MinMetalToHit` is honoured. Bringing an iron sword to a wraith does nothing. This is the
  main "did you prepare" check and is shown on the dungeon's info card before leaving.
- **Magic:** the hero's spellbook comes from the stock spell maker (our modern window). In the sim a spell is
  reduced to its effects with simple rules: damage, heal, fortify/drain as timed stat changes, shield, paralysis,
  silence, resistances. Enemy casters use their class spell lists the same way. Resistances and saving throws
  ported from `FormulaHelper.SavingThrow`.
- **Consumables:** potions map to the same effect rules. Ammunition is counted. Gear condition drops and broken
  gear stops working.
- **Orders ("tactics"):** an ordered list of rules, first match wins:
  `[condition] -> [action]`, e.g. `health < 30% -> drink healing potion`, `enemy is undead -> cast Sunburst`,
  `enemy needs silver+ and I lack it -> flee`, `default -> attack with weapon`. Conditions and actions are int
  codes in the save. 4 rule slots at first, more unlocked by Intelligence or guild rank.

## 8. Items and economy

- **Stock items stay real `DaggerfallUnityItem`s**, so materials, enchantments, condition, the enchanter and the
  hover comparison all keep working.
- **Affixes (new):** dropped gear can roll a prefix and a suffix stored in our save keyed by item UID (not in the
  item, so nothing leaks into DFU's data): "+8% to hit", "+12 fire resistance", "of the Leech (heal 2 per kill)".
  Rarity tiers by affix count, shown by name colour. Affix power scales with dungeon depth.
- **Loot:** per kill, roll the enemy's stock loot matrix through `ItemBuilder` with an effective level from depth,
  then roll affixes. Bosses and champions add guaranteed rolls. The existing loot filter idea returns as "keep
  rules" (auto-sell junk on return).
- **Soul gems:** stock mechanic. Carrying empty gems and a soul trap rule fills them; souls feed the enchanter.
- **Town services:** shop (buy/sell with Mercantile), smith (repair), temple (cure, blessings as timed buffs),
  Mages Guild (spell maker and item enchanter: the modern windows we already have, opened on the idle hero),
  trainer (gold for skill points up to a cap). Guild rank gates services, as in the base game.
- **Gold sinks for the long run:** training, enchanting, a house upgrade that raises the offline cap and storage,
  cart (bigger haul per run).

## 9. Screens (all in the modern style, built from the shared kit)

1. **Hub:** hero card (sprite, level, bars), current activity with progress, big buttons: Expedition, Hero,
   Town, Orders. Shows the away summary on load.
2. **Map:** region and dungeon picker with info card (type, difficulty, known enemies, material warnings).
3. **Expedition view:** battle scene (dungeon wall backdrop from stock textures, enemy facing the camera, hero
   seen from behind at the lower left; hit, hurt and death frames; damage numbers), combat log, loot ticker, floor
   and supplies bar, speed buttons, Retreat.
4. **Hero:** the modern inventory layout reused: gear column, item table, hover comparison, plus stats and skills.
5. **Town:** tabs for shop, smith, temple, guild, trainer, storage.
6. **Orders:** rule list editor with dropdown chips.

## 10. Milestones

Each ends with something runnable and a check.

- **M0 Spike (small):** main-menu button opens an empty hub; create an item, load an enemy sprite frame and draw
  it, write and read the save file. *Check: no game loaded, no errors, DFU saves untouched.*
- **M1 Sim core, no UI:** hero, one dungeon type, combat resolver, loot, death, return; debug command runs N
  expeditions and prints a summary. *Check: deterministic for a seed; win rates sensible for a new hero on floor 1.*
- **M2 Watchable loop:** hub, expedition view with animated fights at 1x/4x/instant, hero screen with equip.
  *Check: a full run start to finish by mouse only.*
- **M3 World and town:** map travel, all dungeon types, depth, shop/smith/sell, auto-repeat, offline catch-up.
  *Check: close the game for an hour, summary matches a replay of the same seed.*
- **M4 Depth of play:** orders editor, spells and potions in the sim, champions, bosses, material warnings.
- **M5 Long tail:** affixes and rarity, soul gems and enchanter hookup, guild ranks, trainer, house.
- **M6 Polish for release:** balance pass with the CSV harness, tutorial tips, settings, README, Nexus page.

M0-M2 are the proof of concept to show the community.

## 11. Risks

- **Main-menu context:** some DFU singletons may assume a loaded game. M0 exists to find out early.
- **Formula drift:** our resolver is a port, not the real thing. Accept it; the mode's numbers only need to be
  consistent with themselves and feel like Daggerfall.
- **Balance over a long tail:** idle games live or die on the curve. The deterministic sim and the CSV harness are
  the mitigation; budget real time for M6.
- **Save compatibility:** version field from day one, migrations as in the loot filter mod.
- **Testing:** the game can be launched hidden and driven by test hooks, but mouse paths need the user or the
  planned in-mod click harness.

## 12. Open questions (defaults chosen, change any time)

1. Death penalty: lose unbanked loot + 10% gold + gear wear (default), or harsher?
2. Target length of an early expedition at 1x: 5-8 minutes (default)?
3. Offline cap: 12 hours (default)?
4. Name of the mod.
