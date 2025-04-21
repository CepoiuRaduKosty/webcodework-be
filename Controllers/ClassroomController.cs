// Controllers/ClassroomsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims; // Required for User.FindFirstValue
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;

namespace WebCodeWork.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // All endpoints in this controller require authentication
    public class ClassroomsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ClassroomsController> _logger;

        public ClassroomsController(ApplicationDbContext context, ILogger<ClassroomsController> logger)
        {
            _context = context;
            _logger = logger;
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

            // Basic check: Is user at least a member of this classroom?
            // More complex logic could be added (e.g., public classrooms)
            var isMember = await _context.ClassroomMembers
                                    .AnyAsync(cm => cm.ClassroomId == classroomId && cm.UserId == currentUserId);

            if (!isMember)
            {
                // Or check if user is admin, etc. For now, only members can view.
                 return Forbid(); // User is authenticated but not allowed to see this specific classroom
            }


            var classroom = await _context.Classrooms
                .AsNoTracking() // Read-only operation
                .Select(c => new ClassroomDto // Project to DTO
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    CreatedAt = c.CreatedAt
                })
                .FirstOrDefaultAsync(c => c.Id == classroomId);

            if (classroom == null)
            {
                return NotFound(new { message = "Classroom not found." });
            }

            return Ok(classroom);
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
    }
}