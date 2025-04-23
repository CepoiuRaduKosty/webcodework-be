// Controllers/SubmissionsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebCodeWork.Data;
using YourProjectName.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;
using WebCodeWork.Services; // Assuming IFileStorageService lives here

[Route("api/")]
[ApiController]
[Authorize]
public class SubmissionsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubmissionsController> _logger;
    private readonly IFileStorageService _fileService; // Inject file service

    public SubmissionsController(ApplicationDbContext context, ILogger<SubmissionsController> logger, IFileStorageService fileService)
    {
        _context = context;
        _logger = logger;
        _fileService = fileService;
    }

    // --- Helper Methods ---
    private int GetCurrentUserId() { /* ... implementation ... */ }
     private async Task<ClassroomRole?> GetUserRoleInClassroom(int userId, int classroomId) { /* ... implementation ... */ }
    // Helper to find or create a submission record for a student+assignment
    private async Task<AssignmentSubmission?> FindOrCreateSubmission(int studentId, int assignmentId)
    {
        var assignment = await _context.Assignments.FindAsync(assignmentId);
        if (assignment == null) return null; // Assignment doesn't exist

        var submission = await _context.AssignmentSubmissions
            .FirstOrDefaultAsync(s => s.StudentId == studentId && s.AssignmentId == assignmentId);

        if (submission == null)
        {
            // Check if user is actually a student in this classroom
            var role = await GetUserRoleInClassroom(studentId, assignment.ClassroomId);
            if (role != ClassroomRole.Student) return null; // Or throw exception/return error

            submission = new AssignmentSubmission
            {
                AssignmentId = assignmentId,
                StudentId = studentId,
                IsLate = false // Initial state
            };
            _context.AssignmentSubmissions.Add(submission);
            // SaveChangesAsync might be needed here OR before returning, depending on usage
            // Let's assume SaveChanges happens *after* this method in the calling action
        }
        return submission;
    }


    // --- Endpoints ---

    // GET /api/assignments/{assignmentId}/submissions/my - Get My Submission
    [HttpGet("assignments/{assignmentId}/submissions/my")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)] // If student hasn't started
     // ... other response types ...
    public async Task<IActionResult> GetMySubmission(int assignmentId)
    {
         int currentUserId = GetCurrentUserId();

         // Find submission, including files and related user info
         var submission = await _context.AssignmentSubmissions
             .Include(s => s.Student) // Include student info
             .Include(s => s.GradedBy) // Include grader info if exists
             .Include(s => s.SubmittedFiles) // Include the list of files
             .FirstOrDefaultAsync(s => s.StudentId == currentUserId && s.AssignmentId == assignmentId);

        // Even if submission doesn't exist yet, we might want to return info about the assignment
        var assignment = await _context.Assignments.FindAsync(assignmentId);
        if (assignment == null) return NotFound(new { message = "Assignment not found." });

        // Auth check: Is user a student in this class?
         var role = await GetUserRoleInClassroom(currentUserId, assignment.ClassroomId);
         if (role != ClassroomRole.Student) return Forbid();

        if (submission == null)
        {
            // Return 200 OK but indicate no submission exists yet, maybe with assignment info?
            // Or return 404? Let's return 404 for simplicity.
             return NotFound(new { message = "Submission not found. You have not started this assignment yet." });
        }

        // Map to DTO
        var dto = new SubmissionDto { /* ... map fields, including files ... */ };
        return Ok(dto);
    }


    // POST /api/assignments/{assignmentId}/submissions/my/files - Upload File
    [HttpPost("assignments/{assignmentId}/submissions/my/files")]
    [ProducesResponseType(typeof(SubmittedFileDto), StatusCodes.Status201Created)]
    // Add response type for validation errors (e.g., file size)
    // ... other response types ...
    public async Task<IActionResult> UploadSubmissionFile(int assignmentId, IFormFile file)
    {
         int currentUserId = GetCurrentUserId();

         if (file == null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });

        // TODO: Add file size and type validation based on configuration/rules

        var submission = await FindOrCreateSubmission(currentUserId, assignmentId);
         if (submission == null) return Forbid(); // User not student or assignment invalid

        // TODO: Check if submission is locked (e.g., already graded) - prevent uploads

        // Store the file using the injected service
        var (storedFileName, relativePath) = await _fileService.SaveSubmissionFileAsync(submission.Id, file);

        var submittedFile = new SubmittedFile
        {
            AssignmentSubmissionId = submission.Id,
            FileName = file.FileName,
            StoredFileName = storedFileName,
            FilePath = relativePath,
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedAt = DateTime.UtcNow
        };

         // If FindOrCreateSubmission didn't save, save now
         if (_context.Entry(submission).State == EntityState.Added) await _context.SaveChangesAsync();

         _context.SubmittedFiles.Add(submittedFile);
         await _context.SaveChangesAsync(); // Save the file record

        var dto = new SubmittedFileDto { /* ... map fields ... */ };
        // Consider returning a URL for accessing the file later
         return CreatedAtAction(nameof(DownloadSubmittedFile), new { submissionId = submission.Id, fileId = submittedFile.Id }, dto);
    }

    // DELETE /api/submissions/{submissionId}/files/{fileId} - Delete Uploaded File
    [HttpDelete("submissions/{submissionId}/files/{fileId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
     // ... other response types ...
    public async Task<IActionResult> DeleteSubmissionFile(int submissionId, int fileId)
    {
        int currentUserId = GetCurrentUserId();

        var fileRecord = await _context.SubmittedFiles
                                      .Include(f => f.AssignmentSubmission) // Need submission to check owner
                                      .FirstOrDefaultAsync(f => f.Id == fileId && f.AssignmentSubmissionId == submissionId);

        if (fileRecord == null) return NotFound();

        // Auth Check: User must be the owner of the submission
        if (fileRecord.AssignmentSubmission.StudentId != currentUserId) return Forbid();

         // TODO: Check if submission is locked

         // Delete physical file
         await _fileService.DeleteSubmissionFileAsync(fileRecord.FilePath, fileRecord.StoredFileName);

        // Delete DB record
        _context.SubmittedFiles.Remove(fileRecord);
        await _context.SaveChangesAsync();

        return NoContent();
    }


    // POST /api/assignments/{assignmentId}/submissions/my/submit - Mark Assignment as Done (Turn In)
    [HttpPost("assignments/{assignmentId}/submissions/my/submit")]
     [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
     // ... other response types ...
    public async Task<IActionResult> SubmitAssignment(int assignmentId)
    {
         int currentUserId = GetCurrentUserId();

        var submission = await FindOrCreateSubmission(currentUserId, assignmentId);
         if (submission == null) return Forbid(); // User not student or assignment invalid

         // TODO: Check if already submitted or locked

         var assignment = await _context.Assignments.FindAsync(assignmentId); // Fetch assignment for DueDate

        submission.SubmittedAt = DateTime.UtcNow;
        submission.IsLate = assignment?.DueDate != null && submission.SubmittedAt > assignment.DueDate.Value;

        // Save changes if needed
         if (_context.Entry(submission).State != EntityState.Unchanged)
         {
             await _context.SaveChangesAsync();
         }

         // Reload data or map existing data to return updated state
         var updatedSubmission = await _context.AssignmentSubmissions
             .Include(s=> s.SubmittedFiles)
             .Include(s => s.Student)
             .Include(s => s.GradedBy)
             .FirstOrDefaultAsync(s => s.Id == submission.Id);

         var dto = new SubmissionDto{ /* ... map fields ... */ };
         return Ok(dto);
    }


    // --- Teacher/Owner Endpoints ---

    // GET /api/assignments/{assignmentId}/submissions - List All Submissions for Assignment
    [HttpGet("assignments/{assignmentId}/submissions")]
    [ProducesResponseType(typeof(IEnumerable<SubmissionSummaryDto>), StatusCodes.Status200OK)]
     // ... other response types ...
    public async Task<IActionResult> GetSubmissionsForAssignment(int assignmentId)
    {
        int currentUserId = GetCurrentUserId();

        var assignment = await _context.Assignments.FindAsync(assignmentId);
        if (assignment == null) return NotFound();

        // Auth Check: Must be Owner/Teacher
        var userRole = await GetUserRoleInClassroom(currentUserId, assignment.ClassroomId);
         if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher) return Forbid();

         var submissions = await _context.AssignmentSubmissions
             .Where(s => s.AssignmentId == assignmentId)
             .Include(s => s.Student) // Include student for username
             .Select(s => new SubmissionSummaryDto // Project to summary DTO
             {
                 Id = s.Id,
                 StudentId = s.StudentId,
                 StudentUsername = s.Student.Username,
                 SubmittedAt = s.SubmittedAt,
                 IsLate = s.IsLate,
                 Grade = s.Grade,
                 HasFiles = s.SubmittedFiles.Any() // Check if files exist efficiently
             })
             .ToListAsync();

         return Ok(submissions);
    }

    // GET /api/submissions/{submissionId} - Get Specific Submission Details (Teacher/Student View)
     [HttpGet("submissions/{submissionId}")]
     [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
     // ... other response types ...
     public async Task<IActionResult> GetSubmissionDetails(int submissionId)
     {
         int currentUserId = GetCurrentUserId();

         var submission = await _context.AssignmentSubmissions
             .Include(s => s.Assignment) // Need assignment for classroom ID
             .Include(s => s.Student)
             .Include(s => s.GradedBy)
             .Include(s => s.SubmittedFiles)
             .FirstOrDefaultAsync(s => s.Id == submissionId);

         if (submission == null) return NotFound();

        // Auth Check: Must be Owner/Teacher of the class OR the student owner
        var userRole = await GetUserRoleInClassroom(currentUserId, submission.Assignment.ClassroomId);
         bool isOwnerTeacher = userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
         bool isStudentOwner = submission.StudentId == currentUserId;

         if (!isOwnerTeacher && !isStudentOwner) return Forbid();

         var dto = new SubmissionDto{ /* ... map fields ... */ };
         return Ok(dto);
     }

    // GET /api/submissions/{submissionId}/files/{fileId} - Download a Submitted File
    [HttpGet("submissions/{submissionId}/files/{fileId}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
     // ... other response types ...
    public async Task<IActionResult> DownloadSubmittedFile(int submissionId, int fileId)
    {
        int currentUserId = GetCurrentUserId();

        var fileRecord = await _context.SubmittedFiles
                                      .Include(f => f.AssignmentSubmission).ThenInclude(s => s!.Assignment) // Need nested include
                                      .FirstOrDefaultAsync(f => f.Id == fileId && f.AssignmentSubmissionId == submissionId);

        if (fileRecord == null) return NotFound();

        // Auth Check: Owner/Teacher of class OR student owner
        var userRole = await GetUserRoleInClassroom(currentUserId, fileRecord.AssignmentSubmission.Assignment.ClassroomId);
        bool isOwnerTeacher = userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
        bool isStudentOwner = fileRecord.AssignmentSubmission.StudentId == currentUserId;

        if (!isOwnerTeacher && !isStudentOwner) return Forbid();

        // Get file stream from storage service
        var (fileStream, contentType, downloadName) = await _fileService.GetSubmissionFileAsync(
            fileRecord.FilePath,
            fileRecord.StoredFileName,
            fileRecord.FileName // Pass original name for download header
        );

        if (fileStream == null) return NotFound(new { message = "File not found in storage."});

        return File(fileStream, contentType ?? "application/octet-stream", downloadName);
    }


    // PUT /api/submissions/{submissionId}/grade - Grade a Submission
    [HttpPut("submissions/{submissionId}/grade")]
     [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
     // ... other response types ...
    public async Task<IActionResult> GradeSubmission(int submissionId, [FromBody] GradeSubmissionDto dto)
    {
         if (!ModelState.IsValid) return BadRequest(ModelState);
         int currentUserId = GetCurrentUserId();

         var submission = await _context.AssignmentSubmissions
            .Include(s => s.Assignment) // Need assignment for classroom check
            .FirstOrDefaultAsync(s => s.Id == submissionId);

         if (submission == null) return NotFound();

         // Auth Check: Must be Owner/Teacher
         var userRole = await GetUserRoleInClassroom(currentUserId, submission.Assignment.ClassroomId);
         if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher) return Forbid();

         // Update grade/feedback
         submission.Grade = dto.Grade;
         submission.Feedback = dto.Feedback;
         submission.GradedAt = DateTime.UtcNow;
         submission.GradedById = currentUserId;

         _context.Entry(submission).State = EntityState.Modified;
         await _context.SaveChangesAsync();

        // Return updated submission details
        var updatedSubmission = await _context.AssignmentSubmissions
             .Include(s=> s.SubmittedFiles)
             .Include(s => s.Student)
             .Include(s => s.GradedBy)
             .FirstOrDefaultAsync(s => s.Id == submission.Id);
         var responseDto = new SubmissionDto{ /* ... map fields ... */ };
         return Ok(responseDto);
    }
}