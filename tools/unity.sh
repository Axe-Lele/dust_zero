#!/usr/bin/env bash
# Run the Unity editor in batch mode against the DustZero project.
#   tools/unity.sh [extra unity args...]
# Log is written to build/unity-<label>.log and tailed on failure.
set -uo pipefail

UNITY="/c/Program Files/Unity/Hub/Editor/6000.4.5f1/Editor/Unity.exe"
PROJECT="C:\\Users\\Master\\Documents\\Project\\clean-girls\\DustZeroUnity"
ROOT="/c/Users/Master/Documents/Project/clean-girls"
LABEL="${LABEL:-run}"
LOG_POSIX="$ROOT/build/unity-$LABEL.log"
LOG_WIN="C:\\Users\\Master\\Documents\\Project\\clean-girls\\build\\unity-$LABEL.log"

mkdir -p "$ROOT/build"
rm -f "$LOG_POSIX"

# GRAPHICS=1 keeps a graphics device so ScreenCapture can produce real screenshots.
if [ "${GRAPHICS:-0}" = "1" ]; then
  "$UNITY" -batchmode -projectPath "$PROJECT" -logFile "$LOG_WIN" "$@"
else
  "$UNITY" -batchmode -nographics -projectPath "$PROJECT" -logFile "$LOG_WIN" "$@"
fi
CODE=$?

echo "=== unity exit: $CODE (log: build/unity-$LABEL.log) ==="
if [ -f "$LOG_POSIX" ]; then
  echo "--- errors / warnings ---"
  grep -nE "error CS|Error building|Compilation failed|Exception|Failed to resolve|Cannot resolve|\[Package Manager\].*error|FAILED|Assertion" "$LOG_POSIX" | head -60
fi
exit $CODE
