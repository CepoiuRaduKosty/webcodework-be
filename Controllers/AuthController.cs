using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Models;
using WebCodeWork.Services;
using System.Text.RegularExpressions; 
using System.Linq;
using PwnedPasswords.Client;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YourProjectName.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthController> _logger;
        private readonly IPwnedPasswordsClient _pwnedPasswordsClient;

        private const int MinPasswordLength = 8;

        public AuthController(
            ApplicationDbContext context,
            IPasswordService passwordService,
            ITokenService tokenService,
            IPwnedPasswordsClient pwnedPasswordsClient,
            ILogger<AuthController> logger)
        {
            _context = context;
            _passwordService = passwordService;
            _tokenService = tokenService;
            _pwnedPasswordsClient = pwnedPasswordsClient;
            _logger = logger;
        }

        private async Task<List<string>> ValidatePassword(string password, string username)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
            {
                errors.Add($"Password must be at least {MinPasswordLength} characters long.");
            }
            if (!Regex.IsMatch(password, @"[A-Z]"))
            {
                errors.Add("Password must contain at least one uppercase letter.");
            }
            if (!Regex.IsMatch(password, @"[a-z]"))
            {
                errors.Add("Password must contain at least one lowercase letter.");
            }
            if (!Regex.IsMatch(password, @"[0-9]"))
            {
                errors.Add("Password must contain at least one digit.");
            }
            if (!Regex.IsMatch(password, @"[\W_]")) // \W is non-word character, _ is underscore
            {
                errors.Add("Password must contain at least one special character (e.g., !@#$%^&*).");
            }

            // Prevent passwords too similar to username (basic check)
            if (!string.IsNullOrWhiteSpace(username) && password.ToLower().Contains(username.ToLower()))
            {
                errors.Add("Password should not contain your username.");
            }
            
            try
            {
                var pwned = await _pwnedPasswordsClient.HasPasswordBeenPwned(password);
                if (pwned)
                {
                    _logger.LogWarning("User attempted to register with a pwned password (Username: {Username}, PwnedCount: {PwnedCount})", username, password);
                    errors.Add("This password has appeared in a data breach and is not allowed. Please choose a different password.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking password against HIBP Pwned Passwords API.");
            }

            return errors;
        }

        [HttpPost("register")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var passwordValidationErrors = await ValidatePassword(registerDto.Password, registerDto.Username);
            if (passwordValidationErrors.Any())
            {
                return BadRequest(new { message = passwordValidationErrors[0] });
            }

            var existingUser = await _context.Users
                                       .FirstOrDefaultAsync(u => u.Username == registerDto.Username);
            if (existingUser != null)
            {
                return BadRequest(new { message = "Username already exists." });
            }

            var user = new User
            {
                Username = registerDto.Username,
                PasswordHash = _passwordService.HashPassword(registerDto.Password),
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                return CreatedAtAction(nameof(Register), new { id = user.Id }, new { message = "User registered successfully." });
            }
            catch (DbUpdateException ex) // Catch specific EF Core exceptions
            {
                 _logger.LogError(ex, "Error saving user registration to database.");
                // Check inner exception for details, e.g., duplicate key violation
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred during registration. Please try again." });
            }
             catch (Exception ex) // Catch broader exceptions
            {
                _logger.LogError(ex, "An unexpected error occurred during registration.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
            }
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _context.Users
                                     .FirstOrDefaultAsync(u => u.Username == loginDto.Username);

            if (user == null || !_passwordService.VerifyPassword(loginDto.Password, user.PasswordHash))
            {
                return Unauthorized(new { message = "Invalid username or password." });
            }

            // Generate JWT token
            var (tokenString, expiration) = _tokenService.GenerateToken(user);

            return Ok(new LoginResponseDto
            {
                Token = tokenString,
                Username = user.Username,
                Expiration = expiration
            });
        }
    }
}