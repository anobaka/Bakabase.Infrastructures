using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;

namespace Bakabase.Infrastructures.Components.SystemService
{
    public interface ISystemService
    {
        UiTheme UiTheme { get; }
        string Language { get; }
    }
}
