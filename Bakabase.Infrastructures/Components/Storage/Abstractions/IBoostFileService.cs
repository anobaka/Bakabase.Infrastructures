using System;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.Storage.Abstractions
{
    [Obsolete]
    public interface IBoostFileService
    {
        Task<string> GetRoot();
    }
}
