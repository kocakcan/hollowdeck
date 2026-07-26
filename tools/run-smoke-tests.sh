#!/usr/bin/env bash
#
# Runs every headless smoke test in scenes/debug/ and fails if any of them do.
#
#   tools/run-smoke-tests.sh                 # all of them
#   tools/run-smoke-tests.sh MapSmokeTest    # just these
#
# Each test scene prints "<Name>: N passed, M failed" and exits nonzero if
# anything failed (see any scripts/debug/*SmokeTest.cs for the shape). This
# script is the CI entry point ROADMAP.md Phase 9 asks for - point a workflow
# at it and the build fails on a broken test.
#
# The three visual scenes (ArtScreenshot, AnimationScreenshot,
# StyleReferenceScreen) are deliberately NOT run here: they need a real
# renderer, produce no pass/fail, and exist to be looked at. Use
# scenes/debug/ScreenShot.tscn for that (see the verify-screen skill).

set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 1

GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if [[ ! -x "$GODOT" ]]; then
    echo "error: Godot not found at '$GODOT'. Set GODOT=/path/to/godot." >&2
    exit 1
fi

# C# changes are compiled ahead of time - without this, a test can silently
# run against the previous build and "pass" a bug you just introduced.
echo "== dotnet build =="
if ! dotnet build -v q --nologo; then
    echo "error: build failed; not running tests." >&2
    exit 1
fi

if [[ $# -gt 0 ]]; then
    tests=("$@")
else
    tests=()
    for scene in scenes/debug/*SmokeTest.tscn; do
        tests+=("$(basename "$scene" .tscn)")
    done
fi

echo
failed=()
for name in "${tests[@]}"; do
    scene="scenes/debug/${name}.tscn"
    if [[ ! -f "$scene" ]]; then
        echo "MISSING  ${name} (no ${scene})"
        failed+=("$name")
        continue
    fi

    output=$("$GODOT" --headless --path . "$scene" 2>&1)
    status=$?
    summary=$(grep -E "[0-9]+ passed, [0-9]+ failed" <<<"$output" | tail -1)

    if [[ $status -ne 0 || -z "$summary" ]]; then
        # A crash mid-run prints no summary line at all, which is exactly the
        # case a bare grep for "0 failed" would silently treat as a pass.
        echo "FAIL     ${name} (exit ${status}) ${summary:-<no summary line - crashed?>}"
        grep -E "^FAIL |SCRIPT ERROR|Unhandled exception" <<<"$output" | head -5 | sed 's/^/           /'
        failed+=("$name")
    else
        echo "ok       ${summary}"
    fi
done

echo
if [[ ${#failed[@]} -gt 0 ]]; then
    echo "${#failed[@]} of ${#tests[@]} suites failed: ${failed[*]}"
    exit 1
fi
echo "all ${#tests[@]} suites passed"
