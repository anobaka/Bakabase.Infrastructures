using Bootstrap.Models.RequestModels;
using System;

namespace Bakabase.Infrastructures.Components.Storage.Models.Aos.RequestModels
{
    [Obsolete]
    public class FileChangeLogSearchRequestModel: SearchRequestModel
    {
        public string Keyword { get; set; }
    }
}
