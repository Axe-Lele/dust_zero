#!/usr/bin/env bash
# CCGS entrypoints; existing tools/unity.sh is preserved.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
action="${1:-help}"
case "$action" in
  compile)
    LABEL=ccgs-compile bash tools/unity.sh -quit
    ;;
  test)
    # Use a new directory each run so stale XML can never count as evidence.
    result_dir="$ROOT/build/ccgs-tests/$(date +%Y%m%d-%H%M%S)-$$"
    mkdir -p "$result_dir"
    # Do not pass -quit to the Unity Test Runner; it exits after finishing.
    LABEL=ccgs-editmode bash tools/unity.sh -runTests -testPlatform EditMode -testResults "$result_dir/editmode.xml"
    LABEL=ccgs-playmode bash tools/unity.sh -runTests -testPlatform PlayMode -testResults "$result_dir/playmode.xml"
    python tools/ccgs-test-results.py "$result_dir/editmode.xml" "$result_dir/playmode.xml"
    ;;
  build)
    echo 'NOT IMPLEMENTED: add a real player build method and enabled gameplay scenes before configuring commands.build.' >&2
    exit 3
    ;;
  run)
    echo 'NOT IMPLEMENTED: configure commands.run to launch the real built game. Opening the editor is not a gameplay verification.' >&2
    exit 3
    ;;
  *)
    echo 'Usage: bash tools/ccgs-unity.sh compile|test|build|run'
    echo 'Compile/tests need the Unity project closed in other editor processes. Do not kill an existing editor.'
    ;;
esac
