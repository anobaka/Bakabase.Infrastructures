using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddUpdater(this IServiceCollection services)
        {
            return services
                .AddSingleton<AppUpdater>();
        }
    }
}
