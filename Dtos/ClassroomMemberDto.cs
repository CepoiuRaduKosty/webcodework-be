using WebCodeWork.Enums;

namespace WebCodeWork.Dtos
{
    public class ClassroomMemberDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty; 
        public int ClassroomId { get; set; }
        public ClassroomRole Role { get; set; }
        public DateTime JoinedAt { get; set; }
        public string? ProfilePhotoUrl { get; set; }
    }
}