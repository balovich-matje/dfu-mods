# Junk and Loot Filter (Daggerfall Unity mod)

Built for DFU 1.1.1. The mod is plain C# in `src/`; DFU compiles it when the game starts.

## Features
- **Quick Loot** plate on a corpse's loot window (default key `L`): takes everything the loot filter allows, plus gold
  and quest items, then closes the window. If you can't carry it all, it stays open and says how much is left.
- **Loot filter** (FILTER button / `F`): toggle the four inventory categories and the twelve materials, plus a
  white list (name contains... always looted) and black list (never looted; the white list wins).
- **JUNK** button (`K`): click items to tag/untag them. Tags are case-insensitive regexes matched against the item
  name (default `^Iron Dagger$`), so they also catch future loot. Tagged items are greyed with a gold coin.
  Magic items, quest items and cargo can't be tagged.
- **LOCK** button (`O`): click items to lock/unlock them. Locked items get a blue frame and a padlock and are never
  sold, not by Quick Sell, not by the junk pre-selection, not by hand. Locks are saved with each game.
- **Q.SELL** (`Q`), **JUNK**, **LOCK** in a vendor's sell window: Quick Sell moves everything the sell filter and
  the junk list allow into the basket and opens the normal price/confirm dialog. Only items the shop buys are
  considered. Junk is also added to the basket automatically when the window opens (toggle in the JUNK tab).
- **Skyrim-style vitals bars**: magicka (left), health (centre, red) and stamina (right, green), all the same size,
  along the bottom of the screen with `current / max` inside each bar and DFU's own compass border as the frame.
  DFU's own bars are moved off screen.
- **Effect names and borders**: the active effect icons sit in the top left corner of the screen, 8 to a row, each
  with its name underneath and a green border for buffs or a red one for debuffs (harmful effects such as damage,
  drain, paralysis, silence, disease and cursed item enchantments count as debuffs). Buffs are in the top row,
  debuffs in the row below.
- **Wide compass**: a stretched compass with letters and ticks at the top centre of the screen, in place of DFU's
  small one in the bottom right corner. Detect spell markers still show, just under the bar.
- **Fading interaction icon**: the grab/info/steal/talk icon moves to the bottom left, stays fully visible for 5
  seconds after the mode is switched (or a game is loaded) and then fades out over the next 5. Does nothing for
  DFU's crosshair-style icons.
- **Corpse clean up**: empty corpses fade out and disappear after 5 seconds, and corpses that a quick loot left
  items in after 10 seconds. A corpse is left alone while its inventory is open, and quest corpses are never touched.
- **KEYS** tab in the settings window: rebind all of the above (click a row, or Up/Down + Enter, then press a key;
  Escape cancels, Backspace unbinds).
- **MISC** tab: switch each of the HUD features and the corpse clean up on or off, and set the window size.
- **Window size** (default 70%): DFU's windows (inventory, talk, shops, character sheet...) open smaller and centred
  instead of filling the screen. Small dialogs shrink half as much so they stay readable. The main menu, character
  creation, maps, books and videos stay full size.
- Sell rules: the black list always wins, magic items follow only the Magic toggle.
- The item info panel follows the cursor instead of sitting in the button column, which frees the room the extra
  buttons use, and DFU's own name tooltip is hidden so you don't get two. It also shows what DFU only reveals after a
  second click on INFO: an item's magic powers, or a potion recipe's ingredients. For gear that goes in a slot you
  have filled it adds a "vs <equipped item>" block: damage, armor, condition and weight deltas in green (better) or
  red (worse), and "Gains/Loses magic powers". (Only when DFU's "inventory info panel" option is on; otherwise
  DFU's own tooltips are used.)

Settings are saved to `JunkLootFilter.json` in DFU's data folder; item locks go into each save game.

## Build and install
```
python3 -m venv venv && venv/bin/pip install UnityPy
venv/bin/python tools/build_mod.py DONOR.dfmod build/junk-loot-filter.dfmod
cp build/junk-loot-filter.dfmod "<DaggerfallUnity.app>/Contents/Resources/Data/StreamingAssets/Mods/"
```
`DONOR.dfmod` is any small script-only DFU mod that contains `LeaveGuild.cs`, `LeaveGuildSaveData.cs`,
`LeaveGuildServicePopupWindow.cs` and `LeaveGuild.dfmod` text assets (we used Berzeger's *Leave Guild* 1.0.5, MIT):
`build_mod.py` keeps its bundle skeleton and swaps in our sources. No Unity Editor needed.

## Compatibility notes
- Replaces DFU's Inventory and Trade windows, so it conflicts with other mods that do the same.
- Runtime-compiled mods can't use enums, optional enum parameters, or nested serializable classes for JsonUtility
  (see comments in the source).
