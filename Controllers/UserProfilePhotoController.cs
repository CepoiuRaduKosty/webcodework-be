// Controllers/UserProfilePhotoController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using WebCodeWork.Data;      // Your DbContext
using WebCodeWork.Models;    // Your User model
using WebCodeWork.Services;  // Your IFileStorageService
using WebCodeWork.Dtos;      // For UserProfileDto or similar response

namespace WebCodeWork.Controllers
{
    [Route("api/user/profile/photo")] // Route for current user's photo
    [ApiController]
    [Authorize] // All actions require an authenticated user
    public class UserProfilePhotoController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileStorageService _fileService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserProfilePhotoController> _logger;

        public UserProfilePhotoController(
            ApplicationDbContext context,
            IFileStorageService fileService,
            IConfiguration configuration,
            ILogger<UserProfilePhotoController> logger)
        {
            _context = context;
            _fileService = fileService;
            _configuration = configuration;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new UnauthorizedAccessException("User ID not found or invalid in token.");
            return userId;
        }

        // Helper to construct Photo URL (can be shared or defined per controller)
        private string? GetPublicUserProfilePhotoUrl(User user)
        {
            if (string.IsNullOrEmpty(user.ProfilePhotoPath) || string.IsNullOrEmpty(user.ProfilePhotoStoredName))
                return null;

            var publicStorageBaseUrl = _configuration.GetValue<string>("AzureStorage:PublicStorageBaseUrl");
            var profilePhotosContainer = _configuration.GetValue<string>("AzureStorage:PublicProfilePhotosContainerName");

            if (string.IsNullOrEmpty(publicStorageBaseUrl) || string.IsNullOrEmpty(profilePhotosContainer))
            {
                _logger.LogWarning("PublicStorageBaseUrl or PublicProfilePhotosContainerName not configured.");
                return null;
            }
            return $"{publicStorageBaseUrl.TrimEnd('/')}/{profilePhotosContainer.TrimEnd('/')}/{user.ProfilePhotoPath.TrimStart('/')}/{user.ProfilePhotoStoredName}";
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)] // Return updated user profile
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UploadOrUpdateProfilePhoto([FromForm] IFormFile photoFile)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException) { return Unauthorized(); }

            if (photoFile == null || photoFile.Length == 0)
                return BadRequest(new ProblemDetails { Title = "File Error", Detail = "No photo file uploaded." });

            // File validation (example)
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(photoFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
                return BadRequest(new ProblemDetails { Title = "Invalid File Type", Detail = "Allowed types: " + string.Join(", ", allowedExtensions) });
            long maxFileSize = 2 * 1024 * 1024; // 2 MB
            if (photoFile.Length > maxFileSize)
                return BadRequest(new ProblemDetails { Title = "File Too Large", Detail = $"File size exceeds limit of {maxFileSize / 1024 / 1024} MB." });

            var user = await _context.Users.FindAsync(currentUserId);
            if (user == null) return NotFound(new ProblemDetails { Title = "User Not Found" });

            // If an old photo exists, delete it from storage
            if (!string.IsNullOrEmpty(user.ProfilePhotoPath) && !string.IsNullOrEmpty(user.ProfilePhotoStoredName))
            {
                await _fileService.DeleteUserProfilePhotoAsync(user.ProfilePhotoPath, user.ProfilePhotoStoredName);
                _logger.LogInformation("Old profile photo {OldPhoto} deleted for User {UserId}", user.ProfilePhotoStoredName, currentUserId);
            }

            // Save the new photo
            var (storedFileName, relativePath) = await _fileService.SaveUserProfilePhotoAsync(currentUserId, photoFile);

            user.ProfilePhotoOriginalName = photoFile.FileName;
            user.ProfilePhotoStoredName = storedFileName;
            user.ProfilePhotoPath = relativePath;
            user.ProfilePhotoContentType = photoFile.ContentType;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();
            _logger.LogInformation("New profile photo {NewPhoto} uploaded for User {UserId}", storedFileName, currentUserId);

            var userProfileDto = new UserProfileDto // Assuming you have this DTO
            {
                Id = user.Id,
                Username = user.Username,
                ProfilePhotoUrl = GetPublicUserProfilePhotoUrl(user),
                CreatedAt = user.CreatedAt
            };
            return Ok(userProfileDto);
        }

        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteProfilePhoto()
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException) { return Unauthorized(); }

            var user = await _context.Users.FindAsync(currentUserId);
            if (user == null) return NotFound(new ProblemDetails { Title = "User Not Found" });

            if (!string.IsNullOrEmpty(user.ProfilePhotoPath) && !string.IsNullOrEmpty(user.ProfilePhotoStoredName))
            {
                bool deleted = await _fileService.DeleteUserProfilePhotoAsync(user.ProfilePhotoPath, user.ProfilePhotoStoredName);
                if (deleted) _logger.LogInformation("Profile photo {StoredName} deleted from storage for User {UserId}.", user.ProfilePhotoStoredName, currentUserId);
                else _logger.LogWarning("Profile photo {StoredName} for User {UserId} not found in storage or delete failed, but clearing DB refs.", user.ProfilePhotoStoredName, currentUserId);

                user.ProfilePhotoOriginalName = null;
                user.ProfilePhotoStoredName = null;
                user.ProfilePhotoPath = null;
                user.ProfilePhotoContentType = null;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();
            }
            else
            {
                _logger.LogInformation("No profile photo to delete for User {UserId}.", currentUserId);
            }
            return NoContent();
        }
    }
}