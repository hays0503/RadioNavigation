"""Has the AssetBundle been rebuilt with the lit-wall fix?

Two facts that shape this checker, both established by probing the real file:

  1. The bundle is BUILT UNCOMPRESSED (BuildTestUIBundle.cs passes
     UncompressedAssetBundle), so text in it is greppable.
  2. Unity STRIPS THE SHADER BODY at build time - the bundle contains no
     `smoothstep`, no `fixed(`, none of the fragment code. Only the Properties
     block survives, with the variable names and their display names.

So the body cannot be verified from the bundle, and any checker that looks for
`wallDistance = length(p)` there is looking for something Unity removed. What
IS verifiable offline is the property declaration `_LuneWallAnchor`: it is in
the Properties block now, and it is absent from a bundle built before the fix.

The control property matters as much as the marker. If the control is missing
too, the scan method does not work on this file and a zero for the marker means
nothing - the checker must say which of the two cases it is in rather than
reporting a bare absence.
"""
import re
import sys

CONTROL = "_LuneRimLight"      # in the shader today; must be in any good build
MARKER = "_LuneWallAnchor"     # added by the lit-wall fix


def count(blob, token):
    return len(re.findall(re.escape(token), blob))


def main(path):
    with open(path, "rb") as fh:
        blob = fh.read().decode("latin-1")
    control = count(blob, CONTROL)
    marker = count(blob, MARKER)
    print(f"{path}")
    print(f"  size {len(blob)} bytes")
    print(f"  control  {CONTROL:16s} {control} occurrence(s)")
    print(f"  marker   {MARKER:16s} {marker} occurrence(s)")

    if control == 0:
        print("\n  INCONCLUSIVE: the control property is absent, so this scan")
        print("  cannot see shader properties in this file at all. A zero for")
        print("  the marker means nothing. Do not conclude anything from this.")
        return 2
    if marker == 0:
        print("\n  RESULT: STALE BUNDLE. The shader properties are readable and")
        print("  the lit-wall marker is not there, so the bundle predates the")
        print("  fix and the game is still running the old shader - the lit wall")
        print("  still rides the pupil and the arc in the field is expected.")
        print("\n  To fix: in the Unity project (TestUI, 2021.3.33f1) run")
        print("  Assets/Editor/BuildTestUIBundle.cs -> menu 'Build/Build TestUI")
        print("  Bundle', then re-run this check, then restart the game.")
        return 1
    print("\n  RESULT: the bundle carries the fixed shader. Start the game and")
    print("  confirm in the log: the LUNE line must show wallfix=1 (and no")
    print("  'bundle is STALE' warning). The log is the in-game proof; this is")
    print("  only the offline one.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
