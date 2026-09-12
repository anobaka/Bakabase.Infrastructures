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
            await db.Database.ExecuteSqlRawAsync($"PRAGMA encoding = 'UTF-16';PRAGMA page_size = {65536};");
            // This two pragmas below are persistent, and cache_size is working with current connection.
            await db.Database.ExecuteSqlRawAsync($"PRAGMA journal_mode = WAL;");
            await db.Database.ExecuteSqlRawAsync("PRAGMA auto_vacuum = INCREMENTAL");
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)");
            await db.Database.ExecuteSqlRawAsync("VACUUM");
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