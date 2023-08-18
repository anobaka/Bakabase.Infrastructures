using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
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
            var connectionStringBuilder = new SqliteConnectionStringBuilder {DataSource = ds};
            var connectionString = connectionStringBuilder.ToString();
            var conn = new SqliteConnection(connectionString);
            builder.UseSqlite(conn, t => { });
            builder.EnableSensitiveDataLogging();
        }

        public static async Task MigrateSqliteDbContexts<TDbContext>(this IServiceProvider serviceProvider)
            where TDbContext : DbContext
        {
            using var scope = serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetService<TDbContext>();

            await db.Database.OpenConnectionAsync();
            // This two pragmas below are persistent, and cache_size is working with current connection.
            await db.Database.ExecuteSqlRawAsync($"PRAGMA encoding = 'UTF-16';PRAGMA page_size = {65536};");
            await db.Database.MigrateAsync();
        }

        public static Task MigrateSqliteDbContexts<TDbContext>(this IApplicationBuilder app)
            where TDbContext : DbContext
        {
            return app.ApplicationServices.MigrateSqliteDbContexts<TDbContext>();
        }
    }
}