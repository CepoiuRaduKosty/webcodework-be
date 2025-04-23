// Controllers/AssignmentsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;
// Add other necessary using statements

[Route("api/")] // Base route adjusted
[ApiController]
[Authorize]
public class AssignmentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AssignmentsController> _logger;
    // Inject file service if needed: private readonly IFileStorageService _fileService;

    public AssignmentsController(ApplicationDbContext context, ILogger<AssignmentsController> logger)
    {
        _context = context;
        _logger = logger;
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
         var responseDto = new AssignmentDetailsDto { /* ... map fields ... */ CreatedByUsername = createdBy?.Username ?? "N/A" };

         return CreatedAtAction(nameof(GetAssignmentDetails), new { assignmentId = assignment.Id }, responseDto);
    }

    // GET /api/classrooms/{classroomId}/assignments - List Assignments for Classroom
    [HttpGet("classrooms/{classroomId}/assignments")]
    [ProducesResponseType(typeof(IEnumerable<AssignmentBasicDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAssignmentsForClassroom(int classroomId)
    {
        int currentUserId = GetCurrentUserId();

        // Auth Check: User must be a member of the classroom
        var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
        if (userRole == null) return Forbid();

        var assignments = await _context.Assignments
            .Where(a => a.ClassroomId == classroomId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AssignmentBasicDto { /* ... map fields ... */ })
            .ToListAsync();

        // Optional: Enhance DTO with submission status for the current user if they are a student
         if (userRole == ClassroomRole.Student)
         {
             var assignmentIds = assignments.Select(a => a.Id).ToList();
             var submissions = await _context.AssignmentSubmissions
                 .Where(s => s.StudentId == currentUserId && assignmentIds.Contains(s.AssignmentId))
                 .ToDictionaryAsync(s => s.AssignmentId); // Efficient lookup

             foreach (var dto in assignments)
             {
                 if (submissions.TryGetValue(dto.Id, out var submission))
                 {
                     if (submission.Grade.HasValue) dto.SubmissionStatus = "Graded";
                     else if (submission.SubmittedAt.HasValue) dto.SubmissionStatus = submission.IsLate ? "Submitted (Late)" : "Submitted";
                     else dto.SubmissionStatus = "In Progress"; // Student started but didn't submit
                 }
                 else
                 {
                     dto.SubmissionStatus = "Not Submitted";
                 }
             }
         }


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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAssignment(int assignmentId)
    {
        int currentUserId = GetCurrentUserId();

        var assignment = await _context.Assignments.FindAsync(assignmentId);
        if (assignment == null) return NotFound();

        // Auth Check: User must be Owner/Teacher in the classroom or original creator
        if (!await CanUserManageAssignment(currentUserId, assignmentId))
        {
             return Forbid();
        }

        // TODO: Handle file deletion from storage if files exist in submissions
        // This requires iterating through submissions and files, calling file service

        _context.Assignments.Remove(assignment); // Cascade delete should handle submissions/files in DB
        await _context.SaveChangesAsync();

        return NoContent();
    }
}