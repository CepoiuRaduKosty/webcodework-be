// Services/TokenService.cs
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WebCodeWork.Models;

namespace WebCodeWork.Services
{
    public interface ITokenService
    {
        (string Token, DateTime Expiration) GenerateToken(User user);
    }

    public class TokenService : ITokenService
    {
        private readonly IConfiguration _configuration;

        public TokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public (string Token, DateTime Expiration) GenerateToken(User user)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]
                ?? throw new InvalidOperationException("JWT Key not configured.")));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var issuer = jwtSettings["Issuer"]
                ?? throw new InvalidOperationException("JWT Issuer not configured.");
            var audience = jwtSettings["Audience"]
                ?? throw new InvalidOperationException("JWT Audience not configured.");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), // Subject (usually user ID)
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // Unique Token ID
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64), // Issued At
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), // Standard claim for user ID
                new Claim(ClaimTypes.Name, user.Username) // Standard claim for username
                // Add other claims as needed (e.g., roles)
            };

            var expiration = DateTime.UtcNow.AddHours(1); // Example: Token valid for 1 hour

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiration,
                signingCredentials: creds
            );

            var tokenHandler = new JwtSecurityTokenHandler();
            return (tokenHandler.WriteToken(token), expiration);
        }
    }
}