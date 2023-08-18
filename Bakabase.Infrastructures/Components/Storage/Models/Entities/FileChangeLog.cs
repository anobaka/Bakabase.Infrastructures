using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.Infrastructures.Components.Storage.Models.Entities
{
    public class FileChangeLog
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(32)]
        public string BatchId { get; set; }

        [Required]
        public string Old { set; get; }

        public string New { set; get; }

        public DateTime ChangeDt { set; get; } = DateTime.Now;
    }
}