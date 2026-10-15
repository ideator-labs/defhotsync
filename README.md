# defhotsync

Start a Workflow Core workflow this host has never seen. DefHotSync looks up the JSON (or YAML) definition in a shared file store, loads it with `IDefinitionLoader`, registers it on this host's `IWorkflowRegistry`, and starts it. A control host without DefHotSync fails the same start with an unknown-definition error.

```csharp
// publish HotOrder@1.json into the shared folder (not RegisterWorkflow)
services.AddWorkflow().AddDefHotSync(storeFolder);
var starter = sp.GetRequiredService<DefHotSyncStarter>();
await starter.StartAsync("HotOrder", 1, data); // loads, registers, starts
```

This is the multi-instance registration gap from Workflow Core issue [#1386](https://github.com/danielgerlag/workflow-core/issues/1386): new definitions live outside the process, and a host that has not pulled yet should not hang on "unknown workflow". Built on real `WorkflowCore` 3.21.0 and `WorkflowCore.DSL` 3.21.0.

```bash
bash demo.sh           # 9 checks, RESULT: PASS
bash demo.sh --break   # LoadDefinition skipped, checks fail, exit 1
```

Needs the .NET 8 SDK and `jq`. In-memory persistence only. See [AGENTS.md](AGENTS.md) for the data shape, what is real, the limits, and next steps.
