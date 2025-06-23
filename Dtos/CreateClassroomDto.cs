
using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class CreateClassroomDto
    {
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }
    }
}