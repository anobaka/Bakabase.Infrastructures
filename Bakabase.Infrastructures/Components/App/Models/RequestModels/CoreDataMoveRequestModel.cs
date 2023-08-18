using System.ComponentModel.DataAnnotations;

namespace Bakabase.Infrastructures.Components.App.Models.RequestModels
{
    public class CoreDataMoveRequestModel
    {
        [Required]
        public string DataPath { get; set; }
    }
}
