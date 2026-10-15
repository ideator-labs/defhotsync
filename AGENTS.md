# AGENTS.md

Handoff for a coding agent with no prior context on this repo.

## What this proves

One claim: a Workflow Core host that does not yet have workflow definition `HotOrder` registered can still start it. DefHotSync looks up the JSON definition in a shared file store, loads it via `IDefinitionLoader`, registers it on that host's `IWorkflowRegistry`, and the workflow runs to Complete. A control host without DefHotSync fails the same start with an unknown-definition error.

It runs on the real `WorkflowCore` 3.21.0 and `WorkflowCore.DSL` 3.21.0 NuGet packages, in one .NET 8 process, with the in-memory store. No paid APIs, no network after the NuGet restore.

Why it matters. Issue #1386 asks how to register new workflows that live outside the library when hosts are multi-instance. Polling every N minutes leaves a window where some instances error on unknown workflow. DefHotSync is the on-demand hook: miss on the local registry, load from a shared store, register, then start.

## Status

| Piece | Real or stubbed |
|---|---|
| Workflow host, in-memory persistence, `IWorkflowRegistry`, `StartWorkflow` | Real. NuGet `WorkflowCore` 3.21.0. |
| `IDefinitionLoader.LoadDefinition` + JSON deserializer | Real. NuGet `WorkflowCore.DSL` 3.21.0. |
| `FileDefinitionStore`, `DefHotSyncStarter`, `AddDefHotSync` | Real, in `src/DefHotSync`. About 120 lines. |
| `HotOrder` definition body | Toy JSON. One `MarkDone` step. Written into the store by the demo, not pre-registered. |
| Cross-process / multi-host shared store | Same folder on one machine. Not a real distributed store. |
| Push / watch / event when a definition is published | Not shown. Pull on start miss only. |

`bash demo.sh` passes 9 of 9 checks on a fresh clone. `bash demo.sh --break` fails checks 3 to 5 (and usually 9) and exits 1.

## Data shape

```
DefinitionRecord
  Id       string
  Version  int
  Format   "json" | "yaml"
  Body     string                 raw definition text

IDefinitionStore.TryGet(id, version?) -> DefinitionRecord?
  null on miss

FileDefinitionStore(folder)
  files named {Id}@{Version}.json|.yaml|.yml
  example: HotOrder@1.json
  version omitted -> highest version for that id

DefHotSyncStarter.StartAsync(workflowId, version?, data?)
  -> HotStartResult { InstanceId, LoadedFromStore }
  if registry has the definition: StartWorkflow only (LoadedFromStore=false)
  if registry miss: TryGet from store, LoadDefinition (registers), StartWorkflow
  if store miss: throw with message containing "store miss"
```

One demo output record per scenario (`.demo/results.jsonl`):

```json
{"scenario":"hotsync_start","workflow":"HotOrder","registeredBefore":false,
 "loadedFromStore":true,"registeredAfter":true,"status":"Complete",
 "result":"hot-sync-ok","error":null,"elapsedMs":42}
{"scenario":"store_miss","workflow":"MissingWf","failed":true,"error":"Definition store miss: MissingWf@1"}
{"scenario":"control_unknown","workflow":"HotOrder","registered":false,"failed":true,"error":"..."}
{"scenario":"already_registered","workflow":"HotOrder","loadedFromStore":false,
 "status":"Complete","result":"hot-sync-ok","error":null}
```

## Repo map

```
demo.sh                              the proof. Builds, runs four scenarios, prints CHECK lines
src/DefHotSync/
  DefinitionRecord.cs                Id, Version, Format, Body
  IDefinitionStore.cs                TryGet
  FileDefinitionStore.cs             folder of {Id}@{Version}.{json|yaml}
  DefHotSyncStarter.cs               registry miss -> store -> LoadDefinition -> StartWorkflow
  ServiceCollectionExtensions.cs     AddDefHotSync(storeFolder)
src/DefHotSync.Demo/
  MarkDone.cs                        one StepBody used by the HotOrder JSON
  HotOrderData.cs                    Result string written by the DSL Outputs binding
  Program.cs                         publishes HotOrder@1.json into the store, runs hot + control hosts
```

## Setup, run, verify (fresh clone)

Prereqs: .NET 8 SDK (`dotnet --list-sdks` shows 8.x), `jq`, and `python3` (break mode only). If the SDK lives in `~/.dotnet`, `demo.sh` puts it on PATH. The first build needs NuGet access.

```bash
git clone https://github.com/ideator-labs/defhotsync.git
cd defhotsync
bash demo.sh
```

Expected: nine `CHECK PASS` lines, a results table, then `RESULT: PASS`, exit 0. It takes a few seconds after the first restore.

```
CHECK PASS  1 build: DefHotSync + demo compile against WorkflowCore 3.21.0 and WorkflowCore.DSL
CHECK PASS  2 hotsync: HotOrder was not registered before start
CHECK PASS  3 hotsync: DefHotSync loaded HotOrder from the shared store
CHECK PASS  4 hotsync: HotOrder registered on this host after load
CHECK PASS  5 hotsync: workflow ran to Complete with Result=hot-sync-ok
CHECK PASS  6 store_miss: missing id fails with a clear store-miss message
CHECK PASS  7 control: host without DefHotSync has HotOrder unregistered
CHECK PASS  8 control: StartWorkflow(HotOrder) fails with unknown-definition error
CHECK PASS  9 already_registered: second start skips store (store wiped) and still Completes
RESULT: PASS
```

Break it on purpose:

```bash
bash demo.sh --break
```

Copies `src/` to `.demo/src/`, deletes the exact `LoadDefinition` call from the copy, then builds and runs the copy. The script exits 2 if the string is not found, so a sabotage can never silently do nothing. The real `src/` is never touched, so `bash demo.sh` passes again right after.

With LoadDefinition removed, HotOrder never lands on the registry. The hotsync start fails, checks 3 to 5 fail, and the script exits 1.

Raw output lands in `.demo/` (`results.jsonl`, `host.log`, `build.log`).

## Design decisions

- **On-demand load over poll.** Issue #1386's 10-minute pull leaves a miss window. Start-time lookup closes that window for the start path. Background sync can still warm the registry; this POC only proves the miss path.
- **File store as the shared source of truth.** One folder of `{Id}@{Version}.json` files is enough to show a store that is not the in-process registry. Swap `IDefinitionStore` for blob/SQL later without changing the starter.
- **Real DSL loader, not a hand parser.** `IDefinitionLoader.LoadDefinition` already registers on `IWorkflowRegistry`. DefHotSync does not reimplement registration.
- **Control host in the same run.** The control host is plain `AddWorkflow` with no DefHotSync and no prior register. Check 8 measures the unknown-definition failure, so the claim is not just "hot path works".
- **Already-registered path after wiping the store.** Check 9 deletes `HotOrder@*` from the folder after the first load. The second start must still Complete with `LoadedFromStore=false`. That proves we do not re-hit the store when the registry already has the definition.
- **Toy step, real host.** `MarkDone` exists so the JSON has a resolvable `StepType` in this assembly. The claim is about registration, not about step logic.

## Limits

- In-memory persistence only. No host restart with a durable store.
- Single-process demo. The "shared" store is a local folder both logical hosts can see. No blob, etcd, or DB backend.
- No watch or push. A definition published after the host starts is only seen on the next miss path for that id.
- YAML is accepted by `FileDefinitionStore` and routed to `Deserializers.Yaml`. The demo fixture is JSON only.
- `Type.GetType` must resolve step and data types. Definitions that reference assemblies not loaded in the process will fail at LoadDefinition.
- Concurrent LoadDefinition of the same id/version from many threads is not tested. The registry rejects duplicate id/version registrations.
- No version negotiation beyond "exact version or highest". No rollback or canary.
- Control error text comes from Workflow Core. Check 8 only requires a non-null error and `failed=true`, not a specific exception type name.

## Next steps (in priority order)

1. **Durable shared store.** Implement `IDefinitionStore` on SQLite or a blob container. Done when two separate processes share one store path/URI, process A writes `HotOrder@2.json`, process B's DefHotSyncStarter starts v2 to Complete with no prior register.
2. **Background warm.** On a timer or file watch, pre-register new definitions so the first start does not pay LoadDefinition latency. Done when a demo check shows `LoadedFromStore=false` on the first start after a publish, because the warmer already registered it.
3. **Unknown-definition hook.** If Workflow Core gains a "definition missing" callback (the ask in #1386), wire DefHotSync there so `IWorkflowHost.StartWorkflow` itself triggers the load. Done when the demo can call `host.StartWorkflow` directly (no DefHotSyncStarter) and still Complete after a store publish.
4. **Idempotent re-register.** Detect body hash changes for the same id@version and either refuse or replace. Done when publishing a changed body for `HotOrder@1` either updates the registry or fails with a clear conflict error, covered by a CHECK and a `--break`.
5. **Package it.** Ship `src/DefHotSync` as a NuGet or propose the pattern upstream against #1386. Done when a fresh console app that references only the package passes checks 2 to 8.

## Working rules

- `bash demo.sh` is the source of truth. Keep it passing and keep `--break` failing.
- Every new claim gets a CHECK line that reads real host/registry/store state, and a sabotage that makes it fail.
- Keep stdout of the demo app to one JSON line per scenario. Logs go to stderr.
- Keep `WorkflowCore` and `WorkflowCore.DSL` at 3.21.x unless a step needs a newer API.
- Commit as `danielgerlag <daniel@gerlag.ca>`.
- Plain prose. No em-dashes.

## Sources

- Workflow Core: https://github.com/danielgerlag/workflow-core
- WorkflowCore 3.21.0 on NuGet: https://www.nuget.org/packages/WorkflowCore/3.21.0
- WorkflowCore.DSL 3.21.0 on NuGet: https://www.nuget.org/packages/WorkflowCore.DSL/3.21.0
- Issue #1386, Question about new workflows registration: https://github.com/danielgerlag/workflow-core/issues/1386
- JSON/YAML DSL docs: https://workflow-core.readthedocs.io/en/latest/json-yaml/
- `IDefinitionLoader.LoadDefinition` registers via `IWorkflowRegistry.RegisterWorkflow` (source: `src/WorkflowCore.DSL/Services/DefinitionLoader.cs`)
