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
        public string? ProfilePhotoUrl { get; set; } // Good to include for the frontend selection UI
    }

    public class UserSearchRequestDto // Optional, if you want a DTO for query params
    {
        [Required(ErrorMessage = "Search term is required.")]
        public string SearchTerm { get; set; } = string.Empty;

        public int Limit { get; set; } = 5; // Default limit for results
    }
}