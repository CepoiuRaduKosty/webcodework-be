// Dtos/UpdateClassroomDto.cs (or add to an existing Classroom DTO file)
using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class UpdateClassroomDto
    {
        [Required(ErrorMessage = "Classroom name is required.")]
        [MaxLength(150, ErrorMessage = "Classroom name cannot exceed 150 characters.")]
        [MinLength(3, ErrorMessage = "Classroom name must be at least 3 characters long.")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string? Description { get; set; } 
    }
}