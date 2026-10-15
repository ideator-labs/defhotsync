using Microsoft.Extensions.DependencyInjection;

namespace DefHotSync;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDefHotSync(this IServiceCollection services, string storeFolder)
    {
        services.AddWorkflowDSL();
        services.AddSingleton<IDefinitionStore>(_ => new FileDefinitionStore(storeFolder));
        services.AddSingleton<DefHotSyncStarter>();
        return services;
    }
}
