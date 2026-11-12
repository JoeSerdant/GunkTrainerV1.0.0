#!/usr/bin/env bash
# Preklad GunkTrainer.dll (Mono mcs).
#   ./build.sh <cesta ke slozce hry Gunk> <cesta k BepInEx/core> <vystupni dll>
set -euo pipefail

GAME="${1:?cesta ke slozce hry (obsahuje Gunk_Data)}"
BEPCORE="${2:?cesta k BepInEx/core}"
OUT="${3:-GunkTrainer.dll}"
HERE="$(cd "$(dirname "$0")" && pwd)"
M="$GAME/Gunk_Data/Managed"

REFS=()
for dll in mscorlib netstandard System System.Core \
           UnityEngine UnityEngine.CoreModule UnityEngine.IMGUIModule UnityEngine.InputLegacyModule \
           UnityEngine.PhysicsModule UnityEngine.AnimationModule UnityEngine.AudioModule \
           UnityEngine.UI UnityEngine.UIModule UnityEngine.TextRenderingModule Unity.TextMeshPro \
           Assembly-CSharp; do
  REFS+=("-r:$M/$dll.dll")
done
REFS+=("-r:$BEPCORE/BepInEx.dll" "-r:$BEPCORE/0Harmony.dll")

mcs -target:library -nostdlib -noconfig -optimize+ -debug- -codepage:utf8 -langversion:7 \
    -nowarn:1701,1702 "${REFS[@]}" -out:"$OUT" "$HERE/GunkTrainer.cs"
echo "OK -> $OUT"
