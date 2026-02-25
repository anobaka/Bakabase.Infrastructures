using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Infrastructures.Components.Logging
{
    [Obsolete]
    public class SqliteLogService : Bootstrap.Components.Logging.LogService.Services.LogService
    {
        public SqliteLogService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public override async Task Truncate()
        {
            using var scope = CreateNewScope();
            await DbContext.Database.ExecuteSqlRawAsync($"delete from {nameof(DbContext.Logs)}");
        }

        public override async Task DeleteBefore(DateTime dateTime)
        {
            using var scope = CreateNewScope();
            await DbContext.Database.ExecuteSqlRawAsync(
                $"delete from {nameof(DbContext.Logs)} where {nameof(Bootstrap.Components.Logging.LogService.Models.Entities.Log.DateTime)} < @p0",
                dateTime);
        }
    }
}