#!/usr/bin/env bash
#
# Builds packaged exports and proves the result actually boots.
#
#   tools/build-export.sh                 # all three presets
#   tools/build-export.sh Linux           # just this one
#
# The arguments are preset *names* from export_presets.cfg and are passed to
# Godot verbatim - --export-release takes a preset name, not a platform.
#
# Output goes to build/<platform>/. Each build is a directory, not a file: with
# dotnet/embed_build_outputs off, the .NET assemblies live in a sibling
# data_Hollowdeck_<platform>_<arch>/ folder that the executable loads at
# startup. Ship the folder, not the binary.
#
# This is the CI entry point for the export job, the same way
# run-smoke-tests.sh is for the test job - so CI and the developer run
# identical code rather than CI reimplementing the checks.

set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 1

GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if [[ ! -x "$GODOT" ]]; then
    echo "error: Godot not found at '$GODOT'. Set GODOT=/path/to/godot." >&2
    exit 1
fi

# Godot runs `dotnet publish` itself during export, so this is not strictly
# required - but CLAUDE.md's rule is build first, and a compile error caught
# here is a far shorter loop than the same error surfacing halfway through a
# universal macOS export.
echo "== dotnet build =="
if ! dotnet build -v q --nologo; then
    echo "error: build failed; not exporting." >&2
    exit 1
fi

# Same reason CI imports before running anything: .godot/ is gitignored, so a
# fresh checkout resolves no resources at all. --export-release would wait for
# the scan on its own; doing it here means import errors appear as themselves
# instead of buried in export output.
echo
echo "== import =="
"$GODOT" --headless --path . --import

# Keep the three presets' content filter in step. CI boots only the *Linux*
# build, so nothing downstream can see a Windows or macOS preset whose filter has
# drifted; this grep can, for nothing. It is a consistency check rather than a
# correctness one - see the comment on include_filter in export_presets.cfg for
# why that filter is insurance rather than the thing that packs the JSONs - so
# the message says "differ", not "broken".
presets_total=$(grep -c '^\[preset\.[0-9]*\]$' export_presets.cfg)
filters_ok=$(grep -c '^include_filter="data/\*\.json"$' export_presets.cfg)
if [[ "$filters_ok" -ne "$presets_total" ]]; then
    echo "error: only ${filters_ok} of ${presets_total} presets carry include_filter=\"data/*.json\"." >&2
    echo "       The presets disagree about what ships under data/; make them match" >&2
    echo "       or update this check if the divergence is deliberate." >&2
    exit 1
fi

if [[ $# -gt 0 ]]; then
    presets=("$@")
else
    presets=("Linux" "Windows Desktop" "macOS")
fi

# Preset name -> output directory. export_path in export_presets.cfg is what
# Godot actually writes to and stays the source of truth; this only says which
# directory to clear beforehand and where to drop CREDITS.md. If the two ever
# disagree the build lands somewhere else and the boot check below fails on a
# missing binary - loud, which is why three duplicated lines are acceptable.
#
# A case rather than an associative array: macOS ships bash 3.2, which predates
# `declare -A` and silently mangles it into a plain array with arithmetic
# subscripts ("Linux: unbound variable"). Same reason this script rolls its own
# watchdog instead of using timeout(1).
out_dir_for() {
    case "$1" in
        "Linux")           echo "build/linux" ;;
        "Windows Desktop") echo "build/windows" ;;
        "macOS")           echo "build/macos" ;;
    esac
}

# The check that actually catches a mis-packed data/ directory - and the reason
# it greps rather than trusting $?.
#
# An unhandled C# exception does NOT move Godot's exit code. The .NET layer
# catches it, prints it through GD.PushError as "ERROR: Unhandled exception",
# and the main loop carries on. A build whose CardDatabase threw on the first
# frame still exits 0. The exit code catches a hard engine abort and nothing
# else; the log is the gate.
#
# --quit-after is available in export templates, not just the editor. 60 frames
# is about a second - enough for the four autoloads' _Ready, the main scene's
# _Ready, and anything they deferred.
#
# Booting the real binary reads user://settings.json and
# user://meta_progression.json and checks whether a run save exists. All three
# are reads - SettingsManager and MetaProgressionManager only write from
# setters, and MainMenu only calls SaveExists() - so unlike the smoke suites
# this needs no RunSaveGuard. If that ever stops being true, this script starts
# silently eating the developer's in-progress run on every export.
BOOT_TIMEOUT="${BOOT_TIMEOUT:-90}"

boot_check() {
    local binary="$1"
    local out status waited=0 pid errors
    out=$(mktemp)

    "$binary" --headless --quit-after 60 >"$out" 2>&1 &
    pid=$!

    # By hand rather than with timeout(1), which is not installed on macOS -
    # same reason run-smoke-tests.sh rolls its own.
    while kill -0 "$pid" 2>/dev/null; do
        if (( waited >= BOOT_TIMEOUT )); then
            kill -9 "$pid" 2>/dev/null
            wait "$pid" 2>/dev/null
            echo "TIMEOUT  boot check (killed after ${BOOT_TIMEOUT}s - hung before quitting?)"
            tail -20 "$out" | sed 's/^/           /'
            rm -f "$out"
            return 1
        fi
        sleep 1
        (( waited++ )) || true
    done
    wait "$pid"; status=$?

    errors=$(grep -E '^(ERROR|SCRIPT ERROR|SHADER ERROR):|Unhandled exception' "$out")

    if [[ $status -ne 0 || -n "$errors" ]]; then
        echo "FAIL     boot check (exit ${status})"
        [[ -n "$errors" ]] && head -10 <<<"$errors" | sed 's/^/           /'
        rm -f "$out"
        return 1
    fi

    rm -f "$out"
    echo "ok       booted clean and quit"
    return 0
}

# Where the runnable executable lands, per preset, and which host can run it.
# A Linux build cannot be booted on macOS or vice versa, so the boot check is
# skipped rather than faked when there is no match.
bootable_binary() {
    case "$1" in
        Linux) [[ "$HOST" == linux ]] && echo "build/linux/Hollowdeck.x86_64" ;;
        macOS) [[ "$HOST" == darwin ]] && echo "build/macos/unpacked/Hollowdeck.app/Contents/MacOS/Hollowdeck" ;;
    esac
}

HOST="$(uname -s | tr '[:upper:]' '[:lower:]')"
failed=()

for preset in "${presets[@]}"; do
    out_dir="$(out_dir_for "$preset")"
    if [[ -z "$out_dir" ]]; then
        echo "error: unknown preset '${preset}' (expected: Linux, Windows Desktop, macOS)" >&2
        exit 1
    fi

    echo
    echo "== export: ${preset} =="
    rm -rf "$out_dir"
    mkdir -p "$out_dir"

    # No output path argument: export_path in export_presets.cfg is the single
    # source of truth for where each build lands. A missing export template is
    # the usual failure here, and Godot reports it as a configuration error and
    # exits nonzero. Note that "completed with warnings" still exits 0, which is
    # another reason the boot check below exists.
    if ! "$GODOT" --headless --path . --export-release "$preset"; then
        echo "FAIL     ${preset} (export failed)"
        failed+=("$preset")
        continue
    fi

    # README.md calls CREDITS.md a file that must ship with any build. Until
    # there is an in-game credits screen, copying it beside the binary is the
    # only thing that makes that sentence true. There is no LICENSE file in this
    # repo yet; when there is, copy it here too - picking a licence is a
    # decision, not something a build script should invent.
    cp CREDITS.md "$out_dir/"

    # The macOS preset emits a .zip (see export_presets.cfg), so unpack it to
    # get at something runnable. Kept out of the OUT_DIR map because it is the
    # one preset whose export_path is an archive rather than an executable.
    if [[ "$preset" == "macOS" && -f build/macos/Hollowdeck.zip ]]; then
        unzip -q -o build/macos/Hollowdeck.zip -d build/macos/unpacked
    fi

    binary="$(bootable_binary "$preset")"
    if [[ -z "$binary" ]]; then
        echo "skipped  boot check (a ${preset} build cannot run on ${HOST})"
    elif [[ ! -x "$binary" ]]; then
        echo "FAIL     ${preset} (expected a runnable binary at ${binary})"
        failed+=("$preset")
    else
        boot_check "$binary" || failed+=("$preset")
    fi
done

echo
if [[ ${#failed[@]} -gt 0 ]]; then
    echo "${#failed[@]} of ${#presets[@]} presets failed: ${failed[*]}"
    exit 1
fi
echo "all ${#presets[@]} presets exported"
