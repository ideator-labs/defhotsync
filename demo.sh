#!/usr/bin/env bash
# Proves: a Workflow Core host without HotOrder registered can still start it via DefHotSync
# (load JSON from a shared file store, IDefinitionLoader, register, run to Complete). A control
# host without DefHotSync fails the same start with an unknown-definition error.
#   bash demo.sh          all checks must PASS, exit 0
#   bash demo.sh --break  LoadDefinition skipped, hotsync check must FAIL, exit 1
set -euo pipefail
cd "$(dirname "$0")"
[[ -d "$HOME/.dotnet" ]] && export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
command -v jq >/dev/null || { echo "jq is required"; exit 2; }

OUT=.demo
SRC=src
rm -rf "$OUT" && mkdir -p "$OUT"

sabotage() {  # sabotage <file> <exact text to remove>
  grep -qF -- "$2" "$1" || { echo "sabotage target not found in $1"; exit 2; }
  F="$1" T="$2" python3 -c 'import os; p=os.environ["F"]; s=open(p).read(); open(p,"w").write(s.replace(os.environ["T"], "", 1))'
}

copy_src() { cp -r src "$OUT/src" && rm -rf "$OUT"/src/*/bin "$OUT"/src/*/obj && SRC="$OUT/src"; }

case "${1:-}" in
  "") ;;
  --break)
    copy_src
    sabotage "$SRC/DefHotSync/DefHotSyncStarter.cs" "_loader.LoadDefinition(record.Body, deserializer);"
    echo "BREAK MODE: LoadDefinition skipped, so HotOrder never registers. Expect a FAIL." ;;
  *) echo "usage: bash demo.sh [--break]"; exit 2 ;;
esac

FAILED=0
pass() { echo "CHECK PASS  $1"; }
fail() { echo "CHECK FAIL  $1"; FAILED=1; }

if dotnet build "$SRC/DefHotSync.Demo" -c Release -o "$OUT/bin" -v quiet -nologo > "$OUT/build.log" 2>&1; then
  pass "1 build: DefHotSync + demo compile against WorkflowCore 3.21.0 and WorkflowCore.DSL"
else
  cat "$OUT/build.log"; fail "1 build"; echo "RESULT: FAIL"; exit 1
fi

STORE="$OUT/store"
mkdir -p "$STORE"
dotnet "$OUT/bin/DefHotSync.Demo.dll" "$STORE" > "$OUT/results.jsonl" 2> "$OUT/host.log"

check() {  # check <label> <scenario> <jq filter on that scenario's record that must print true>
  if [[ "$(jq -s "[.[] | select(.scenario == \"$2\")] | length == 1 and (.[0] | $3)" "$OUT/results.jsonl")" == "true" ]]; then pass "$1"; else fail "$1"; fi
}

check "2 hotsync: HotOrder was not registered before start" hotsync_start \
  '.registeredBefore == false'
check "3 hotsync: DefHotSync loaded HotOrder from the shared store" hotsync_start \
  '.loadedFromStore == true'
check "4 hotsync: HotOrder registered on this host after load" hotsync_start \
  '.registeredAfter == true'
check "5 hotsync: workflow ran to Complete with Result=hot-sync-ok" hotsync_start \
  '.status == "Complete" and .result == "hot-sync-ok" and .error == null'
check "6 store_miss: missing id fails with a clear store-miss message" store_miss \
  '.failed == true and (.error | test("store miss"; "i"))'
check "7 control: host without DefHotSync has HotOrder unregistered" control_unknown \
  '.registered == false'
check "8 control: StartWorkflow(HotOrder) fails with unknown-definition error" control_unknown \
  '.failed == true and .error != null'
check "9 already_registered: second start skips store (store wiped) and still Completes" already_registered \
  '.loadedFromStore == false and .status == "Complete" and .result == "hot-sync-ok" and .error == null'

echo "--- results ---"
jq -c '{scenario, status, registeredBefore, loadedFromStore, registeredAfter, registered, failed, result, error}' "$OUT/results.jsonl"

if [[ $FAILED -ne 0 ]]; then echo "RESULT: FAIL (raw output in $OUT/)"; exit 1; fi
echo "RESULT: PASS"
