using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Infrastructures.Components.Orm
{
    public static class SqliteExtensions
    {
        public const int MainPageCacheBudgetKiB = 32 * 1024;
        public const int AuxiliaryPageCacheBudgetKiB = 8 * 1024;

        /// <summary>
        /// Applies a per-connection page-cache budget. Negative cache_size values are KiB,
        /// whereas positive values are page counts and scale with the database page size.
        /// Reapply for each context because a pooled connection can retain an older setting.
        /// </summary>
        public static void SetSqlitePageCacheBudget(this DbContext context, int budgetKiB)
        {
            if (budgetKiB <= 0)
                throw new ArgumentOutOfRangeException(nameof(budgetKiB), "The page-cache budget must be positive.");

            context.Database.OpenConnection();
            context.Database.ExecuteSqlRaw($"PRAGMA cache_size = {-budgetKiB}");
        }

        public static void UseBootstrapSqLite(this DbContextOptionsBuilder builder, string appDataPath, string filenameWithoutExtension)
        {
            var ds = Path.Combine(appDataPath, $"{Path.GetFileNameWithoutExtension(filenameWithoutExtension)}.db");
            var dir = Path.GetDirectoryName(ds)!;
            Directory.CreateDirectory(dir);
            var connectionStringBuilder = new SqliteConnectionStringBuilder {DataSource = ds };
            var connectionString = connectionStringBuilder.ToString();
            // Use connection string instead of a shared SqliteConnection object.
            // Sharing a single connection across scoped DbContext instances is not thread-safe
            // and causes EF Core 9's migration lock (INSERT OR IGNORE + SELECT changes())
            // to return incorrect results under concurrent access from background services.
            builder.UseSqlite(connectionString, t =>
            {
                t.CommandTimeout(10);
            });
            builder.EnableSensitiveDataLogging();
        }

        public static async Task MigrateSqliteDbContexts<TDbContext>(this IServiceProvider serviceProvider)
            where TDbContext : DbContext
        {
            using var scope = serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            // Required, not optional: this method has nothing to do when the context is
            // absent, and the old GetService turned "nobody registered it" into a bare
            // NullReferenceException from inside a migration step — a stack that names
            // SQLite for a problem that is purely about registration.
            var db = sp.GetRequiredService<TDbContext>();

            await db.Database.OpenConnectionAsync();
            long pageCacheSetting;
            using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "PRAGMA cache_size";
                pageCacheSetting = Convert.ToInt64(await command.ExecuteScalarAsync());
            }
            await db.Database.ExecuteSqlRawAsync($"PRAGMA encoding = 'UTF-16';PRAGMA page_size = {65536};");
            // This two pragmas below are persistent, and cache_size is working with current connection.
            await db.Database.ExecuteSqlRawAsync($"PRAGMA journal_mode = WAL;");
            await db.Database.ExecuteSqlRawAsync("PRAGMA auto_vacuum = INCREMENTAL");
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)");
            await db.Database.ExecuteSqlRawAsync("VACUUM");
            // A page-size change can rescale a negative KiB budget's cached page count.
            // Restore the existing budget before migrations start reading/writing tables.
            if (pageCacheSetting < 0)
                await db.Database.ExecuteSqlRawAsync($"PRAGMA cache_size = {pageCacheSetting}");
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)");
            await db.Database.MigrateAsync();
        }

        public static Task MigrateSqliteDbContexts<TDbContext>(this IApplicationBuilder app)
            where TDbContext : DbContext
        {
            return app.ApplicationServices.MigrateSqliteDbContexts<TDbContext>();
        }
    }
}
