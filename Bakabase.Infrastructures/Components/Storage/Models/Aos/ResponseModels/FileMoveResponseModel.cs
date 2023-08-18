using System;
using System.Collections.Generic;

namespace Bakabase.Infrastructures.Components.Storage.Models.Aos.ResponseModels
{
    [Obsolete]
    public class FileMoveResponseModel
    {
        public int TotalCount { get; set; }
        public int FileExistedCount { get; set; }
        public int SuccessfulCount => TotalCount - FailedCount;
        public int FailedCount => FailedMoveActions?.Count ?? 0;
        public Dictionary<string, string> FailedMoveActions { get; set; }
    }
}