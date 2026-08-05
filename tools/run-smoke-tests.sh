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
#
# BalanceReport is skipped for the second half of that reason alone - it is
# headless, but it prints a curve for a human to read rather than passing or
# failing anything, and this script swallows a suite's stdout to show only its
# summary line. Run it with tools/balance-report.sh. The assertions that belong
# in a sweep are BalanceSmokeTest, which is picked up normally.

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

# ART_SPEC's asset rules (grid, palette, hard alpha, no SVG) - see
# tools/artgen/src/validate.rs. This runs on the raw PNG bytes, which the
# engine-side suites cannot do: GD.Load hands back an already-imported
# texture. Skipped with a warning rather than failed if cargo is absent, so
# the smoke suite still runs on a machine without a Rust toolchain.
echo
echo "== artgen validate =="
if command -v cargo >/dev/null 2>&1; then
    if ! cargo run --release --quiet --manifest-path tools/artgen/Cargo.toml -- validate; then
        echo "error: assets violate docs/ART_SPEC.md; not running tests." >&2
        exit 1
    fi
else
    echo "skipped: cargo not on PATH (asset spec unchecked)"
fi

# Per-suite watchdog, in seconds. A test that throws inside _Ready - a
# GetNode against a path a .tscn no longer has is the usual way - never
# reaches its GetTree().Quit(), so Godot drops into an idle main loop and
# sits there. The suite does not fail; it stops, and takes the whole sweep
# with it. The full run is meant to take well under a minute, so anything
# past this is that hang and gets reported as a failure.
#
# `timeout(1)` is not installed on macOS, hence doing it by hand.
SUITE_TIMEOUT="${SUITE_TIMEOUT:-90}"

run_suite() {
    local scene="$1"
    local out
    out=$(mktemp)

    "$GODOT" --headless --path . "$scene" >"$out" 2>&1 &
    local pid=$!

    local waited=0
    while kill -0 "$pid" 2>/dev/null; do
        if (( waited >= SUITE_TIMEOUT )); then
            kill -9 "$pid" 2>/dev/null
            wait "$pid" 2>/dev/null
            cat "$out"
            rm -f "$out"
            return 124
        fi
        sleep 1
        (( waited++ )) || true
    done

    wait "$pid"
    local status=$?
    cat "$out"
    rm -f "$out"
    return $status
}

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

    output=$(run_suite "$scene")
    status=$?
    summary=$(grep -E "[0-9]+ passed, [0-9]+ failed" <<<"$output" | tail -1)

    if [[ $status -eq 124 ]]; then
        echo "TIMEOUT  ${name} (killed after ${SUITE_TIMEOUT}s - threw before Quit()?)"
        grep -E "^FAIL |SCRIPT ERROR|Unhandled exception|Node not found" <<<"$output" | head -5 | sed 's/^/           /'
        failed+=("$name")
        continue
    fi

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
