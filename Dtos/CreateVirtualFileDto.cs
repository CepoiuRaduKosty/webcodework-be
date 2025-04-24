// Dtos/CreateVirtualFileDto.cs
using System.ComponentModel.DataAnnotations;

namespace YourProjectName.Dtos
{
    public class CreateVirtualFileDto
    {
        [Required]
        [MaxLength(255)]
        // Basic validation - could add regex for allowed characters/extensions
        [RegularExpression(@"^[\w\-. ]+\.(c|cpp|h|txt)$", ErrorMessage = "Invalid filename or extension (only .c, .cpp, .h, .txt allowed).")] // Example validation
        public string FileName { get; set; } = string.Empty;
    }
}