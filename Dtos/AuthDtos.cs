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
}

namespace WebCodeWork.Dtos
{
    public class LoginDto
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }
}

namespace WebCodeWork.Dtos
{
    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
         public DateTime Expiration { get; set; }
    }
}