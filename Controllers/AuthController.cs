using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Models;
using WebCodeWork.Services;

namespace YourProjectName.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthController> _logger; // Optional: for logging

        public AuthController(
            ApplicationDbContext context,
            IPasswordService passwordService,
            ITokenService tokenService,
            ILogger<AuthController> logger)
        {
            _context = context;
            _passwordService = passwordService;
            _tokenService = tokenService;
            _logger = logger;
        }

        [HttpPost("register")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
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