
using System.ComponentModel.DataAnnotations;

namespace YourProjectName.Dtos
{
    public class CreateVirtualFileDto
    {
        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;
    }
}