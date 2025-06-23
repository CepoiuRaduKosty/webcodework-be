using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class UserProfileDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? ProfilePhotoUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UserSearchResultDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? ProfilePhotoUrl { get; set; } 
    }

    public class UserSearchRequestDto 
    {
        [Required(ErrorMessage = "Search term is required.")]
        public string SearchTerm { get; set; } = string.Empty;

        public int Limit { get; set; } = 5; 
    }
}