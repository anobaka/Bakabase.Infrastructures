using System;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Infrastructures.Components.Storage.Extensions
{
    [Obsolete]
    public static class StorageServiceCollectionExtensions
    {
        public static IServiceCollection AddBootstrapStorageService<TDbContextImplementation>(
            this IServiceCollection services, Action<DbContextOptionsBuilder> configure = null)
            where TDbContextImplementation : StorageDbContext
        {
            return services.AddServiceBootstrapServices<StorageDbContext, TDbContextImplementation>(
                SpecificTypeUtils<FileService>.Type, configure);
        }
    }
}