# Idle Adventurer (Daggerfall Unity mod, work in progress)

An idle side game mode for DFU 1.1.1: your hero explores and fights alone, you manage gear, spells and supplies.
It has its own save file (`IdleAdventurer.json` in DFU's data folder) and never touches normal saves.

Status: milestone 0 (proof of concept). The main menu gets an **Idle Adventurer** button (key `I`) that opens a test
screen: two animated sprites, a generated item and a save-file check, all with no game loaded. See [PLAN.md](PLAN.md).

## Build and install
```
python3 -m venv venv && venv/bin/pip install UnityPy
venv/bin/python tools/build_mod.py DONOR.dfmod build/idle-adventurer.dfmod
cp build/idle-adventurer.dfmod "<DaggerfallUnity.app>/Contents/Resources/Data/StreamingAssets/Mods/"
```
`DONOR.dfmod` is any small script-only DFU mod (a built `junk-loot-filter.dfmod` works).
