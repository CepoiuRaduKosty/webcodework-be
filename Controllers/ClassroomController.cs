// Controllers/ClassroomsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims; // Required for User.FindFirstValue
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;
using WebCodeWork.Services;

namespace WebCodeWork.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // All endpoints in this controller require authentication
    public class ClassroomsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ClassroomsController> _logger;
        private readonly IFileStorageService _fileService;
        private readonly IConfiguration _configuration;

        private const string ClassroomPhotoBaseDir = "classroom_photos";

        public ClassroomsController(ApplicationDbContext context,
            ILogger<ClassroomsController> logger,
            IFileStorageService fileService,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _fileService = fileService;
            _configuration = configuration;
        }

        // Helper method to get current user ID from JWT token
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                // This should not happen if [Authorize] is working, but defensive coding is good
                throw new UnauthorizedAccessException("User ID not found in token.");
            }
            return userId;
        }

        // Helper method to check user role within a specific classroom
        private async Task<ClassroomRole?> GetUserRoleInClassroom(int userId, int classroomId)
        {
            var membership = await _context.ClassroomMembers
                .FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ClassroomId == classroomId);

            return membership?.Role;
        }

        private string? GetPublicPhotoUrl(string PhotoPath, string PhotoStoredName) // Updated Helper
        {
            if (string.IsNullOrEmpty(PhotoPath) || string.IsNullOrEmpty(PhotoStoredName))
            {
                return null;
            }

            var publicStorageBaseUrl = _configuration.GetValue<string>("AzureStorage:PublicStorageBaseUrl");
            var publicPhotosContainerName = _configuration.GetValue<string>("AzureStorage:PublicPhotosContainerName");

            if (string.IsNullOrEmpty(publicStorageBaseUrl) || string.IsNullOrEmpty(publicPhotosContainerName))
            {
                _logger.LogWarning("AzureStorage:PublicStorageBaseUrl or PublicPhotosContainerName not configured. Cannot generate photo URLs.");
                return null;
            }
            // classroom.PhotoPath is already like "classrooms/123/photo"
            // We construct URL: base/container/path/storedName
            return $"{publicStorageBaseUrl.TrimEnd('/')}/{publicPhotosContainerName.TrimEnd('/')}/{PhotoPath.TrimStart('/')}/{PhotoStoredName}";
        }

        // --- 1. Create Classroom ---
        [HttpPost]
        [ProducesResponseType(typeof(ClassroomDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateClassroom([FromBody] CreateClassroomDto createDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            int ownerUserId;
            try
            {
                ownerUserId = GetCurrentUserId();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }

            var classroom = new Classroom
            {
                Name = createDto.Name,
                Description = createDto.Description,
                CreatedAt = DateTime.UtcNow
            };

            // Add the creator as the Owner
            var ownerMembership = new ClassroomMember
            {
                UserId = ownerUserId,
                Classroom = classroom, // Link to the classroom object
                Role = ClassroomRole.Owner,
                JoinedAt = DateTime.UtcNow
            };

            // It's usually best to add the principal entity first if not using navigation property assignment
            _context.Classrooms.Add(classroom);
            _context.ClassroomMembers.Add(ownerMembership); // Add membership explicitly


            try
            {
                await _context.SaveChangesAsync();

                // Return the created classroom details (using a DTO is recommended)
                var classroomDto = new ClassroomDto
                {
                    Id = classroom.Id,
                    Name = classroom.Name,
                    Description = classroom.Description,
                    CreatedAt = classroom.CreatedAt
                };
                // Use CreatedAtAction for proper RESTful response
                return CreatedAtAction(nameof(GetClassroomById), new { classroomId = classroom.Id }, classroomDto);
                // return Created(nameof(GetClassroomById), classroomDto); // Alternative basic Created response

            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error creating classroom for user {UserId}", ownerUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error saving classroom to database." });
            }
        }

        // --- GET Endpoint for CreatedAtAction (Optional but good practice) ---
        [HttpGet("{classroomId}")]
        [ProducesResponseType(typeof(ClassroomDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetClassroomById(int classroomId)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

            var isMember = await _context.ClassroomMembers
                                    .AnyAsync(cm => cm.ClassroomId == classroomId && cm.UserId == currentUserId);

            if (!isMember)
            {
                return Forbid();
            }


            var classroom = await _context.Classrooms
                           .AsNoTracking()
                           .FirstOrDefaultAsync(c => c.Id == classroomId);

            if (classroom == null)
            {
                return NotFound(new { message = "Classroom not found." });
            }

            var classroomDto = new ClassroomDto
            {
                Id = classroom.Id,
                Name = classroom.Name,
                Description = classroom.Description,
                CreatedAt = classroom.CreatedAt,
                PhotoUrl = GetPublicPhotoUrl(classroom.PhotoPath!, classroom.PhotoStoredName!),
            };
            return Ok(classroomDto);
        }


        // --- 2. Delete Classroom ---
        [HttpDelete("{classroomId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteClassroom(int classroomId)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

            var classroom = await _context.Classrooms.FindAsync(classroomId);
            if (classroom == null)
            {
                return NotFound(new { message = "Classroom not found." });
            }

            // Authorization Check: Is the current user the Owner?
            var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (userRole != ClassroomRole.Owner)
            {
                _logger.LogWarning("User {UserId} attempted to delete classroom {ClassroomId} without Owner role.", currentUserId, classroomId);
                return Forbid(); // Or NotFound() if you want to hide existence
            }

            if (!string.IsNullOrEmpty(classroom.PhotoPath) && !string.IsNullOrEmpty(classroom.PhotoStoredName))
            {
                try
                {
                    await _fileService.DeleteClassroomPhotoAsync(classroom.PhotoPath, classroom.PhotoStoredName);
                    _logger.LogInformation("Deleted photo for classroom {ClassroomId} during classroom deletion.", classroomId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete photo for classroom {ClassroomId} during classroom deletion. Continuing with DB record deletion.", classroomId);
                }
            }

            try
            {
                _context.Classrooms.Remove(classroom); // EF Core Cascade delete should handle members
                await _context.SaveChangesAsync();
                return NoContent(); // Standard successful DELETE response
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error deleting classroom {ClassroomId} by user {UserId}", classroomId, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error deleting classroom from database." });
            }
        }

        // --- 3. Add Teacher ---
        [HttpPost("{classroomId}/teachers")]
        [ProducesResponseType(typeof(ClassroomMemberDto), StatusCodes.Status200OK)] // Or 201 Created
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddTeacher(int classroomId, [FromBody] AddMemberDto addDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

            // Authorization Check: Is the current user the Owner?
            var currentUserRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (currentUserRole != ClassroomRole.Owner)
            {
                _logger.LogWarning("User {UserId} attempted to add teacher to classroom {ClassroomId} without Owner role.", currentUserId, classroomId);
                return Forbid();
            }

            // Check if classroom exists
            var classroomExists = await _context.Classrooms.AnyAsync(c => c.Id == classroomId);
            if (!classroomExists)
            {
                return NotFound(new { message = "Classroom not found." });
            }

            // Check if the target user exists
            var targetUser = await _context.Users
                                       .AsNoTracking() // No need to track the user being added
                                       .Select(u => new { u.Id, u.Username }) // Select only needed fields
                                       .FirstOrDefaultAsync(u => u.Id == addDto.UserId);
            if (targetUser == null)
            {
                return BadRequest(new { message = $"User with ID {addDto.UserId} not found." });
            }

            // Check if the user is already a member
            var existingMembership = await GetUserRoleInClassroom(addDto.UserId, classroomId);
            if (existingMembership != null)
            {
                return BadRequest(new { message = $"User {targetUser.Username} is already a {existingMembership} in this classroom." });
            }

            // Add the new member
            var newMembership = new ClassroomMember
            {
                UserId = addDto.UserId,
                ClassroomId = classroomId,
                Role = ClassroomRole.Teacher,
                JoinedAt = DateTime.UtcNow
            };

            _context.ClassroomMembers.Add(newMembership);

            try
            {
                await _context.SaveChangesAsync();

                // Return details of the added member
                var memberDto = new ClassroomMemberDto
                {
                    UserId = newMembership.UserId,
                    Username = targetUser.Username, // Include username from the earlier check
                    ClassroomId = newMembership.ClassroomId,
                    Role = newMembership.Role,
                    JoinedAt = newMembership.JoinedAt
                };
                return Ok(memberDto); // Or return CreatedAtAction if you have a GetMember endpoint
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error adding teacher {TargetUserId} to classroom {ClassroomId} by user {CurrentUserId}", addDto.UserId, classroomId, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error adding teacher to the classroom." });
            }
        }


        // --- 4. Add Student ---
        [HttpPost("{classroomId}/students")]
        [ProducesResponseType(typeof(ClassroomMemberDto), StatusCodes.Status200OK)] // Or 201 Created
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddStudent(int classroomId, [FromBody] AddMemberDto addDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

            // Authorization Check: Is the current user Owner or Teacher?
            var currentUserRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (currentUserRole != ClassroomRole.Owner && currentUserRole != ClassroomRole.Teacher)
            {
                _logger.LogWarning("User {UserId} with role {Role} attempted to add student to classroom {ClassroomId}.", currentUserId, currentUserRole, classroomId);
                return Forbid();
            }

            // Check if classroom exists
            var classroomExists = await _context.Classrooms.AnyAsync(c => c.Id == classroomId);
            if (!classroomExists)
            {
                return NotFound(new { message = "Classroom not found." });
            }

            // Check if the target user exists
            var targetUser = await _context.Users
                                      .AsNoTracking()
                                      .Select(u => new { u.Id, u.Username })
                                      .FirstOrDefaultAsync(u => u.Id == addDto.UserId);
            if (targetUser == null)
            {
                return BadRequest(new { message = $"User with ID {addDto.UserId} not found." });
            }

            // Check if the user is already a member
            var existingMembership = await GetUserRoleInClassroom(addDto.UserId, classroomId);
            if (existingMembership != null)
            {
                return BadRequest(new { message = $"User {targetUser.Username} is already a {existingMembership} in this classroom." });
            }

            // Add the new member
            var newMembership = new ClassroomMember
            {
                UserId = addDto.UserId,
                ClassroomId = classroomId,
                Role = ClassroomRole.Student,
                JoinedAt = DateTime.UtcNow
            };

            _context.ClassroomMembers.Add(newMembership);

            try
            {
                await _context.SaveChangesAsync();

                // Return details of the added member
                var memberDto = new ClassroomMemberDto
                {
                    UserId = newMembership.UserId,
                    Username = targetUser.Username,
                    ClassroomId = newMembership.ClassroomId,
                    Role = newMembership.Role,
                    JoinedAt = newMembership.JoinedAt
                };
                return Ok(memberDto);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error adding student {TargetUserId} to classroom {ClassroomId} by user {CurrentUserId}", addDto.UserId, classroomId, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error adding student to the classroom." });
            }
        }

        // --- GET User's Classrooms ---
        [HttpGet("my")] // Route: GET /api/classrooms/my
        [ProducesResponseType(typeof(IEnumerable<UserClassroomDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetMyClassrooms()
        {
            int currentUserId;
            try
            {
                currentUserId = GetCurrentUserId();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }

            var userClassroomsModels = await _context.ClassroomMembers
                .Where(cm => cm.UserId == currentUserId)
                .Include(cm => cm.Classroom)
                .OrderBy(cm => cm.Classroom.Name)
                .ToListAsync();

            var userClassrooms = userClassroomsModels
                .Select(cm => new UserClassroomDto
                {
                    ClassroomId = cm.ClassroomId,
                    Name = cm.Classroom.Name,
                    Description = cm.Classroom.Description,
                    UserRole = cm.Role,
                    JoinedAt = cm.JoinedAt,
                    PhotoUrl = GetPublicPhotoUrl(cm.Classroom.PhotoPath!, cm.Classroom.PhotoStoredName!)
                })
                .ToList();

            return Ok(userClassrooms);
        }

        // --- GET Classroom Details (including members) ---
        [HttpGet("{classroomId}/details")]
        [ProducesResponseType(typeof(ClassroomDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetClassroomDetails(int classroomId)
        {
            int currentUserId;
            try
            {
                currentUserId = GetCurrentUserId();
            }
            catch (UnauthorizedAccessException ex)
            {
                // User not authenticated
                return Unauthorized(new { message = ex.Message });
            }

            // Fetch the classroom and include member data with associated user info in one query
            var classroom = await _context.Classrooms
                .Include(c => c.Members)         // Include the ClassroomMembers collection
                    .ThenInclude(cm => cm.User) // For each member, include their User details
                .FirstOrDefaultAsync(c => c.Id == classroomId);

            // 1. Check if Classroom Exists
            if (classroom == null)
            {
                return NotFound(new { message = "Classroom not found." });
            }

            // 2. Authorization Check: Is the current user a member of this classroom?
            var currentUserMembership = classroom.Members
                .FirstOrDefault(cm => cm.UserId == currentUserId);

            if (currentUserMembership == null)
            {
                // User is authenticated but not a member of this specific classroom
                _logger.LogWarning("User {UserId} attempted to access details for classroom {ClassroomId} without membership.", currentUserId, classroomId);
                return Forbid(); // HTTP 403 Forbidden
            }

            // 3. Map the data to the DTO
            var membersDto = classroom.Members
                .OrderBy(cm => cm.Role) // Optional: Order members (e.g., Owner, Teacher, Student)
                .ThenBy(cm => cm.User.Username)
                .Select(cm => new ClassroomMemberDto
                {
                    UserId = cm.UserId,
                    Username = cm.User.Username, // Get username from included User entity
                    ClassroomId = cm.ClassroomId,
                    Role = cm.Role,
                    JoinedAt = cm.JoinedAt
                })
                .ToList(); // Convert to List

            var detailsDto = new ClassroomDetailsDto
            {
                Id = classroom.Id,
                Name = classroom.Name,
                Description = classroom.Description,
                CurrentUserRole = currentUserMembership.Role, // Get the role from the check above
                Members = membersDto,
                PhotoUrl = GetPublicPhotoUrl(classroom.PhotoPath!, classroom.PhotoStoredName!)
            };

            return Ok(detailsDto);
        }

        [HttpPost("{classroomId}/photo")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ClassroomDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UploadClassroomPhoto(int classroomId, [FromForm] IFormFile photoFile)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

            if (photoFile == null || photoFile.Length == 0)
                return BadRequest(new { message = "No photo file uploaded." });

            // Validate file type and size (example)
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var extension = Path.GetExtension(photoFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
                return BadRequest(new { message = "Invalid file type. Allowed types: " + string.Join(", ", allowedExtensions) });

            long maxFileSize = 5 * 1024 * 1024; // 5 MB
            if (photoFile.Length > maxFileSize)
                return BadRequest(new { message = $"File size exceeds limit of {maxFileSize / 1024 / 1024} MB." });


            var classroom = await _context.Classrooms.FirstOrDefaultAsync(c => c.Id == classroomId);
            if (classroom == null) return NotFound(new { message = "Classroom not found." });

            // Authorization: Only classroom owner can change photo
            var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (userRole != ClassroomRole.Owner)
            {
                _logger.LogWarning("User {UserId} (Role: {Role}) attempted to upload photo for classroom {ClassroomId} without Owner permission.", currentUserId, userRole, classroomId);
                return Forbid();
            }

            // If an old photo exists, delete it from storage
            if (!string.IsNullOrEmpty(classroom.PhotoPath) && !string.IsNullOrEmpty(classroom.PhotoStoredName))
            {
                await _fileService.DeleteTestCaseFileAsync(classroom.PhotoPath, classroom.PhotoStoredName); // Assuming DeleteTestCaseFileAsync can take generic path/name
                _logger.LogInformation("Old photo {OldPhoto} deleted for classroom {ClassroomId}", classroom.PhotoStoredName, classroomId);
            }

            // Save the new photo using the dedicated service method
            var (storedFileName, relativePath) = await _fileService.SaveClassroomPhotoAsync(classroomId, photoFile);

            classroom.PhotoOriginalName = photoFile.FileName;
            classroom.PhotoStoredName = storedFileName;
            classroom.PhotoPath = relativePath; // This is the path within the public_photos container
            classroom.PhotoContentType = photoFile.ContentType;

            _context.Classrooms.Update(classroom);
            await _context.SaveChangesAsync();

            // Map and return (as before, ensuring GetPublicPhotoUrl is called)
            var classroomDto = new ClassroomDto
            {
                Id = classroom.Id,
                Name = classroom.Name,
                Description = classroom.Description,
                CreatedAt = classroom.CreatedAt,
                PhotoUrl = GetPublicPhotoUrl(classroom.PhotoPath, classroom.PhotoStoredName)
            };
            return Ok(classroomDto);
        }

        // --- NEW: Remove Classroom Photo ---
        [HttpDelete("{classroomId}/photo")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteClassroomPhoto(int classroomId)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

            var classroom = await _context.Classrooms.FirstOrDefaultAsync(c => c.Id == classroomId);
            if (classroom == null) return NotFound(new { message = "Classroom not found." });

            // Authorization: Only classroom owner can remove photo
            var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (userRole != ClassroomRole.Owner)
            {
                _logger.LogWarning("User {UserId} (Role: {Role}) attempted to delete photo for classroom {ClassroomId} without Owner permission.", currentUserId, userRole, classroomId);
                return Forbid();
            }

            if (!string.IsNullOrEmpty(classroom.PhotoPath) && !string.IsNullOrEmpty(classroom.PhotoStoredName))
            {
                bool deleted = await _fileService.DeleteClassroomPhotoAsync(classroom.PhotoPath, classroom.PhotoStoredName);
                if (deleted)
                {
                    _logger.LogInformation("Photo {StoredName} deleted from storage for classroom {ClassroomId}.", classroom.PhotoStoredName, classroomId);
                }
                else
                {
                    _logger.LogWarning("Photo {StoredName} for classroom {ClassroomId} not found in storage or delete failed, but clearing DB refs.", classroom.PhotoStoredName, classroomId);
                }

                classroom.PhotoOriginalName = null;
                classroom.PhotoStoredName = null;
                classroom.PhotoPath = null;
                classroom.PhotoContentType = null;

                _context.Classrooms.Update(classroom);
                await _context.SaveChangesAsync();
            }
            else
            {
                _logger.LogInformation("No photo to delete for classroom {ClassroomId}.", classroomId);
            }

            return NoContent();
        }
        
        [HttpPut("{classroomId}")]
        [ProducesResponseType(typeof(ClassroomDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateClassroom(int classroomId, [FromBody] UpdateClassroomDto updateDto)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            int currentUserId;
            try
            {
                currentUserId = GetCurrentUserId();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new ProblemDetails { Title = "Unauthorized", Detail = ex.Message });
            }

            _logger.LogInformation("User {UserId} attempting to update classroom {ClassroomId}", currentUserId, classroomId);

            var classroom = await _context.Classrooms.FirstOrDefaultAsync(c => c.Id == classroomId);

            if (classroom == null)
            {
                return NotFound(new ProblemDetails { Title = "Not Found", Detail = $"Classroom with ID {classroomId} not found." });
            }

            var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (userRole != ClassroomRole.Owner)
            {
                _logger.LogWarning("User {UserId} (Role: {UserRole}) forbidden from updating classroom {ClassroomId}.",
                    currentUserId, userRole?.ToString() ?? "N/A", classroomId);
                return Forbid(); 
            }

            classroom.Name = updateDto.Name;
            classroom.Description = updateDto.Description;

            _context.Classrooms.Update(classroom);

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Classroom {ClassroomId} updated successfully by User {UserId}.", classroomId, currentUserId);

                var classroomDto = new ClassroomDto
                {
                    Id = classroom.Id,
                    Name = classroom.Name,
                    Description = classroom.Description,
                    CreatedAt = classroom.CreatedAt,
                    PhotoUrl = GetPublicPhotoUrl(classroom.PhotoPath!, classroom.PhotoStoredName!)
                };
                return Ok(classroomDto);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                 _logger.LogError(ex, "Concurrency error while updating classroom {ClassroomId}.", classroomId);
                 return Conflict(new ProblemDetails { Title = "Conflict", Detail = "The classroom was modified by another user. Please refresh and try again."});
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error while updating classroom {ClassroomId}.", classroomId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Database Error", Detail = "Could not update classroom details." });
            }
        }
    }
}