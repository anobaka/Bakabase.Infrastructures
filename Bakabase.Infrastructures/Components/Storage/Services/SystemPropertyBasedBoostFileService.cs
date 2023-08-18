using System;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Storage.Abstractions;
using Bootstrap.Components.Configuration.SystemProperty.Services;

namespace Bakabase.Infrastructures.Components.Storage.Services
{
    [Obsolete]
    public abstract class SystemPropertyBasedBoostFileService : IBoostFileService
    {
        private readonly SystemPropertyService _systemPropertyService;

        protected SystemPropertyBasedBoostFileService(SystemPropertyService systemPropertyService)
        {
            _systemPropertyService = systemPropertyService;
        }

        protected abstract string SystemPropertyKey { get; }

        public async Task<string> GetRoot()
        {
            return (await _systemPropertyService.GetByKey(SystemPropertyKey, false))?.Value;
        }
    }
}