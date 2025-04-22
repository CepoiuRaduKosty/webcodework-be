// Dtos/UserClassroomDto.cs
using WebCodeWork.Enums;

namespace WebCodeWork.Dtos
{
    /// <summary>
    /// Represents a classroom from the perspective of the current user,
    /// including their role in it.
    /// </summary>
    public class UserClassroomDto
    {
        public int ClassroomId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ClassroomRole UserRole { get; set; } // The user's role in this specific classroom
        public DateTime JoinedAt { get; set; } // When the user joined this classroom
        // Optional: Add owner info or member count if needed later
        // public string OwnerUsername { get; set; }
        // public int MemberCount { get; set; }
    }
}