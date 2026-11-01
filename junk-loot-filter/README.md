# Junk and Loot Filter (Daggerfall Unity mod)

Built for DFU 1.1.1. The mod is plain C# in `src/`; DFU compiles it when the game starts.

## Features
- **Modern inventory** (replaces DFU's classic inventory window): a big table window in the style of SkyUI. Top row:
  the twelve trinket slots (amulets, bracelets, rings, bracers, marks, crystals, in pairs) and five category tabs - Weapons,
  Armor, Clothes (jewelry too), Ingredients, Misc - with item counts; magic items sit in the tab of their type, shown
  in blue. Middle: a column of equipped gear (icons only, with the armor class of each armor slot and the damage of the
  weapons; hover for the name), the paper doll as a plain picture, and a table of the open tab with icon, type, name, weight, price and
  gold per kg (click a header to sort; the mouse wheel scrolls). Bottom bar: Wagon, Gold, Setup, Exit.
  - Left click equips or uses an item, `Shift`+click moves it to the side panel (or drops it), middle click cycles
    variants. Left click on an equipped item takes it off.
  - **Right click** opens a menu: Equip/Use, Info, Move to cart/Drop, Lock, Tag as junk (and Unequip for worn gear).
  - Number keys `1`-`5` pick the tab. `K` tags and `O` locks the item under the mouse, `T` quick stacks, `L` quick
    loots, `F` opens the settings (all rebindable).
  - **Side panel** at the right edge while the cart, a corpse/chest or the ground has items:
    a table of that container, its weight, and its own button: **Quick stack**, **Quick loot** or nothing.
  - The window and the side panel together take at most 90% of the screen; the inventory stays put when the
    side panel opens.
  - Two looks, chosen in the settings (MISC tab, "Inventory frames"): separate frames, or one shared container.
- **Quick Loot** button on a corpse's or chest's side panel (default key `L`): takes everything the loot filter
  allows, plus gold and quest items, then closes the window. If you can't carry it all, it stays open and says how
  much is left.
- **Quick stack** button while the cart is open (default key `T`): moves everything on the open tab into the cart,
  except locked items (and equipped, quest and cargo items). Splits stacks if the cart is nearly full.
- **Loot filter** (Setup button / `F`, the mod's settings window): toggle the five categories and the twelve
  materials, plus "Magic items (any type)", a white list (name contains... always looted) and a black list (never
  looted; the white list wins). "Ignore materials for magic items" (on by default) takes enchanted weapons and armor
  whatever their material.
- **Junk tags**: tag an item (menu or `K`) and it is matched by name (a case-insensitive regex, default
  `^Iron Dagger$`), so it also catches future loot. Tagged items are greyed with a gold coin. Magic items, quest
  items and cargo can't be tagged.
- **Locks** (menu or `O`): locked items get a blue frame and a padlock and are never sold, quick stacked or
  pre-selected for sale. Locks are saved with each game.
- **Vendor windows** (still DFU's classic layout for now): **Q.SELL** (`Q`), **JUNK** and **LOCK** buttons. Quick
  Sell moves everything the sell filter and the junk list allow into the basket and opens the normal price/confirm
  dialog. Only items the shop buys are considered. Junk is also added to the basket automatically when the window
  opens (toggle in the JUNK tab).
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
- **Window size** (default 70%): DFU's other windows (talk, shops, character sheet...) open smaller and centred
  instead of filling the screen. The modern inventory ignores this and uses about 86% of the screen height. Small dialogs shrink half as much so they stay readable. The main menu, character
  creation, maps, books and videos stay full size.
- Sell rules: the black list always wins, magic items follow only the Magic toggle.
- The item hover box follows the cursor, in the inventory and in shops. It shows what DFU only reveals after a
  second click on INFO: an item's magic powers, or a potion recipe's ingredients. For gear that goes in a slot you
  have filled it adds a "vs <equipped item>" block (a one-handed weapon is compared with the weapon in your other
  hand when its own slot is empty): damage, armor and weight deltas in green (better) or red (worse), and
  "Gains/Loses magic powers". The condition shows after the name, as in "Daedric Tanto (85%)". The box is only as
  wide as its text, framed with DFU's compass border. (In shop windows it needs DFU's "inventory info panel" option.)

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
