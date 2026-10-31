#!/usr/bin/env python3
"""Builds junk-loot-filter.dfmod without the Unity Editor.

A Daggerfall Unity mod is a Unity AssetBundle that holds the C# sources as TextAssets (*.cs.txt) plus a
*.dfmod.json manifest; DFU compiles the sources when the mod loads. UnityPy can't create a bundle from
nothing, so this script takes any small script-only DFU mod as a "donor", swaps its text assets for ours and
rewrites the AssetBundle's asset table.

usage: build_mod.py DONOR.dfmod OUTPUT.dfmod
"""
import json
import os
import sys

import UnityPy

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "src")
MOD_NAME = "JunkLootFilter"
MOD_TITLE = "Junk and Loot Filter"
SOURCES = ["JunkLootCore.cs", "JunkLootWindows.cs", "JunkLootSettingsWindow.cs"]

MANIFEST = {
    "ModTitle": MOD_TITLE,
    "ModVersion": "0.1.0",
    "ModAuthor": "balovich-matje",
    "DFUnity_Version": "1.0.0",
    "ModDescription": "Quick loot with a loot filter, quick sell, and always-sell junk tagging.",
    "GUID": "8c6f1d52-3a0e-4b7a-9e44-5d2b7a1c90f3",
    "Files": [
        "Assets/Game/Mods/%s/%s.dfmod.json" % (MOD_NAME, MOD_NAME),
    ] + ["Assets/Game/Mods/%s/Scripts/%s" % (MOD_NAME, s) for s in SOURCES],
    "Dependencies": [],
}


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    donor_path, out_path = sys.argv[1], sys.argv[2]

    env = UnityPy.load(donor_path)
    sf = list(env.file.files.values())[0]
    text_assets = [o for o in sf.objects.values() if o.type.name == "TextAsset"]
    bundle_obj = [o for o in sf.objects.values() if o.type.name == "AssetBundle"][0]

    # Donor text assets we can reuse: keep exactly len(SOURCES) + 1 (sources + manifest), drop the rest.
    by_name = {}
    for o in text_assets:
        by_name[o.read_typetree()["m_Name"]] = o
    donor_slots = [by_name["LeaveGuild.cs"], by_name["LeaveGuildSaveData.cs"],
                   by_name["LeaveGuildServicePopupWindow.cs"]]
    manifest_slot = by_name["LeaveGuild.dfmod"]
    keep = set(o.path_id for o in donor_slots) | {manifest_slot.path_id, bundle_obj.path_id}

    container = []  # (key, path_id)

    for slot, src in zip(donor_slots, SOURCES):
        with open(os.path.join(SRC, src), "r", encoding="utf-8") as fh:
            text = fh.read()
        tree = slot.read_typetree()
        tree["m_Name"] = src
        tree["m_Script"] = text
        slot.save_typetree(tree)
        container.append(("assets/untracked/modbuilder/%s/%s.txt" % (MOD_TITLE.lower(), src.lower()), slot.path_id))

    tree = manifest_slot.read_typetree()
    tree["m_Name"] = MOD_NAME + ".dfmod"
    tree["m_Script"] = json.dumps(MANIFEST, indent=4)
    manifest_slot.save_typetree(tree)
    container.append(("assets/game/mods/%s/%s.dfmod.json" % (MOD_NAME.lower(), MOD_NAME.lower()), manifest_slot.path_id))

    # Remove everything else (donor settings file, texture, ...).
    for path_id in list(sf.objects.keys()):
        if path_id not in keep:
            del sf.objects[path_id]

    asset_ids = [pid for _, pid in container]
    tree = bundle_obj.read_typetree()
    tree["m_Name"] = MOD_TITLE.lower() + ".dfmod"
    tree["m_AssetBundleName"] = MOD_TITLE.lower() + ".dfmod"
    tree["m_PreloadTable"] = [{"m_FileID": 0, "m_PathID": pid} for pid in asset_ids]
    tree["m_Container"] = [
        (key, {"preloadIndex": i, "preloadSize": 1, "asset": {"m_FileID": 0, "m_PathID": pid}})
        for i, (key, pid) in enumerate(container)
    ]
    bundle_obj.save_typetree(tree)

    data = env.file.save(packer="lz4")
    with open(out_path, "wb") as fh:
        fh.write(data)

    # Read it back as a sanity check.
    check = UnityPy.load(out_path)
    names = []
    for o in check.objects:
        if o.type.name == "TextAsset":
            d = o.read()
            names.append((d.m_Name, len(d.m_Script)))
        elif o.type.name == "AssetBundle":
            for key, info in o.read_typetree()["m_Container"]:
                print("container:", key)
    print("text assets:", names)
    print("wrote", out_path, os.path.getsize(out_path), "bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
