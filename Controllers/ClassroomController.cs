// Controllers/ClassroomsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;
using WebCodeWork.Services;

namespace WebCodeWork.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ClassroomsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ClassroomsController> _logger;
        private readonly IFileStorageService _fileService;
        private readonly IConfiguration _configuration;

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

       
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("User ID not found in token.");
            }
            return userId;
        }
       
        private async Task<ClassroomRole?> GetUserRoleInClassroom(int userId, int classroomId)
        {
            var membership = await _context.ClassroomMembers
                .FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ClassroomId == classroomId);
            return membership?.Role;
        }

        private string? GetPublicPhotoUrl(string PhotoPath, string PhotoStoredName)
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
           
            return $"{publicStorageBaseUrl.TrimEnd('/')}/{publicPhotosContainerName.TrimEnd('/')}/{PhotoPath.TrimStart('/')}/{PhotoStoredName}";
        }

       
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

            var ownerMembership = new ClassroomMember
            {
                UserId = ownerUserId,
                Classroom = classroom, 
                Role = ClassroomRole.Owner,
                JoinedAt = DateTime.UtcNow
            };

            _context.Classrooms.Add(classroom);
            _context.ClassroomMembers.Add(ownerMembership); 

            try
            {
                await _context.SaveChangesAsync();
               
                var classroomDto = new ClassroomDto
                {
                    Id = classroom.Id,
                    Name = classroom.Name,
                    Description = classroom.Description,
                    CreatedAt = classroom.CreatedAt
                };
               
                return CreatedAtAction(nameof(GetClassroomById), new { classroomId = classroom.Id }, classroomDto);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error creating classroom for user {UserId}", ownerUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error saving classroom to database." });
            }
        }
       
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
           
            var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (userRole != ClassroomRole.Owner)
            {
                _logger.LogWarning("User {UserId} attempted to delete classroom {ClassroomId} without Owner role.", currentUserId, classroomId);
                return Forbid();
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
                _context.Classrooms.Remove(classroom);
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error deleting classroom {ClassroomId} by user {UserId}", classroomId, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error deleting classroom from database." });
            }
        }

       
        [HttpPost("{classroomId}/teachers")]
        [ProducesResponseType(typeof(ClassroomMemberDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddTeacher(int classroomId, [FromBody] AddMemberDto addDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
           
            var currentUserRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (currentUserRole != ClassroomRole.Owner)
            {
                _logger.LogWarning("User {UserId} attempted to add teacher to classroom {ClassroomId} without Owner role.", currentUserId, classroomId);
                return Forbid();
            }
           
            var classroomExists = await _context.Classrooms.AnyAsync(c => c.Id == classroomId);
            if (!classroomExists)
            {
                return NotFound(new { message = "Classroom not found." });
            }
           
            var targetUser = await _context.Users
                                       .AsNoTracking()
                                       .Select(u => new { u.Id, u.Username })
                                       .FirstOrDefaultAsync(u => u.Id == addDto.UserId);
            if (targetUser == null)
            {
                return BadRequest(new { message = $"User with ID {addDto.UserId} not found." });
            }
           
            var existingMembership = await GetUserRoleInClassroom(addDto.UserId, classroomId);
            if (existingMembership != null)
            {
                return BadRequest(new { message = $"User {targetUser.Username} is already a {existingMembership} in this classroom." });
            }
           
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
                _logger.LogError(ex, "Database error adding teacher {TargetUserId} to classroom {ClassroomId} by user {CurrentUserId}", addDto.UserId, classroomId, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error adding teacher to the classroom." });
            }
        }

        [HttpPost("{classroomId}/students")]
        [ProducesResponseType(typeof(ClassroomMemberDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddStudent(int classroomId, [FromBody] AddMemberDto addDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

            var currentUserRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (currentUserRole != ClassroomRole.Owner && currentUserRole != ClassroomRole.Teacher)
            {
                _logger.LogWarning("User {UserId} with role {Role} attempted to add student to classroom {ClassroomId}.", currentUserId, currentUserRole, classroomId);
                return Forbid();
            }
           
            var classroomExists = await _context.Classrooms.AnyAsync(c => c.Id == classroomId);
            if (!classroomExists)
            {
                return NotFound(new { message = "Classroom not found." });
            }
           
            var targetUser = await _context.Users
                                      .AsNoTracking()
                                      .Select(u => new { u.Id, u.Username })
                                      .FirstOrDefaultAsync(u => u.Id == addDto.UserId);
            if (targetUser == null)
            {
                return BadRequest(new { message = $"User with ID {addDto.UserId} not found." });
            }
           
            var existingMembership = await GetUserRoleInClassroom(addDto.UserId, classroomId);
            if (existingMembership != null)
            {
                return BadRequest(new { message = $"User {targetUser.Username} is already a {existingMembership} in this classroom." });
            }
           
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

        [HttpGet("my")]
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
                return Unauthorized(new { message = ex.Message });
            }
           
            var classroom = await _context.Classrooms
                .Include(c => c.Members)        
                    .ThenInclude(cm => cm.User)
                .FirstOrDefaultAsync(c => c.Id == classroomId);

            if (classroom == null)
            {
                return NotFound(new { message = "Classroom not found." });
            }
           
            var currentUserMembership = classroom.Members
                .FirstOrDefault(cm => cm.UserId == currentUserId);

            if (currentUserMembership == null)
            {
                _logger.LogWarning("User {UserId} attempted to access details for classroom {ClassroomId} without membership.", currentUserId, classroomId);
                return Forbid();
            }

            var membersDto = classroom.Members
                .OrderBy(cm => cm.Role)
                .ThenBy(cm => cm.User.Username)
                .Select(cm => new ClassroomMemberDto
                {
                    UserId = cm.UserId,
                    Username = cm.User.Username,
                    ClassroomId = cm.ClassroomId,
                    Role = cm.Role,
                    JoinedAt = cm.JoinedAt,
                    ProfilePhotoUrl = _fileService.GetPublicUserProfilePhotoUrl(cm.User.ProfilePhotoPath!, cm.User.ProfilePhotoStoredName!),
                })
                .ToList();

            var detailsDto = new ClassroomDetailsDto
            {
                Id = classroom.Id,
                Name = classroom.Name,
                Description = classroom.Description,
                CurrentUserRole = currentUserMembership.Role,
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
           
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var extension = Path.GetExtension(photoFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
                return BadRequest(new { message = "Invalid file type. Allowed types: " + string.Join(", ", allowedExtensions) });

            long maxFileSize = 5 * 1024 * 1024;
            if (photoFile.Length > maxFileSize)
                return BadRequest(new { message = $"File size exceeds limit of {maxFileSize / 1024 / 1024} MB." });

            var classroom = await _context.Classrooms.FirstOrDefaultAsync(c => c.Id == classroomId);
            if (classroom == null) return NotFound(new { message = "Classroom not found." });
           
            var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (userRole != ClassroomRole.Owner)
            {
                _logger.LogWarning("User {UserId} (Role: {Role}) attempted to upload photo for classroom {ClassroomId} without Owner permission.", currentUserId, userRole, classroomId);
                return Forbid();
            }
           
            if (!string.IsNullOrEmpty(classroom.PhotoPath) && !string.IsNullOrEmpty(classroom.PhotoStoredName))
            {
                await _fileService.DeleteTestCaseFileAsync(classroom.PhotoPath, classroom.PhotoStoredName);
                _logger.LogInformation("Old photo {OldPhoto} deleted for classroom {ClassroomId}", classroom.PhotoStoredName, classroomId);
            }
           
            var (storedFileName, relativePath) = await _fileService.SaveClassroomPhotoAsync(classroomId, photoFile);

            classroom.PhotoOriginalName = photoFile.FileName;
            classroom.PhotoStoredName = storedFileName;
            classroom.PhotoPath = relativePath;
            classroom.PhotoContentType = photoFile.ContentType;

            _context.Classrooms.Update(classroom);
            await _context.SaveChangesAsync();
           
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
                return Conflict(new ProblemDetails { Title = "Conflict", Detail = "The classroom was modified by another user. Please refresh and try again." });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error while updating classroom {ClassroomId}.", classroomId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Database Error", Detail = "Could not update classroom details." });
            }
        }

        [HttpGet("{classroomId}/potential-members/search")]
        [ProducesResponseType(typeof(IEnumerable<UserSearchResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SearchPotentialMembers(
            int classroomId,
            [FromQuery, Required(ErrorMessage = "Search term is required.")]
            string searchTerm,
            [FromQuery, Range(1, 20)] int limit = 5)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException) { return Unauthorized(); }

            _logger.LogInformation("User {UserId} searching for potential members in classroom {ClassroomId} with term '{SearchTerm}'", currentUserId, classroomId, searchTerm);

            var classroom = await _context.Classrooms.FindAsync(classroomId);
            if (classroom == null)
            {
                return NotFound(new ProblemDetails { Title = "Not Found", Detail = $"Classroom with ID {classroomId} not found." });
            }
           
            var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher)
            {
                _logger.LogWarning("User {UserId} (Role: {UserRole}) forbidden from searching members for classroom {ClassroomId}.",
                    currentUserId, userRole?.ToString() ?? "N/A", classroomId);
                return Forbid();
            }

            var existingMemberIds = await _context.ClassroomMembers
                .Where(cm => cm.ClassroomId == classroomId)
                .Select(cm => cm.UserId)
                .Distinct()
                .ToListAsync();

            var potentialMembers = await _context.Users
                .Where(u => u.Username.ToLower().Contains(searchTerm.ToLower()))
                .Where(u => !existingMemberIds.Contains(u.Id))
                .OrderBy(u => u.Username)
                .Take(limit)
                .Select(u => new UserSearchResultDto
                {
                    UserId = u.Id,
                    Username = u.Username,
                    ProfilePhotoUrl = _fileService.GetPublicUserProfilePhotoUrl(u.ProfilePhotoPath!, u.ProfilePhotoStoredName!)
                })
                .ToListAsync();

            return Ok(potentialMembers);
        }

        [HttpPost("{classroomId}/leave")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> LeaveClassroom(int classroomId, [FromBody] LeaveClassroomRequestDto leaveRequestDto)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new ProblemDetails { Title = "Unauthorized", Detail = ex.Message }); }

            _logger.LogInformation("User {UserId} attempting to leave classroom {ClassroomId}", currentUserId, classroomId);
           
            var classroomForValidation = await _context.Classrooms
                .Include(c => c.Members)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == classroomId);

            if (classroomForValidation == null)
            {
                return NotFound(new ProblemDetails { Title = "Not Found", Detail = $"Classroom with ID {classroomId} not found." });
            }

            var currentUserMembershipForValidation = classroomForValidation.Members.FirstOrDefault(m => m.UserId == currentUserId);

            if (currentUserMembershipForValidation == null)
            {
                return BadRequest(new ProblemDetails { Title = "Not a Member", Detail = "You are not a member of this classroom." });
            }

            if (currentUserMembershipForValidation.Role == ClassroomRole.Owner)
            {
                var teachersInClassroom = classroomForValidation.Members
                    .Where(m => m.Role == ClassroomRole.Teacher && m.UserId != currentUserId)
                    .ToList();

                if (!teachersInClassroom.Any())
                {
                    var otherMembersCount = classroomForValidation.Members.Count(m => m.UserId != currentUserId);
                    if (otherMembersCount > 0)
                    {
                        return BadRequest(new ProblemDetails { Title = "Ownership Transfer Required", Detail = "You are the owner. To leave, you must first promote a teacher to owner, or delete the classroom if no other teachers exist." });
                    }
                    return BadRequest(new ProblemDetails { Title = "Cannot Leave", Detail = "As the sole owner and member, please delete the classroom instead of leaving." });
                }
                if (leaveRequestDto.NewOwnerUserId == null)
                {
                    ModelState.AddModelError(nameof(leaveRequestDto.NewOwnerUserId), "As the current owner, you must specify a teacher to become the new owner.");
                    return ValidationProblem(ModelState);
                }
                if (leaveRequestDto.NewOwnerUserId.Value == currentUserId)
                {
                    ModelState.AddModelError(nameof(leaveRequestDto.NewOwnerUserId), "You cannot transfer ownership to yourself.");
                    return ValidationProblem(ModelState);
                }
                var newOwnerProspect = teachersInClassroom.FirstOrDefault(t => t.UserId == leaveRequestDto.NewOwnerUserId.Value);
                if (newOwnerProspect == null)
                {
                    ModelState.AddModelError(nameof(leaveRequestDto.NewOwnerUserId), "The specified user is not a teacher in this classroom or does not exist.");
                    return ValidationProblem(ModelState);
                }
            }
           
            var strategy = _context.Database.CreateExecutionStrategy();
            try
            {
                return await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    var classroom = await _context.Classrooms
                        .Include(c => c.Members)
                        .FirstOrDefaultAsync(c => c.Id == classroomId);

                    if (classroom == null) throw new InvalidOperationException("Classroom disappeared mid-operation.");
                    var currentUserMembership = classroom.Members.FirstOrDefault(m => m.UserId == currentUserId);
                    if (currentUserMembership == null) throw new InvalidOperationException("User membership disappeared mid-operation.");

                    if (currentUserMembership.Role == ClassroomRole.Owner)
                    {
                        _logger.LogInformation("User {UserId} is Owner of classroom {ClassroomId}. Attempting owner transition within strategy.", currentUserId, classroomId);
                        var newOwnerMembership = classroom.Members.FirstOrDefault(t => t.UserId == leaveRequestDto.NewOwnerUserId!.Value);
                        if (newOwnerMembership == null || newOwnerMembership.Role != ClassroomRole.Teacher)
                        {
                            _logger.LogError("New owner candidate {NewOwnerId} is not a teacher or not found during transaction for classroom {ClassroomId}.", leaveRequestDto.NewOwnerUserId!.Value, classroomId);
                            throw new InvalidOperationException("Selected new owner is no longer a valid teacher.");
                        }

                        newOwnerMembership.Role = ClassroomRole.Owner;
                        _context.ClassroomMembers.Update(newOwnerMembership);
                        _logger.LogInformation("User {NewOwnerId} promoted to Owner for classroom {ClassroomId}.", newOwnerMembership.UserId, classroomId);

                        _context.ClassroomMembers.Remove(currentUserMembership);
                        _logger.LogInformation("Previous owner {OldOwnerId} removed from classroom {ClassroomId}.", currentUserMembership.UserId, classroomId);
                    }
                    else
                    {
                        _logger.LogInformation("User {UserId} (Role: {Role}) leaving classroom {ClassroomId} within strategy.", currentUserId, currentUserMembership.Role, classroomId);
                        _context.ClassroomMembers.Remove(currentUserMembership);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("User {UserId} successfully left/transferred ownership of classroom {ClassroomId}.", currentUserId, classroomId);
                   
                    return NoContent() as IActionResult;
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation during LeaveClassroom for classroom {ClassroomId} by user {UserId}.", classroomId, currentUserId);
                return BadRequest(new ProblemDetails { Title = "Operation Error", Detail = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during LeaveClassroom strategy execution for classroom {ClassroomId} by user {UserId}.", classroomId, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Operation Failed", Detail = "An error occurred while processing your request." });
            }
        }

        [HttpDelete("{classroomId}/members/{memberUserIdToRemove}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveMemberFromClassroom(int classroomId, int memberUserIdToRemove)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new ProblemDetails { Title = "Unauthorized", Detail = ex.Message }); }

            _logger.LogInformation("User {CurrentUserId} attempting to remove member {MemberUserIdToRemove} from classroom {ClassroomId}",
                currentUserId, memberUserIdToRemove, classroomId);

            var classroom = await _context.Classrooms.FindAsync(classroomId);
            if (classroom == null)
            {
                return NotFound(new ProblemDetails { Title = "Not Found", Detail = $"Classroom with ID {classroomId} not found." });
            }
           
            if (currentUserId == memberUserIdToRemove)
            {
                return BadRequest(new ProblemDetails { Title = "Invalid Operation", Detail = "Cannot remove yourself using this endpoint. Please use the 'leave classroom' functionality." });
            }
           
            var currentUserRoleInClassroom = await GetUserRoleInClassroom(currentUserId, classroomId);
            if (currentUserRoleInClassroom == null)
            {
                _logger.LogWarning("User {CurrentUserId} is not a member of classroom {ClassroomId} and cannot remove others.", currentUserId, classroomId);
                return Forbid();
            }
           
            var targetMembership = await _context.ClassroomMembers
                .FirstOrDefaultAsync(cm => cm.ClassroomId == classroomId && cm.UserId == memberUserIdToRemove);

            if (targetMembership == null)
            {
                return NotFound(new ProblemDetails { Title = "Not Found", Detail = $"Member with User ID {memberUserIdToRemove} not found in classroom {classroomId}." });
            }
           
            bool canRemove = false;
           
            if (currentUserRoleInClassroom == ClassroomRole.Owner)
            {
                if (targetMembership.Role == ClassroomRole.Teacher || targetMembership.Role == ClassroomRole.Student)
                {
                    canRemove = true;
                }
                else if (targetMembership.Role == ClassroomRole.Owner)
                {
                    return BadRequest(new ProblemDetails { Title = "Invalid Operation", Detail = "Owners cannot be removed using this endpoint. Ownership must be transferred or the classroom deleted by an owner." });
                }
            }
           
            else if (currentUserRoleInClassroom == ClassroomRole.Teacher)
            {
                if (targetMembership.Role == ClassroomRole.Student)
                {
                    canRemove = true;
                }
            }
           
            if (!canRemove)
            {
                _logger.LogWarning("User {CurrentUserId} (Role: {CurrentUserRole}) is not authorized to remove member {MemberUserIdToRemove} (Role: {TargetUserRole}) from classroom {ClassroomId}.",
                    currentUserId, currentUserRoleInClassroom, memberUserIdToRemove, targetMembership.Role, classroomId);
                return Forbid();
            }
           
            try
            {
                _context.ClassroomMembers.Remove(targetMembership);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Member {MemberUserIdToRemove} successfully removed from classroom {ClassroomId} by user {CurrentUserId}.",
                    memberUserIdToRemove, classroomId, currentUserId);
                return NoContent();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error while removing member {MemberUserIdToRemove} from classroom {ClassroomId}.", memberUserIdToRemove, classroomId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Database Error", Detail = "Could not remove member from the classroom." });
            }
        }
    }
}