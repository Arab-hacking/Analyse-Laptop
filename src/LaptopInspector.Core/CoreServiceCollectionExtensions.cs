using LaptopInspector.Core.Rules;
using LaptopInspector.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LaptopInspector.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddLaptopInspectorCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IPrivilegeDetector, DefaultPrivilegeDetector>();
        services.TryAddSingleton<CollectionScheduler>();
        services.TryAddSingleton<HealthRulesEngine>();
        return services;
    }
}
