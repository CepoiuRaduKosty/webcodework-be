namespace WebCodeWork.Dtos
{
    public class UserProfileDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? ProfilePhotoUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}