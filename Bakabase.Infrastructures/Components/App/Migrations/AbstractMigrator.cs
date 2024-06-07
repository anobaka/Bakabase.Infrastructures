using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Semver;

namespace Bakabase.Infrastructures.Components.App.Migrations
{
    public abstract class AbstractMigrator : IMigrator
    {
        private readonly IServiceProvider _serviceProvider;
        private IServiceProvider _scopedServiceProvider;
        protected TService GetRequiredService<TService>() => _scopedServiceProvider.GetRequiredService<TService>();
        private object _context;
        protected ILogger Logger { get; }

        protected AbstractMigrator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            Logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        }

        /// <summary>
        /// <inheritdoc cref="ApplyOnVersionEqualsOrBefore"/>
        /// </summary>
        protected abstract string ApplyOnVersionEqualsOrBeforeString { get; }

        public SemVersion ApplyOnVersionEqualsOrBefore =>
            SemVersion.Parse(ApplyOnVersionEqualsOrBeforeString, SemVersionStyles.Any);

        protected virtual Task<object> MigrateBeforeDbMigrationInternal()
        {
            return Task.FromResult<object>(null);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Context using in <see cref="MigrateAfterDbMigrationInternal"/></returns>
        public async Task MigrateBeforeDbMigration()
        {
            Logger.LogInformation("Migrating before db migration.");
            await using var scope = _serviceProvider.CreateAsyncScope();
            _scopedServiceProvider = scope.ServiceProvider;
            _context = await MigrateBeforeDbMigrationInternal();
            Logger.LogInformation("Migration is done");
        }


        protected virtual Task MigrateAfterDbMigrationInternal(object context)
        {
            return Task.CompletedTask;
        }

        public async Task MigrateAfterDbMigration()
        {
            Logger.LogInformation("Migrating after db migration.");
            await using var scope = _serviceProvider.CreateAsyncScope();
            _scopedServiceProvider = scope.ServiceProvider;
            await MigrateAfterDbMigrationInternal(_context);
            Logger.LogInformation("Migration is done");
        }
    }
}