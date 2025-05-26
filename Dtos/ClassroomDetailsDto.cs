// Dtos/ClassroomDetailsDto.cs
using WebCodeWork.Enums;
using System.Collections.Generic; // Required for List

namespace WebCodeWork.Dtos
{
    /// <summary>
    /// Represents the detailed view of a classroom, including its members
    /// and the role of the user requesting the details.
    /// </summary>
    public class ClassroomDetailsDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ClassroomRole CurrentUserRole { get; set; } // Role of the user making the request
        public List<ClassroomMemberDto> Members { get; set; } = new List<ClassroomMemberDto>(); // List of all members
        public string? PhotoUrl { get; set; }
    }
}