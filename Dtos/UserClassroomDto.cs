
using WebCodeWork.Enums;

namespace WebCodeWork.Dtos
{
    public class UserClassroomDto
    {
        public int ClassroomId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ClassroomRole UserRole { get; set; }
        public DateTime JoinedAt { get; set; } 
        public string? PhotoUrl { get; set; }
    }
}