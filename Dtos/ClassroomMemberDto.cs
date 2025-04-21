using WebCodeWork.Enums;

namespace WebCodeWork.Dtos
{
    public class ClassroomMemberDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty; // Include username for easier display
        public int ClassroomId { get; set; }
        public ClassroomRole Role { get; set; }
        public DateTime JoinedAt { get; set; }
    }
}