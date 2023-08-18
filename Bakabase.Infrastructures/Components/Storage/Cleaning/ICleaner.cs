using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.Storage.Cleaning
{
    public interface ICleaner
    {
        Task Clean();
    }
}
