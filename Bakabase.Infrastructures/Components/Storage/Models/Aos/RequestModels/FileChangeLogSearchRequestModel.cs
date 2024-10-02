using Bootstrap.Models.RequestModels;
using System;

namespace Bakabase.Infrastructures.Components.Storage.Models.Aos.RequestModels
{
    [Obsolete]
    public record FileChangeLogSearchRequestModel: SearchRequestModel
    {
        public string Keyword { get; set; }
    }
}
