using System.ComponentModel.DataAnnotations;

namespace WebCodeWork.Dtos
{
    public class RegisterDto
    {
        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MinLength(6)] // Example minimum length
        public string Password { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
        public string? ProfilePhotoUrl { get; set; }
    }
    
    public class ChangeUsernameDto
    {
        [Required(ErrorMessage = "New username is required.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 100 characters.")]
        public string NewUsername { get; set; } = string.Empty;
    }

    public class ChangePasswordDto
    {
        [Required(ErrorMessage = "Current password is required.")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirmation password is required.")]
        [Compare(nameof(NewPassword), ErrorMessage = "New password and confirmation password do not match.")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}