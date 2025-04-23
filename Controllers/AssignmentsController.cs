// Controllers/AssignmentsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;
using WebCodeWork.Services;

[Route("api/")] // Base route adjusted
[ApiController]
[Authorize]
public class AssignmentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AssignmentsController> _logger;
    private readonly IFileStorageService _fileService;

    public AssignmentsController(ApplicationDbContext context, ILogger<AssignmentsController> logger, IFileStorageService fileService)
    {
        _context = context;
        _logger = logger;
        _fileService = fileService;
    }

    // --- Helper Methods (similar to ClassroomsController, adapt as needed) ---
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
    private async Task<ClassroomRole?> GetUserRoleInClassroom(int userId, int classroomId)
    {
        var membership = await _context.ClassroomMembers
            .FirstOrDefaultAsync(cm => cm.UserId == userId && cm.ClassroomId == classroomId);

        return membership?.Role;
    }
    private async Task<bool> IsUserMemberOfClassroomByAssignment(int userId, int assignmentId)
    {
        // Check if user is member of the classroom associated with the assignment
        var assignment = await _context.Assignments
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment == null) return false;
        return await _context.ClassroomMembers
                             .AnyAsync(cm => cm.UserId == userId && cm.ClassroomId == assignment.ClassroomId);
    }
    private async Task<bool> CanUserManageAssignment(int userId, int assignmentId)
    {
        var assignment = await _context.Assignments
                                      .AsNoTracking()
                                      .Select(a => new { a.Id, a.ClassroomId, a.CreatedById })
                                      .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment == null) return false;

        var userRole = await GetUserRoleInClassroom(userId, assignment.ClassroomId);
        // Allow original creator OR Owner/Teacher of the classroom to manage
        return userId == assignment.CreatedById || userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
    }

    // --- Endpoints ---

    // POST /api/classrooms/{classroomId}/assignments - Create Assignment
    [HttpPost("classrooms/{classroomId}/assignments")]
    [ProducesResponseType(typeof(AssignmentDetailsDto), StatusCodes.Status201Created)]
    // ... other response types ...
    public async Task<IActionResult> CreateAssignment(int classroomId, [FromBody] CreateAssignmentDto dto)
    {
         if (!ModelState.IsValid) return BadRequest(ModelState);
         int currentUserId = GetCurrentUserId();

         // Auth Check: User must be Owner or Teacher in the classroom
         var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
         if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher)
         {
             return Forbid();
         }

         var assignment = new Assignment
         {
             ClassroomId = classroomId,
             Title = dto.Title,
             Instructions = dto.Instructions,
             CreatedById = currentUserId,
             CreatedAt = DateTime.UtcNow,
             DueDate = dto.DueDate?.ToUniversalTime(), // Ensure UTC
             MaxPoints = dto.MaxPoints
         };

         _context.Assignments.Add(assignment);
         await _context.SaveChangesAsync();

         // Map to response DTO (fetch creator username if needed)
         var createdBy = await _context.Users.FindAsync(currentUserId);
         var responseDto = new AssignmentDetailsDto { 
            CreatedByUsername = createdBy?.Username ?? "N/A",
            CreatedById = currentUserId,
            ClassroomId = classroomId,
            Id = assignment.Id,
            Title = assignment.Title,
            CreatedAt = assignment.CreatedAt,
            DueDate = assignment.DueDate,
            MaxPoints = assignment.MaxPoints
        };

         return CreatedAtAction(nameof(GetAssignmentDetails), new { assignmentId = assignment.Id }, responseDto);
    }

    // GET /api/classrooms/{classroomId}/assignments - List Assignments for Classroom
    [HttpGet("classrooms/{classroomId}/assignments")]
    [ProducesResponseType(typeof(IEnumerable<AssignmentBasicDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)] // Added in case classroom doesn't exist
    public async Task<IActionResult> GetAssignmentsForClassroom(int classroomId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        // Auth Check: User must be a member of the classroom
        var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
        if (userRole == null)
        {
             // Check if classroom exists at all before returning Forbid
             var classroomExists = await _context.Classrooms.AnyAsync(c => c.Id == classroomId);
             if (!classroomExists)
             {
                 return NotFound(new { message = $"Classroom with ID {classroomId} not found." });
             }
             _logger.LogWarning("User {UserId} forbidden from accessing assignments for classroom {ClassroomId}.", currentUserId, classroomId);
             return Forbid();
        }

        // --- Select and Map Assignment data ---
        var assignments = await _context.Assignments
            .Where(a => a.ClassroomId == classroomId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AssignmentBasicDto
            {
                // --- Mapping Start ---
                Id = a.Id,
                Title = a.Title,
                CreatedAt = a.CreatedAt,
                DueDate = a.DueDate,
                MaxPoints = a.MaxPoints
                // SubmissionStatus is intentionally NOT mapped here yet.
                // It will be populated below only if the user is a student.
                // --- Mapping End ---
            })
            .ToListAsync(); // Execute the query to get the initial list

        // --- Enhance DTO with submission status for students ---
         if (userRole == ClassroomRole.Student)
         {
             var assignmentIds = assignments.Select(a => a.Id).ToList();
             // Avoid fetching if there are no assignments
             if (assignmentIds.Any())
             {
                 // Fetch relevant submissions for this student efficiently
                 var submissions = await _context.AssignmentSubmissions
                     .Where(s => s.StudentId == currentUserId && assignmentIds.Contains(s.AssignmentId))
                     .Select(s => new // Select only needed fields for status calculation
                     {
                         s.AssignmentId,
                         s.SubmittedAt,
                         s.Grade,
                         s.IsLate
                     })
                     .ToDictionaryAsync(s => s.AssignmentId); // Efficient lookup by AssignmentId

                 // Populate the SubmissionStatus in the DTO list
                 foreach (var dto in assignments)
                 {
                     if (submissions.TryGetValue(dto.Id, out var submission))
                     {
                         // Submission record exists
                         if (submission.Grade.HasValue)
                         {
                             dto.SubmissionStatus = "Graded";
                         }
                         else if (submission.SubmittedAt.HasValue)
                         {
                             dto.SubmissionStatus = submission.IsLate ? "Submitted (Late)" : "Submitted";
                         }
                         else
                         {
                             // Submission record exists but SubmittedAt is null - implies student started (e.g., uploaded file) but didn't click "Turn In"
                             dto.SubmissionStatus = "In Progress";
                         }
                     }
                     else
                     {
                         // No submission record found for this assignment and student
                         dto.SubmissionStatus = "Not Submitted";
                     }
                 }
             }
         }
         // Teachers/Owners will not have the SubmissionStatus field populated (it remains null)

        return Ok(assignments);
    }

    // GET /api/assignments/{assignmentId} - Get Assignment Details
    [HttpGet("assignments/{assignmentId}")]
    [ProducesResponseType(typeof(AssignmentDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssignmentDetails(int assignmentId)
    {
        int currentUserId = GetCurrentUserId();

        // Fetch assignment including creator's username
        var assignment = await _context.Assignments
             .Include(a => a.CreatedBy) // Include the creator User object
             .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null) return NotFound();

        // Auth Check: User must be member of the classroom
        if (!await IsUserMemberOfClassroomByAssignment(currentUserId, assignmentId))
        {
            return Forbid();
        }

        // Map to DTO
         var dto = new AssignmentDetailsDto
         {
            Id = assignment.Id,
            Title = assignment.Title,
            Instructions = assignment.Instructions,
            CreatedAt = assignment.CreatedAt,
            DueDate = assignment.DueDate,
            MaxPoints = assignment.MaxPoints,
            CreatedById = assignment.CreatedById,
            CreatedByUsername = assignment.CreatedBy.Username, // Get username from included entity
            ClassroomId = assignment.ClassroomId,
            // SubmissionStatus could be added here too if needed for the detail view
         };
         return Ok(dto);
    }

    // PUT /api/assignments/{assignmentId} - Update Assignment
    [HttpPut("assignments/{assignmentId}")]
    [ProducesResponseType(typeof(AssignmentDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAssignment(int assignmentId, [FromBody] UpdateAssignmentDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        int currentUserId = GetCurrentUserId();

        var assignment = await _context.Assignments.FindAsync(assignmentId);
        if (assignment == null) return NotFound();

        // Auth Check: User must be Owner/Teacher in the classroom or original creator
        if (!await CanUserManageAssignment(currentUserId, assignmentId))
        {
            return Forbid();
        }

        // Update fields
        assignment.Title = dto.Title;
        assignment.Instructions = dto.Instructions;
        assignment.DueDate = dto.DueDate?.ToUniversalTime();
        assignment.MaxPoints = dto.MaxPoints;
        // Add audit fields like LastUpdatedAt if needed

        _context.Entry(assignment).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        // Return updated details (similar to GetAssignmentDetails)
        var responseDto = new AssignmentDetailsDto {
            Id = assignment.Id,
            Title = assignment.Title,
            Instructions = assignment.Instructions,
            CreatedAt = assignment.CreatedAt,
            DueDate = assignment.DueDate,
            MaxPoints = assignment.MaxPoints,
            CreatedById = assignment.CreatedById,
            CreatedByUsername = assignment.CreatedBy.Username, // Get username from included entity
            ClassroomId = assignment.ClassroomId,
        };
        return Ok(responseDto);
    }

    // DELETE /api/assignments/{assignmentId} - Delete Assignment
    [HttpDelete("assignments/{assignmentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)] // Added potential 400 for file deletion failure if desired
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAssignment(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); } // Simplified error handling

        // Use Include to potentially help track related entities if needed,
        // although we query SubmittedFiles separately later.
        // FindAsync doesn't support Include, so we use FirstOrDefaultAsync.
        var assignment = await _context.Assignments
                                      .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null)
        {
            return NotFound(new { message = $"Assignment with ID {assignmentId} not found." });
        }

        // Auth Check: User must be Owner/Teacher in the classroom or original creator
        if (!await CanUserManageAssignment(currentUserId, assignmentId))
        {
             _logger.LogWarning("User {UserId} forbidden from deleting assignment {AssignmentId}.", currentUserId, assignmentId);
             return Forbid();
        }

        // Handle file deletion from storage before deleting DB records

        _logger.LogInformation("Starting physical file cleanup for assignment {AssignmentId} deletion.", assignmentId);

        // 1. Query for all files associated with this assignment's submissions
        var filesToDelete = await _context.SubmittedFiles
            .Where(f => f.AssignmentSubmission.AssignmentId == assignmentId) // Filter via navigation property
            .Select(f => new { f.FilePath, f.StoredFileName }) // Select only needed info
            .ToListAsync(); // Get the list

        if (filesToDelete.Any())
        {
            _logger.LogInformation("Found {FileCount} files to delete for assignment {AssignmentId}.", filesToDelete.Count, assignmentId);
            bool allFilesDeletedSuccessfully = true;

            // 2. Loop and delete each file using the file service
            foreach (var fileInfo in filesToDelete)
            {
                try
                {
                    // Assuming FilePath stores the relative directory (e.g., "submissions/123")
                    // And StoredFileName stores the unique blob name (e.g., "guid.pdf")
                    bool deleted = await _fileService.DeleteSubmissionFileAsync(fileInfo.FilePath, fileInfo.StoredFileName);
                    if (!deleted)
                    {
                        // Log if the service indicated the file didn't exist or failed, but continue
                         _logger.LogWarning("Failed to delete or file not found in storage: Path='{FilePath}', Name='{StoredFileName}' during assignment {AssignmentId} deletion.",
                            fileInfo.FilePath, fileInfo.StoredFileName, assignmentId);
                        // Optionally set allFilesDeletedSuccessfully = false here if you want to return a different status later
                    }
                }
                catch (Exception ex)
                {
                    // Log unexpected errors during file deletion but continue cleanup
                    _logger.LogError(ex, "Error deleting file Path='{FilePath}', Name='{StoredFileName}' during assignment {AssignmentId} deletion.",
                       fileInfo.FilePath, fileInfo.StoredFileName, assignmentId);
                    allFilesDeletedSuccessfully = false; // Mark that at least one error occurred
                }
            }

            if (!allFilesDeletedSuccessfully)
            {
                 _logger.LogWarning("One or more files failed to delete during cleanup for assignment {AssignmentId}. Database record will still be removed.", assignmentId);
                // Optionally, you could return a specific status code like 400 Bad Request or 500 Internal Server Error here
                // if you want the API call to reflect the partial failure. Example:
                // return BadRequest(new { message = "Assignment deleted, but failed to clean up one or more associated files." });
                // However, proceeding to delete the DB record and returning 204 is also common for cleanup tasks.
            }
            else
            {
                 _logger.LogInformation("Successfully processed physical file cleanup for assignment {AssignmentId}.", assignmentId);
            }
        }
        else
        {
            _logger.LogInformation("No associated files found to delete for assignment {AssignmentId}.", assignmentId);
        }


        // 3. Remove the Assignment record from the database context
        // Cascade delete settings in DbContext should handle removing related
        // AssignmentSubmissions and SubmittedFiles records from the database.
        _context.Assignments.Remove(assignment);

        try
        {
            // 4. Save changes to the database
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully deleted assignment {AssignmentId} and associated DB records.", assignmentId);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error while deleting assignment {AssignmentId}.", assignmentId);
            // If file deletion succeeded but DB delete failed, you might be in an inconsistent state.
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to delete assignment from database after file cleanup." });
        }

        // 5. Return success
        return NoContent(); // Standard HTTP 204 No Content for successful DELETE
    }
}