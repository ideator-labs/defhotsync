using WorkflowCore.Interface;
using WorkflowCore.Services.DefinitionStorage;

namespace DefHotSync;

public sealed class DefHotSyncStarter
{
    private readonly IWorkflowHost _host;
    private readonly IWorkflowRegistry _registry;
    private readonly IDefinitionLoader _loader;
    private readonly IDefinitionStore _store;

    public DefHotSyncStarter(
        IWorkflowHost host,
        IWorkflowRegistry registry,
        IDefinitionLoader loader,
        IDefinitionStore store)
    {
        _host = host;
        _registry = registry;
        _loader = loader;
        _store = store;
    }

    /// <summary>
    /// Start a workflow by id. If this host has no matching registration, load the
    /// JSON/YAML body from the shared definition store, register it via IDefinitionLoader,
    /// then start. Store miss fails with a clear message.
    /// </summary>
    public async Task<HotStartResult> StartAsync(string workflowId, int? version = null, object? data = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        var existing = _registry.GetDefinition(workflowId, version);
        var loadedFromStore = false;

        if (existing is null)
        {
            var record = _store.TryGet(workflowId, version)
                ?? throw new InvalidOperationException(
                    $"Definition store miss: {workflowId}@{(version?.ToString() ?? "any")}");

            var deserializer = record.Format == "yaml" ? Deserializers.Yaml : Deserializers.Json;
            _loader.LoadDefinition(record.Body, deserializer);
            loadedFromStore = true;
        }

        var instanceId = version is int v
            ? await _host.StartWorkflow(workflowId, v, data)
            : await _host.StartWorkflow(workflowId, data);

        return new HotStartResult(instanceId, loadedFromStore);
    }
}

public sealed record HotStartResult(string InstanceId, bool LoadedFromStore);
