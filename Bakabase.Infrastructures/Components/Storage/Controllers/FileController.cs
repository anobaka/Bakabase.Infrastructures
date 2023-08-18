using System;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Storage.Cleaning;
using Bakabase.Infrastructures.Components.Storage.Models.Aos.RequestModels;
using Bakabase.Infrastructures.Components.Storage.Models.Entities;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Infrastructures.Components.Storage.Controllers
{
    [Route("~/file")]
    [Obsolete]
    public abstract class FileController : Controller
    {
        private readonly FileService _fileService;
        private readonly CleanerManager _cleanerManager;

        protected FileController(FileService fileService, CleanerManager cleanerManager)
        {
            _fileService = fileService;
            _cleanerManager = cleanerManager;
        }

        [HttpGet("change-log")]
        [SwaggerOperation(OperationId = "SearchFileChangeLogs")]
        public async Task<SearchResponse<FileChangeLog>> SearchChangeLogs(FileChangeLogSearchRequestModel model)
        {
            return await _fileService.Search(
                t => string.IsNullOrEmpty(model.Keyword) || t.Old.Contains(model.Keyword) ||
                     t.New.Contains(model.Keyword), model.PageIndex, model.PageSize);
        }

        [HttpDelete("clean")]
        [SwaggerOperation(OperationId = "CleanUnknownFiles")]
        public async Task<BaseResponse> CleanUnknownFiles()
        {
            await _cleanerManager.Clean();
            return BaseResponseBuilder.Ok;
        }
    }
}