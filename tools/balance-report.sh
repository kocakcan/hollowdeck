#!/usr/bin/env bash
#
# Prints the three-act balance curve - see scripts/debug/BalanceReport.cs.
#
#   tools/balance-report.sh
#
# This exists rather than going through run-smoke-tests.sh because that script
# captures each suite's stdout and echoes only the "N passed, M failed" line,
# which would swallow the entire report. The pass/fail half of this work is
# BalanceSmokeTest, which does run in the sweep.

set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 1

GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if [[ ! -x "$GODOT" ]]; then
    echo "error: Godot not found at '$GODOT'. Set GODOT=/path/to/godot." >&2
    exit 1
fi

# C# is compiled ahead of time - without this the report describes the
# previous build's model, which is the one failure mode a balance number must
# not have.
if ! dotnet build -v q --nologo; then
    echo "error: build failed; not running the report." >&2
    exit 1
fi

exec "$GODOT" --headless --path . scenes/debug/BalanceReport.tscn
