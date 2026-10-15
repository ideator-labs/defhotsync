using System.Diagnostics;
using System.Text.Json;
using DefHotSync;
using DefHotSync.Demo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkflowCore.Interface;
using WorkflowCore.Models;

var storeRoot = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "defhotsync-store-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(storeRoot);

// Publish HotOrder v1 into the shared store only. Neither host registers it up front.
var hotOrderJson = """
{
  "Id": "HotOrder",
  "Version": 1,
  "DataType": "DefHotSync.Demo.HotOrderData, DefHotSync.Demo",
  "Steps": [
    {
      "Id": "mark",
      "StepType": "DefHotSync.Demo.MarkDone, DefHotSync.Demo",
      "Inputs": { "Message": "\"hot-sync-ok\"" },
      "Outputs": { "Result": "step.Message" }
    }
  ]
}
""";
File.WriteAllText(Path.Combine(storeRoot, "HotOrder@1.json"), hotOrderJson);

var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var settle = TimeSpan.FromSeconds(5);

// --- Hot-sync host: AddDefHotSync, no prior RegisterWorkflow for HotOrder ---
var hotServices = new ServiceCollection()
    .AddLogging(l => l.SetMinimumLevel(LogLevel.Warning).AddConsole(c => c.LogToStandardErrorThreshold = LogLevel.Trace))
    .AddWorkflow()
    .AddDefHotSync(storeRoot)
    .BuildServiceProvider();

var hotHost = hotServices.GetRequiredService<IWorkflowHost>();
var hotRegistry = hotServices.GetRequiredService<IWorkflowRegistry>();
var hotStore = hotServices.GetRequiredService<IPersistenceProvider>();
var starter = hotServices.GetRequiredService<DefHotSyncStarter>();
await hotHost.StartAsync(CancellationToken.None);

var registeredBefore = hotRegistry.GetDefinition("HotOrder", 1) is not null;

object hotsyncRecord;
try
{
    var clock = Stopwatch.StartNew();
    var start = await starter.StartAsync("HotOrder", 1, new HotOrderData());
    var instance = await WaitUntil(hotStore, start.InstanceId, i => i.Status == WorkflowStatus.Complete, settle);
    var data = (HotOrderData)instance.Data;
    var registeredAfter = hotRegistry.GetDefinition("HotOrder", 1) is not null;
    hotsyncRecord = new
    {
        scenario = "hotsync_start",
        workflow = "HotOrder",
        registeredBefore,
        loadedFromStore = start.LoadedFromStore,
        registeredAfter,
        status = instance.Status.ToString(),
        result = data.Result,
        error = (string?)null,
        elapsedMs = clock.ElapsedMilliseconds,
    };
}
catch (Exception ex)
{
    hotsyncRecord = new
    {
        scenario = "hotsync_start",
        workflow = "HotOrder",
        registeredBefore,
        loadedFromStore = false,
        registeredAfter = hotRegistry.GetDefinition("HotOrder", 1) is not null,
        status = "Error",
        result = (string?)null,
        error = ex.GetType().Name + ": " + ex.Message,
        elapsedMs = 0L,
    };
}
Console.WriteLine(JsonSerializer.Serialize(hotsyncRecord, jsonOpts));

// --- Store miss on the same hot host ---
object missRecord;
try
{
    await starter.StartAsync("MissingWf", 1, new HotOrderData());
    missRecord = new
    {
        scenario = "store_miss",
        workflow = "MissingWf",
        failed = false,
        error = (string?)null,
    };
}
catch (Exception ex)
{
    missRecord = new
    {
        scenario = "store_miss",
        workflow = "MissingWf",
        failed = true,
        error = ex.Message,
    };
}
Console.WriteLine(JsonSerializer.Serialize(missRecord, jsonOpts));

// --- Control host: plain AddWorkflow, no DefHotSync, no prior register ---
var controlServices = new ServiceCollection()
    .AddLogging(l => l.SetMinimumLevel(LogLevel.Warning).AddConsole(c => c.LogToStandardErrorThreshold = LogLevel.Trace))
    .AddWorkflow()
    .BuildServiceProvider();

var controlHost = controlServices.GetRequiredService<IWorkflowHost>();
var controlRegistry = controlServices.GetRequiredService<IWorkflowRegistry>();
await controlHost.StartAsync(CancellationToken.None);

var controlRegistered = controlRegistry.GetDefinition("HotOrder", 1) is not null;
object controlRecord;
try
{
    await controlHost.StartWorkflow("HotOrder", 1, new HotOrderData());
    controlRecord = new
    {
        scenario = "control_unknown",
        workflow = "HotOrder",
        registered = controlRegistered,
        failed = false,
        error = (string?)null,
    };
}
catch (Exception ex)
{
    controlRecord = new
    {
        scenario = "control_unknown",
        workflow = "HotOrder",
        registered = controlRegistered,
        failed = true,
        error = ex.GetType().Name + ": " + ex.Message,
    };
}
Console.WriteLine(JsonSerializer.Serialize(controlRecord, jsonOpts));

// Second start on the hot host: already registered, should not need the store again.
object secondRecord;
try
{
    // Wipe the store file so a re-load would fail. Already-registered path must still work.
    foreach (var f in Directory.EnumerateFiles(storeRoot, "HotOrder@*"))
        File.Delete(f);

    var start2 = await starter.StartAsync("HotOrder", 1, new HotOrderData());
    var instance2 = await WaitUntil(hotStore, start2.InstanceId, i => i.Status == WorkflowStatus.Complete, settle);
    var data2 = (HotOrderData)instance2.Data;
    secondRecord = new
    {
        scenario = "already_registered",
        workflow = "HotOrder",
        loadedFromStore = start2.LoadedFromStore,
        status = instance2.Status.ToString(),
        result = data2.Result,
        error = (string?)null,
    };
}
catch (Exception ex)
{
    secondRecord = new
    {
        scenario = "already_registered",
        workflow = "HotOrder",
        loadedFromStore = false,
        status = "Error",
        result = (string?)null,
        error = ex.GetType().Name + ": " + ex.Message,
    };
}
Console.WriteLine(JsonSerializer.Serialize(secondRecord, jsonOpts));

await hotHost.StopAsync(CancellationToken.None);
await controlHost.StopAsync(CancellationToken.None);

static async Task<WorkflowInstance> WaitUntil(
    IPersistenceProvider store,
    string id,
    Func<WorkflowInstance, bool> done,
    TimeSpan timeout)
{
    var clock = Stopwatch.StartNew();
    while (true)
    {
        var instance = await store.GetWorkflowInstance(id);
        if (done(instance) || clock.Elapsed > timeout)
            return instance;
        await Task.Delay(50);
    }
}
