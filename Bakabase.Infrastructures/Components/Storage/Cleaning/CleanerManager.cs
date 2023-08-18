using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.Storage.Cleaning
{
    public class CleanerManager
    {
        private readonly ICleaner[] _cleaners;

        public CleanerManager(IEnumerable<ICleaner> cleaners)
        {
            _cleaners = cleaners.ToArray();
        }

        public async Task Clean()
        {
            foreach (var c in _cleaners)
            {
                await c.Clean();
            }
        }
    }
}