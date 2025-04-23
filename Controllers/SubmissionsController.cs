// Controllers/SubmissionsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;
using WebCodeWork.Services;

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
    [ProducesResponseType(StatusCodes.Status404NotFound)] // If student hasn't started or assignment doesn't exist
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMySubmission(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        // Find submission, including files and related user info
        // Ensure Assignment is included if needed for auth check later or context
        var submission = await _context.AssignmentSubmissions
            .Include(s => s.Assignment) // Include Assignment for classroom check
            .Include(s => s.Student) // Include student info
            .Include(s => s.GradedBy) // Include grader info if exists (might be null)
            .Include(s => s.SubmittedFiles) // Include the list of files
            .FirstOrDefaultAsync(s => s.StudentId == currentUserId && s.AssignmentId == assignmentId);

        // Check if assignment exists first
        var assignmentExists = await _context.Assignments.AnyAsync(a => a.Id == assignmentId);
        if (!assignmentExists)
        {
            return NotFound(new { message = "Assignment not found." });
        }

        // Auth check: Is user a student in this class?
        // We need the ClassroomId, which we can get from the Assignment record associated with the potential submission
        // Or query it separately if submission is null
        int classroomId;
        if (submission != null)
        {
            classroomId = submission.Assignment.ClassroomId;
        }
        else
        {
            // If submission doesn't exist, find the assignment to get the classroomId
            var assignment = await _context.Assignments.FindAsync(assignmentId);
            // We already checked if assignment exists, so assignment should not be null here
            classroomId = assignment!.ClassroomId; // Use null-forgiving operator as we know it exists
        }

        var role = await GetUserRoleInClassroom(currentUserId, classroomId);
        if (role != ClassroomRole.Student)
        {
            // If the user is not a student in this class, they shouldn't see the submission status
            _logger.LogWarning("User {UserId} attempted to access submission for assignment {AssignmentId} but is not a Student in classroom {ClassroomId}.", currentUserId, assignmentId, classroomId);
            return Forbid(); // Return Forbidden if not a student
        }

        if (submission == null)
        {
            // It's confirmed the user IS a student, but they haven't created a submission record yet.
            // Return 404 Not Found for the submission itself.
            return NotFound(new { message = "Submission not found. You have not started this assignment yet." });
        }

        // --- Map to DTO Start ---
        var dto = new SubmissionDto
        {
            Id = submission.Id,
            AssignmentId = submission.AssignmentId,
            StudentId = submission.StudentId,
            // Ensure Student is not null (shouldn't be due to FK constraint, but good practice)
            StudentUsername = submission.Student?.Username ?? "N/A",
            SubmittedAt = submission.SubmittedAt,
            IsLate = submission.IsLate,
            Grade = submission.Grade,
            Feedback = submission.Feedback,
            GradedAt = submission.GradedAt,
            GradedById = submission.GradedById,
            // Use null-conditional operator for GradedBy as it's nullable
            GradedByUsername = submission.GradedBy?.Username, // Returns null if GradedBy is null
            SubmittedFiles = submission.SubmittedFiles.Select(file => new SubmittedFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
                // Provide a default content type if null/empty
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = file.FileSize,
                UploadedAt = file.UploadedAt
                // We intentionally don't include StoredFileName or FilePath in the DTO
                // unless needed for constructing download links directly on the client.
            }).ToList() // Convert the IEnumerable from Select to a List
        };
        // --- Map to DTO End ---

        return Ok(dto);
    }


    // Controllers/SubmissionsController.cs

// ... (using statements, constructor, other methods) ...

    // POST /api/assignments/{assignmentId}/submissions/my/files - Upload File
    [HttpPost("assignments/{assignmentId}/submissions/my/files")]
    [ProducesResponseType(typeof(SubmittedFileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] // For invalid file or validation errors
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)] // For file saving errors
    public async Task<IActionResult> UploadSubmissionFile(int assignmentId, IFormFile file)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file uploaded or file is empty." });
        }


        var submission = await FindOrCreateSubmission(currentUserId, assignmentId);
        // FindOrCreateSubmission returns null if user isn't student or assignment doesn't exist
        if (submission == null)
        {
            _logger.LogWarning("User {UserId} failed file upload: Not a student or assignment {AssignmentId} invalid.", currentUserId, assignmentId);
            // Return 403 if user is not student, 404 if assignment doesn't exist - could refine FindOrCreateSubmission
            return Forbid(); // Or NotFound() depending on the reason submission is null
        }

        //Check if submission is locked (e.g., already graded) - prevent uploads
        if (submission.Grade.HasValue)
        {
            return BadRequest(new { message = "Cannot upload files to a graded submission." });
        }

        string storedFileName;
        string relativePath;
        try
        {
            // Store the file using the injected service
            (storedFileName, relativePath) = await _fileService.SaveSubmissionFileAsync(submission.Id, file);
        }
        catch (Exception ex) // Catch exceptions during file saving
        {
            _logger.LogError(ex, "File storage service failed to save file for submission {SubmissionId}", submission.Id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while saving the file." });
        }


        var submittedFile = new SubmittedFile
        {
            AssignmentSubmissionId = submission.Id,
            FileName = file.FileName,
            StoredFileName = storedFileName,
            FilePath = relativePath,
            ContentType = file.ContentType, // Use content type provided by the browser/client
            FileSize = file.Length,
            UploadedAt = DateTime.UtcNow
        };

        try
        {
            // If FindOrCreateSubmission didn't save, save now to ensure submission ID exists
            if (_context.Entry(submission).State == EntityState.Added)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created new submission record {SubmissionId} during file upload.", submission.Id);
            }

            _context.SubmittedFiles.Add(submittedFile);
            await _context.SaveChangesAsync(); // Save the file record to get its ID
             _logger.LogInformation("Saved SubmittedFile record {FileId} for submission {SubmissionId}.", submittedFile.Id, submission.Id);
        }
        catch (DbUpdateException dbEx)
        {
             _logger.LogError(dbEx, "Database error saving SubmittedFile record for submission {SubmissionId}.", submission.Id);
             // Attempt to clean up the file that was just saved to storage
             await _fileService.DeleteSubmissionFileAsync(relativePath, storedFileName);
             return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error saving file information." });
        }


        // --- Map to DTO Start ---
        var dto = new SubmittedFileDto
        {
            Id = submittedFile.Id, // Use the ID generated after saving
            FileName = submittedFile.FileName,
            ContentType = string.IsNullOrEmpty(submittedFile.ContentType) ? "application/octet-stream" : submittedFile.ContentType,
            FileSize = submittedFile.FileSize,
            UploadedAt = submittedFile.UploadedAt
        };
        // --- Map to DTO End ---

        // Consider returning a URL for accessing the file later (requires a download endpoint)
        // The CreatedAtAction provides the location header for the download endpoint
        return CreatedAtAction(
            nameof(DownloadSubmittedFile), // Name of the download action method
            new { submissionId = submission.Id, fileId = submittedFile.Id }, // Route parameters for the download action
            dto // The response body (details of the created file)
        );
    }

    // DELETE /api/submissions/{submissionId}/files/{fileId} - Delete Uploaded File
    [HttpDelete("submissions/{submissionId}/files/{fileId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] // Added for locked submission
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)] // For file deletion errors
    public async Task<IActionResult> DeleteSubmissionFile(int submissionId, int fileId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var fileRecord = await _context.SubmittedFiles
                                      // Include submission to check owner and grade status
                                      .Include(f => f.AssignmentSubmission)
                                      .FirstOrDefaultAsync(f => f.Id == fileId && f.AssignmentSubmissionId == submissionId);

        if (fileRecord == null)
        {
             _logger.LogWarning("File record not found for ID {FileId} on submission {SubmissionId}.", fileId, submissionId);
            return NotFound(new { message = "File record not found." });
        }

        // Auth Check: User must be the owner of the submission
        // We access AssignmentSubmission safely because the query would have returned null if it didn't exist for the fileRecord
        if (fileRecord.AssignmentSubmission.StudentId != currentUserId)
        {
            _logger.LogWarning("User {UserId} forbidden from deleting file {FileId} on submission {SubmissionId} (not owner).", currentUserId, fileId, submissionId);
            return Forbid();
        }

        // --- TODO Implementation Start: Check if submission is locked (graded) ---
        if (fileRecord.AssignmentSubmission.Grade.HasValue)
        {
            _logger.LogWarning("User {UserId} attempted to delete file {FileId} from graded submission {SubmissionId}.", currentUserId, fileId, submissionId);
            // Prevent deletion if a grade has been assigned
            return BadRequest(new { message = "Cannot delete files from a submission that has already been graded." });
        }
        // --- TODO Implementation End ---

        // Delete physical file
        bool fileDeleted = false;
        try
        {
            fileDeleted = await _fileService.DeleteSubmissionFileAsync(fileRecord.FilePath, fileRecord.StoredFileName);
            if (!fileDeleted)
            {
                // Log warning but proceed to try and delete DB record if file wasn't found in storage
                 _logger.LogWarning("Physical file not found or failed to delete from storage: Path='{FilePath}', Name='{StoredFileName}' for file record {FileId}.",
                            fileRecord.FilePath, fileRecord.StoredFileName, fileId);
            }
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error deleting physical file from storage: Path='{FilePath}', Name='{StoredFileName}' for file record {FileId}.",
                       fileRecord.FilePath, fileRecord.StoredFileName, fileId);
             // Consider returning 500 Internal Server Error here if physical file deletion failure should stop the process
             return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to delete file from storage." });
        }


        // Delete DB record
        _context.SubmittedFiles.Remove(fileRecord);

        try
        {
             await _context.SaveChangesAsync();
             _logger.LogInformation("Successfully deleted SubmittedFile record {FileId} for submission {SubmissionId}.", fileId, submissionId);
        }
         catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error deleting SubmittedFile record {FileId}.", fileId);
             // If physical file was deleted but DB fails, we have an orphan file reference potentially.
             // This is harder to recover from automatically.
             return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error removing file record." });
        }

        return NoContent(); // Success
    }


    // POST /api/assignments/{assignmentId}/submissions/my/submit - Mark Assignment as Done (Turn In)
    [HttpPost("assignments/{assignmentId}/submissions/my/submit")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] // For already submitted/graded
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)] // If assignment not found
    public async Task<IActionResult> SubmitAssignment(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        // FindOrCreateSubmission ensures user is student and assignment exists, or returns null
        var submission = await FindOrCreateSubmission(currentUserId, assignmentId);
        if (submission == null)
        {
            // Check if assignment exists to return NotFound, otherwise assume Forbidden
            var assignmentExists = await _context.Assignments.AnyAsync(a => a.Id == assignmentId);
             if (!assignmentExists) return NotFound(new { message = "Assignment not found." });
            _logger.LogWarning("User {UserId} forbidden from submitting assignment {AssignmentId} (not student or other issue).", currentUserId, assignmentId);
            return Forbid();
        }

        // --- TODO Implementation Start: Check if already submitted or locked ---
        if (submission.SubmittedAt.HasValue)
        {
            _logger.LogWarning("User {UserId} attempted to re-submit assignment {AssignmentId} which was already submitted at {SubmittedAt}.", currentUserId, assignmentId, submission.SubmittedAt);
            // Already submitted - potentially allow "unsubmit" later, but for now, prevent re-submit.
            return BadRequest(new { message = "Assignment has already been submitted." });
        }

        if (submission.Grade.HasValue)
        {
            _logger.LogWarning("User {UserId} attempted to submit assignment {AssignmentId} which has already been graded.", currentUserId, assignmentId);
            // Already graded - cannot submit after grading.
            return BadRequest(new { message = "Cannot submit an assignment that has already been graded." });
        }
        // --- TODO Implementation End ---


        // Fetch assignment separately IF needed for DueDate check,
        // FindOrCreateSubmission doesn't include it by default.
        // Alternatively modify FindOrCreateSubmission to include it if needed often.
        var assignment = await _context.Assignments.FindAsync(assignmentId);
        // Assignment should exist if submission was found/created, but check defensively
        if (assignment == null)
        {
            // This case is less likely if FindOrCreate worked, but good practice
            return NotFound(new { message = "Assignment details not found." });
        }


        // Set submission details
        submission.SubmittedAt = DateTime.UtcNow;
        submission.IsLate = assignment.DueDate.HasValue && submission.SubmittedAt > assignment.DueDate.Value;

        // Mark the entity as modified if it wasn't just added
        if (_context.Entry(submission).State != EntityState.Added)
        {
             _context.Entry(submission).State = EntityState.Modified;
        }
        // Save changes (handles both new submission creation and modification)
        try
        {
            await _context.SaveChangesAsync();
             _logger.LogInformation("User {UserId} successfully submitted assignment {AssignmentId} for submission {SubmissionId}. IsLate: {IsLate}",
                currentUserId, assignmentId, submission.Id, submission.IsLate);
        }
        catch (DbUpdateException dbEx)
        {
             _logger.LogError(dbEx, "Database error while submitting assignment {AssignmentId} for user {UserId}.", assignmentId, currentUserId);
             return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error saving submission." });
        }


        // Reload data to return the full, updated DTO state
        // (Could also map manually but reloading ensures consistency)
        var updatedSubmission = await _context.AssignmentSubmissions
            .Include(s => s.SubmittedFiles) // Include files
            .Include(s => s.Student)      // Include student info
            .Include(s => s.GradedBy)     // Include grader info (likely null here)
            .FirstOrDefaultAsync(s => s.Id == submission.Id); // Fetch by the now-guaranteed ID

        if(updatedSubmission == null) // Should not happen, but defensive check
        {
             _logger.LogError("Failed to reload submission {SubmissionId} after saving.", submission.Id);
             return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to retrieve submission details after saving." });
        }

        // --- Map to DTO Start (from previous example) ---
        var dto = new SubmissionDto
        {
            Id = updatedSubmission.Id,
            AssignmentId = updatedSubmission.AssignmentId,
            StudentId = updatedSubmission.StudentId,
            StudentUsername = updatedSubmission.Student?.Username ?? "N/A",
            SubmittedAt = updatedSubmission.SubmittedAt,
            IsLate = updatedSubmission.IsLate,
            Grade = updatedSubmission.Grade,
            Feedback = updatedSubmission.Feedback,
            GradedAt = updatedSubmission.GradedAt,
            GradedById = updatedSubmission.GradedById,
            GradedByUsername = updatedSubmission.GradedBy?.Username,
            SubmittedFiles = updatedSubmission.SubmittedFiles.Select(file => new SubmittedFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = file.FileSize,
                UploadedAt = file.UploadedAt
            }).ToList()
        };
        // --- Map to DTO End ---

        return Ok(dto); // Return the updated submission state
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubmissionDetails(int submissionId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var submission = await _context.AssignmentSubmissions
            .Include(s => s.Assignment) // Need assignment for classroom ID
            .Include(s => s.Student)     // Need student for username & owner check
            .Include(s => s.GradedBy)    // Need grader for username
            .Include(s => s.SubmittedFiles) // Need files list
            .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission == null)
        {
             _logger.LogWarning("Submission details request failed: Submission {SubmissionId} not found.", submissionId);
            return NotFound(new { message = "Submission not found." });
        }

        // Auth Check: Must be Owner/Teacher of the class OR the student owner
        var userRole = await GetUserRoleInClassroom(currentUserId, submission.Assignment.ClassroomId);
        bool isOwnerTeacher = userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
        bool isStudentOwner = submission.StudentId == currentUserId;

        if (!isOwnerTeacher && !isStudentOwner)
        {
             _logger.LogWarning("User {UserId} forbidden from viewing submission {SubmissionId} details.", currentUserId, submissionId);
            return Forbid();
        }

        // --- Map to DTO Start ---
        var dto = new SubmissionDto
        {
            Id = submission.Id,
            AssignmentId = submission.AssignmentId,
            StudentId = submission.StudentId,
            StudentUsername = submission.Student?.Username ?? "N/A", // Null check just in case
            SubmittedAt = submission.SubmittedAt,
            IsLate = submission.IsLate,
            Grade = submission.Grade,
            Feedback = submission.Feedback,
            GradedAt = submission.GradedAt,
            GradedById = submission.GradedById,
            GradedByUsername = submission.GradedBy?.Username, // Null-conditional access
            SubmittedFiles = submission.SubmittedFiles.Select(file => new SubmittedFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = file.FileSize,
                UploadedAt = file.UploadedAt
            }).ToList()
        };
        // --- Map to DTO End ---

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

        if (fileStream == null) return NotFound(new { message = "File not found in storage." });

        return File(fileStream, contentType ?? "application/octet-stream", downloadName);
    }


    [HttpPut("submissions/{submissionId}/grade")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)] // For validation errors
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)] // For DB errors
    public async Task<IActionResult> GradeSubmission(int submissionId, [FromBody] GradeSubmissionDto dto)
    {
        if (!ModelState.IsValid)
        {
             // Return validation errors if DTO constraints aren't met
             // (e.g., if Grade had range attributes)
             return BadRequest(ModelState);
        }

        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var submission = await _context.AssignmentSubmissions
           .Include(s => s.Assignment) // Need assignment for classroom check
           .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission == null)
        {
             _logger.LogWarning("Grading failed: Submission {SubmissionId} not found.", submissionId);
            return NotFound(new { message = "Submission not found." });
        }

        // Auth Check: Must be Owner/Teacher
        var userRole = await GetUserRoleInClassroom(currentUserId, submission.Assignment.ClassroomId);
        if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher)
        {
             _logger.LogWarning("User {UserId} forbidden from grading submission {SubmissionId}.", currentUserId, submissionId);
            return Forbid();
        }

        // --- Update grade/feedback ---
        // Only update if values are provided in the DTO, allowing partial updates
        // However, the current GradeSubmissionDto forces both to be present or null.
        // If partial updates were desired, the DTO properties would be nullable.
        submission.Grade = dto.Grade; // Assigns null if dto.Grade is null
        submission.Feedback = dto.Feedback; // Assigns null if dto.Feedback is null

        // Update grading timestamp and grader ID only if a grade or feedback was actually provided
        // Or always update if grading action implies interaction? Let's update always.
        submission.GradedAt = DateTime.UtcNow;
        submission.GradedById = currentUserId;

        _context.Entry(submission).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
             _logger.LogInformation("User {UserId} successfully graded submission {SubmissionId}. Grade: {Grade}, Feedback: {FeedbackProvided}",
                currentUserId, submissionId, dto.Grade?.ToString() ?? "N/A", !string.IsNullOrEmpty(dto.Feedback));
        }
         catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error while grading submission {SubmissionId} by user {UserId}.", submissionId, currentUserId);
             return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error saving grade." });
        }


        // Return updated submission details by re-fetching
        var updatedSubmission = await _context.AssignmentSubmissions
             .Include(s => s.SubmittedFiles) // Include files list
             .Include(s => s.Student)      // Include student for username
             .Include(s => s.GradedBy)     // Include grader for username (should be the current user now)
             .FirstOrDefaultAsync(s => s.Id == submission.Id); // Fetch by ID

        // Defensive check in case fetch fails immediately after save (highly unlikely)
        if(updatedSubmission == null)
        {
             _logger.LogError("Failed to reload submission {SubmissionId} after grading.", submission.Id);
             return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to retrieve updated submission details after grading." });
        }

        // --- Map to DTO Start ---
        var responseDto = new SubmissionDto
        {
            Id = updatedSubmission.Id,
            AssignmentId = updatedSubmission.AssignmentId,
            StudentId = updatedSubmission.StudentId,
            StudentUsername = updatedSubmission.Student?.Username ?? "N/A",
            SubmittedAt = updatedSubmission.SubmittedAt,
            IsLate = updatedSubmission.IsLate,
            Grade = updatedSubmission.Grade, // The grade just set
            Feedback = updatedSubmission.Feedback, // The feedback just set
            GradedAt = updatedSubmission.GradedAt, // The timestamp just set
            GradedById = updatedSubmission.GradedById, // The ID just set (currentUserId)
            GradedByUsername = updatedSubmission.GradedBy?.Username, // Should resolve to current user's name
            SubmittedFiles = updatedSubmission.SubmittedFiles.Select(file => new SubmittedFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = file.FileSize,
                UploadedAt = file.UploadedAt
            }).ToList()
        };
        // --- Map to DTO End ---

        return Ok(responseDto); // Return the full updated state
    }
}