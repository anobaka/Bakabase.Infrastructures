using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.App
{
    public record AppContext
    {
        public string[] ServerAddresses { get; set; }
    }
}
