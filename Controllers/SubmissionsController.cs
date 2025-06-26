// Controllers/SubmissionsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;
using WebCodeWork.Services;
using YourProjectName.Dtos;

[Route("api/")]
[ApiController]
[Authorize]
public class SubmissionsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubmissionsController> _logger;
    private readonly IFileStorageService _fileService;

    public SubmissionsController(ApplicationDbContext context, ILogger<SubmissionsController> logger, IFileStorageService fileService)
    {
        _context = context;
        _logger = logger;
        _fileService = fileService;
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
   
    private async Task<AssignmentSubmission?> FindOrCreateSubmission(int studentId, int assignmentId)
    {
        var assignment = await _context.Assignments.FindAsync(assignmentId);
        if (assignment == null) return null;

        var submission = await _context.AssignmentSubmissions
            .FirstOrDefaultAsync(s => s.StudentId == studentId && s.AssignmentId == assignmentId);

        if (submission == null)
        {
            var role = await GetUserRoleInClassroom(studentId, assignment.ClassroomId);
            if (role != ClassroomRole.Student) return null;

            submission = new AssignmentSubmission
            {
                AssignmentId = assignmentId,
                StudentId = studentId,
                IsLate = false
            };
            _context.AssignmentSubmissions.Add(submission);
        }
        return submission;
    }
   
    [HttpGet("assignments/{assignmentId}/submissions/my")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMySubmission(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }
        var submission = await _context.AssignmentSubmissions
            .Include(s => s.Assignment)
            .Include(s => s.Student)
            .Include(s => s.GradedBy)
            .Include(s => s.SubmittedFiles)
            .FirstOrDefaultAsync(s => s.StudentId == currentUserId && s.AssignmentId == assignmentId);
       
        var assignmentExists = await _context.Assignments.AnyAsync(a => a.Id == assignmentId);
        if (!assignmentExists)
        {
            return NotFound(new { message = "Assignment not found." });
        }

        int classroomId;
        if (submission != null)
        {
            classroomId = submission.Assignment.ClassroomId;
        }
        else
        {
            var assignment = await _context.Assignments.FindAsync(assignmentId);
            classroomId = assignment!.ClassroomId;
        }

        var role = await GetUserRoleInClassroom(currentUserId, classroomId);
        if (role != ClassroomRole.Student)
        {
            _logger.LogWarning("User {UserId} attempted to access submission for assignment {AssignmentId} but is not a Student in classroom {ClassroomId}.", currentUserId, assignmentId, classroomId);
            return Forbid();
        }

        if (submission == null)
        {
            return NotFound(new { message = "Submission not found. You have not started this assignment yet." });
        }

        var dto = new SubmissionDto
        {
            Id = submission.Id,
            AssignmentId = submission.AssignmentId,
            StudentId = submission.StudentId,
            StudentUsername = submission.Student?.Username ?? "N/A",
            SubmittedAt = submission.SubmittedAt,
            IsLate = submission.IsLate,
            Grade = submission.Grade,
            Feedback = submission.Feedback,
            GradedAt = submission.GradedAt,
            GradedById = submission.GradedById,
            GradedByUsername = submission.GradedBy?.Username,
            LastEvaluatedAt = submission.LastEvaluatedAt,
            LastEvaluationDetailsJson = submission.LastEvaluationDetailsJson,
            LastEvaluationOverallStatus = submission.LastEvaluationOverallStatus,
            LastEvaluationPointsObtained = submission.LastEvaluationPointsObtained,
            LastEvaluationTotalPossiblePoints = submission.LastEvaluationTotalPossiblePoints,
            SubmittedFiles = submission.SubmittedFiles.Select(file => new SubmittedFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
               
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = file.FileSize,
                UploadedAt = file.UploadedAt
            }).ToList()
        };
        return Ok(dto);
    }

    [HttpPost("assignments/{assignmentId}/submissions/my/files")]
    [ProducesResponseType(typeof(SubmittedFileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadSubmissionFile(int assignmentId, IFormFile file)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file uploaded or file is empty." });
        }

        var submission = await FindOrCreateSubmission(currentUserId, assignmentId);
        if (submission == null)
        {
            _logger.LogWarning("User {UserId} failed file upload: Not a student or assignment {AssignmentId} invalid.", currentUserId, assignmentId);
            return Forbid();
        }
       
        if (submission.Grade.HasValue)
        {
            return BadRequest(new { message = "Cannot upload files to a graded submission." });
        }

        string storedFileName;
        string relativePath;
        try
        {
            (storedFileName, relativePath) = await _fileService.SaveSubmissionFileAsync(submission.Id, file);
        }
        catch (Exception ex)
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
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedAt = DateTime.UtcNow
        };

        try
        {
            if (_context.Entry(submission).State == EntityState.Added)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created new submission record {SubmissionId} during file upload.", submission.Id);
            }

            _context.SubmittedFiles.Add(submittedFile);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Saved SubmittedFile record {FileId} for submission {SubmissionId}.", submittedFile.Id, submission.Id);
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error saving SubmittedFile record for submission {SubmissionId}.", submission.Id);
           
            await _fileService.DeleteSubmissionFileAsync(relativePath, storedFileName);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error saving file information." });
        }
       
        var dto = new SubmittedFileDto
        {
            Id = submittedFile.Id,
            FileName = submittedFile.FileName,
            ContentType = string.IsNullOrEmpty(submittedFile.ContentType) ? "application/octet-stream" : submittedFile.ContentType,
            FileSize = submittedFile.FileSize,
            UploadedAt = submittedFile.UploadedAt
        };
       
        return CreatedAtAction(
            nameof(DownloadSubmittedFile),
            new { submissionId = submission.Id, fileId = submittedFile.Id },
            dto
        );
    }
   
    [HttpDelete("submissions/{submissionId}/files/{fileId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteSubmissionFile(int submissionId, int fileId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var fileRecord = await _context.SubmittedFiles
                                      .Include(f => f.AssignmentSubmission)
                                      .FirstOrDefaultAsync(f => f.Id == fileId && f.AssignmentSubmissionId == submissionId);

        if (fileRecord == null)
        {
            _logger.LogWarning("File record not found for ID {FileId} on submission {SubmissionId}.", fileId, submissionId);
            return NotFound(new { message = "File record not found." });
        }
       
        if (fileRecord.AssignmentSubmission.StudentId != currentUserId)
        {
            _logger.LogWarning("User {UserId} forbidden from deleting file {FileId} on submission {SubmissionId} (not owner).", currentUserId, fileId, submissionId);
            return Forbid();
        }
       
        if (fileRecord.AssignmentSubmission.Grade.HasValue)
        {
            _logger.LogWarning("User {UserId} attempted to delete file {FileId} from graded submission {SubmissionId}.", currentUserId, fileId, submissionId);
            return BadRequest(new { message = "Cannot delete files from a submission that has already been graded." });
        }
       
        bool fileDeleted = false;
        try
        {
            fileDeleted = await _fileService.DeleteSubmissionFileAsync(fileRecord.FilePath, fileRecord.StoredFileName);
            if (!fileDeleted)
            {
                _logger.LogWarning("Physical file not found or failed to delete from storage: Path='{FilePath}', Name='{StoredFileName}' for file record {FileId}.",
                           fileRecord.FilePath, fileRecord.StoredFileName, fileId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting physical file from storage: Path='{FilePath}', Name='{StoredFileName}' for file record {FileId}.",
                      fileRecord.FilePath, fileRecord.StoredFileName, fileId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to delete file from storage." });
        }

        _context.SubmittedFiles.Remove(fileRecord);

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully deleted SubmittedFile record {FileId} for submission {SubmissionId}.", fileId, submissionId);
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error deleting SubmittedFile record {FileId}.", fileId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error removing file record." });
        }
        return NoContent();
    }

    [HttpPost("assignments/{assignmentId}/submissions/my/submit")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitAssignment(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }
        var submission = await FindOrCreateSubmission(currentUserId, assignmentId);
        if (submission == null)
        {
            var assignmentExists = await _context.Assignments.AnyAsync(a => a.Id == assignmentId);
            if (!assignmentExists) return NotFound(new { message = "Assignment not found." });
            _logger.LogWarning("User {UserId} forbidden from submitting assignment {AssignmentId} (not student or other issue).", currentUserId, assignmentId);
            return Forbid();
        }

        if (submission.SubmittedAt.HasValue)
        {
            _logger.LogWarning("User {UserId} attempted to re-submit assignment {AssignmentId} which was already submitted at {SubmittedAt}.", currentUserId, assignmentId, submission.SubmittedAt);
            return BadRequest(new { message = "Assignment has already been submitted." });
        }

        if (submission.Grade.HasValue)
        {
            _logger.LogWarning("User {UserId} attempted to submit assignment {AssignmentId} which has already been graded.", currentUserId, assignmentId);
            return BadRequest(new { message = "Cannot submit an assignment that has already been graded." });
        }

        var assignment = await _context.Assignments.FindAsync(assignmentId);
        if (assignment == null)
        {
            return NotFound(new { message = "Assignment details not found." });
        }

        submission.SubmittedAt = DateTime.UtcNow;
        submission.IsLate = assignment.DueDate.HasValue && submission.SubmittedAt > assignment.DueDate.Value;

        if (_context.Entry(submission).State != EntityState.Added)
        {
            _context.Entry(submission).State = EntityState.Modified;
        }
       
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

        var updatedSubmission = await _context.AssignmentSubmissions
            .Include(s => s.SubmittedFiles)
            .Include(s => s.Student)     
            .Include(s => s.GradedBy)    
            .FirstOrDefaultAsync(s => s.Id == submission.Id);

        if (updatedSubmission == null)
        {
            _logger.LogError("Failed to reload submission {SubmissionId} after saving.", submission.Id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to retrieve submission details after saving." });
        }

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
        return Ok(dto);
    }

    [HttpGet("assignments/{assignmentId}/submissions")]
    [ProducesResponseType(typeof(IEnumerable<TeacherSubmissionViewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubmissionsForAssignment(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var assignment = await _context.Assignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null)
        {
            return NotFound(new { message = "Assignment not found." });
        }
       
        var userRole = await GetUserRoleInClassroom(currentUserId, assignment.ClassroomId);
        if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher)
        {
            _logger.LogWarning("User {UserId} forbidden from viewing submissions for assignment {AssignmentId}.", currentUserId, assignmentId);
            return Forbid();
        }
       
        var studentsInClass = await _context.ClassroomMembers
            .Where(cm => cm.ClassroomId == assignment.ClassroomId && cm.Role == ClassroomRole.Student)
            .Include(cm => cm.User)
            .Select(cm => new { cm.UserId, cm.User.Username, profilePhotoUrl = _fileService.GetPublicUserProfilePhotoUrl(cm.User.ProfilePhotoPath!, cm.User.ProfilePhotoStoredName!) })
            .ToListAsync();
       
        var submissions = await _context.AssignmentSubmissions
            .Where(s => s.AssignmentId == assignmentId)
            .Include(s => s.SubmittedFiles)
            .Select(s => new
            {
                s.Id,
                s.StudentId,
                s.SubmittedAt,
                s.IsLate,
                s.Grade,
                s.LastEvaluatedAt,
                s.LastEvaluationDetailsJson,
                s.LastEvaluationOverallStatus,
                s.LastEvaluationPointsObtained,
                s.LastEvaluationTotalPossiblePoints,
                HasFiles = s.SubmittedFiles.Any()
            })
            .ToDictionaryAsync(s => s.StudentId);
       
        var result = new List<TeacherSubmissionViewDto>();

        foreach (var student in studentsInClass)
        {
            var studentView = new TeacherSubmissionViewDto
            {
                StudentId = student.UserId,
                StudentUsername = student.Username ?? "N/A",
                ProfilePhotoUrl = student.profilePhotoUrl,
            };
           
            if (submissions.TryGetValue(student.UserId, out var submissionInfo))
            {
                studentView.SubmissionId = submissionInfo.Id;
                studentView.SubmittedAt = submissionInfo.SubmittedAt;
                studentView.IsLate = submissionInfo.IsLate;
                studentView.Grade = submissionInfo.Grade;
                studentView.HasFiles = submissionInfo.HasFiles;
                studentView.LastEvaluatedAt = submissionInfo.LastEvaluatedAt;
                studentView.LastEvaluationDetailsJson = submissionInfo.LastEvaluationDetailsJson;
                studentView.LastEvaluationOverallStatus = submissionInfo.LastEvaluationOverallStatus;
                studentView.LastEvaluationPointsObtained = submissionInfo.LastEvaluationPointsObtained;
                studentView.LastEvaluationTotalPossiblePoints = submissionInfo.LastEvaluationTotalPossiblePoints;

                if (submissionInfo.Grade.HasValue)
                {
                    studentView.Status = "Graded";
                }
                else if (submissionInfo.SubmittedAt.HasValue)
                {
                    studentView.Status = submissionInfo.IsLate ? "Submitted (Late)" : "Submitted";
                }
                else
                {
                    studentView.Status = "In Progress";
                }
            }
            else
            {
                studentView.Status = "Not Submitted";
            }
            result.Add(studentView);
            Console.WriteLine("DEBUGG student " + studentView.Status);
        }

        result = result.OrderBy(s => s.StudentUsername).ToList();
        return Ok(result);
    }
   
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
            .Include(s => s.Assignment)
            .Include(s => s.Student)    
            .Include(s => s.GradedBy)   
            .Include(s => s.SubmittedFiles)
            .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission == null)
        {
            _logger.LogWarning("Submission details request failed: Submission {SubmissionId} not found.", submissionId);
            return NotFound(new { message = "Submission not found." });
        }
       
        var userRole = await GetUserRoleInClassroom(currentUserId, submission.Assignment.ClassroomId);
        bool isOwnerTeacher = userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
        bool isStudentOwner = submission.StudentId == currentUserId;

        if (!isOwnerTeacher && !isStudentOwner)
        {
            _logger.LogWarning("User {UserId} forbidden from viewing submission {SubmissionId} details.", currentUserId, submissionId);
            return Forbid();
        }
       
        var dto = new SubmissionDto
        {
            Id = submission.Id,
            AssignmentId = submission.AssignmentId,
            StudentId = submission.StudentId,
            StudentUsername = submission.Student?.Username ?? "N/A",
            SubmittedAt = submission.SubmittedAt,
            IsLate = submission.IsLate,
            Grade = submission.Grade,
            Feedback = submission.Feedback,
            GradedAt = submission.GradedAt,
            GradedById = submission.GradedById,
            GradedByUsername = submission.GradedBy?.Username,
            LastEvaluatedLanguage = submission.LastEvaluatedLanguage,
            LastEvaluatedAt = submission.LastEvaluatedAt,
            LastEvaluationDetailsJson = submission.LastEvaluationDetailsJson,
            LastEvaluationOverallStatus = submission.LastEvaluationOverallStatus,
            LastEvaluationPointsObtained = submission.LastEvaluationPointsObtained,
            LastEvaluationTotalPossiblePoints = submission.LastEvaluationTotalPossiblePoints,
            SubmittedFiles = submission.SubmittedFiles.Select(file => new SubmittedFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = file.FileSize,
                UploadedAt = file.UploadedAt
            }).ToList()
        };
        return Ok(dto);
    }

    [HttpGet("submissions/{submissionId}/files/{fileId}/download")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadSubmittedFile(int submissionId, int fileId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }

        var fileRecord = await _context.SubmittedFiles
            .Include(f => f.AssignmentSubmission)
                .ThenInclude(s => s!.Assignment)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.AssignmentSubmissionId == submissionId);

        if (fileRecord == null)
        {
            return NotFound(new ProblemDetails { Title = "File Not Found", Detail = "The requested file does not exist or is not part of this submission." });
        }
       
        bool isStudentOwner = fileRecord.AssignmentSubmission.StudentId == currentUserId;
        bool isPrivilegedUserInClassroom = false;

        if (!isStudentOwner)
        {
            var userRole = await GetUserRoleInClassroom(currentUserId, fileRecord.AssignmentSubmission.Assignment.ClassroomId);
            isPrivilegedUserInClassroom = userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
        }

        if (!isStudentOwner && !isPrivilegedUserInClassroom)
        {
            _logger.LogWarning("User {UserId} forbidden from downloading file {FileId} for submission {SubmissionId}.", currentUserId, fileId, submissionId);
            return Forbid();
        }

        var (fileStream, contentType, originalFileName) = await _fileService.GetSubmissionFileAsync(
            fileRecord.FilePath,      
            fileRecord.StoredFileName,
            fileRecord.FileName       
        );

        if (fileStream == null)
        {
            _logger.LogError("File {FileId} ({OriginalName}) found in DB but not in storage for submission {SubmissionId}.", fileId, originalFileName, submissionId);
            return NotFound(new ProblemDetails { Title = "File Not Found in Storage", Detail = "The file content could not be retrieved from storage." });
        }

        return File(fileStream, contentType ?? "application/octet-stream", originalFileName);
    }


    [HttpPut("submissions/{submissionId}/grade")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GradeSubmission(int submissionId, [FromBody] GradeSubmissionDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var submission = await _context.AssignmentSubmissions
           .Include(s => s.Assignment)
           .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission == null)
        {
            _logger.LogWarning("Grading failed: Submission {SubmissionId} not found.", submissionId);
            return NotFound(new { message = "Submission not found." });
        }

        var userRole = await GetUserRoleInClassroom(currentUserId, submission.Assignment.ClassroomId);
        if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher)
        {
            _logger.LogWarning("User {UserId} forbidden from grading submission {SubmissionId}.", currentUserId, submissionId);
            return Forbid();
        }

        submission.Grade = dto.Grade;
        submission.Feedback = dto.Feedback;
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

        var updatedSubmission = await _context.AssignmentSubmissions
             .Include(s => s.SubmittedFiles)
             .Include(s => s.Student)     
             .Include(s => s.GradedBy)    
             .FirstOrDefaultAsync(s => s.Id == submission.Id);

        if (updatedSubmission == null)
        {
            _logger.LogError("Failed to reload submission {SubmissionId} after grading.", submission.Id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to retrieve updated submission details after grading." });
        }

        var responseDto = new SubmissionDto
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

        return Ok(responseDto);
    }

    [HttpPost("assignments/{assignmentId}/submissions/my/create-file")]
    [ProducesResponseType(typeof(SubmittedFileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateVirtualFile(int assignmentId, [FromBody] CreateVirtualFileDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var submission = await FindOrCreateSubmission(currentUserId, assignmentId);
        if (submission == null)
        {
            var assignmentExists = await _context.Assignments.AnyAsync(a => a.Id == assignmentId);
            if (!assignmentExists) return NotFound(new { message = "Assignment not found." });
            _logger.LogWarning("User {UserId} forbidden from creating file for assignment {AssignmentId}.", currentUserId, assignmentId);
            return Forbid();
        }

        if (submission.Id == 0 && _context.Entry(submission).State == EntityState.Added)
        {
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error saving new submission record for user {UserId}, assignment {AssignmentId} before creating file.", currentUserId, assignmentId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error preparing submission." });
            }
        }

        bool fileExists = await _context.SubmittedFiles
            .AnyAsync(f => f.AssignmentSubmissionId == submission.Id && f.FileName == dto.FileName);

        if (fileExists)
        {
            _logger.LogWarning("User {UserId} attempted to create duplicate file '{FileName}' for submission {SubmissionId}.", currentUserId, dto.FileName, submission.Id);
            return Conflict(new { message = $"A file named '{dto.FileName}' already exists for this submission." });
        }

        string storedFileName;
        string relativePath;
        try
        {
            (storedFileName, relativePath) = await _fileService.CreateEmptyFileAsync(submission.Id, dto.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File storage service failed to create empty file '{FileName}' for submission {SubmissionId}", dto.FileName, submission.Id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while creating the file in storage." });
        }

        var submittedFile = new SubmittedFile
        {
            AssignmentSubmissionId = submission.Id,
            FileName = dto.FileName,
            StoredFileName = storedFileName,
            FilePath = relativePath,
            ContentType = "text/plain",
            FileSize = 0,
            UploadedAt = DateTime.UtcNow
        };

        _context.SubmittedFiles.Add(submittedFile);

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Saved SubmittedFile record {FileId} ('{FileName}') for submission {SubmissionId}.", submittedFile.Id, dto.FileName, submission.Id);
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error saving SubmittedFile record for '{FileName}', submission {SubmissionId}.", dto.FileName, submission.Id);
            await _fileService.DeleteSubmissionFileAsync(relativePath, storedFileName);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error saving file information." });
        }

        var responseDto = new SubmittedFileDto
        {
            Id = submittedFile.Id,
            FileName = submittedFile.FileName,
            ContentType = submittedFile.ContentType ?? "application/octet-stream",
            FileSize = submittedFile.FileSize,
            UploadedAt = submittedFile.UploadedAt
        };

        return CreatedAtAction(
            nameof(DownloadSubmittedFile),
            new { submissionId = submission.Id, fileId = submittedFile.Id },
            responseDto
        );
    }

    [HttpGet("submissions/{submissionId}/files/{fileId}/content")]
    [Produces("text/plain")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSubmittedFileContent(int submissionId, int fileId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var fileRecord = await _context.SubmittedFiles
            .Include(f => f.AssignmentSubmission)
                .ThenInclude(s => s!.Assignment)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.AssignmentSubmissionId == submissionId);

        if (fileRecord == null)
        {
            return NotFound(new { message = "File record not found." });
        }

        var userRole = await GetUserRoleInClassroom(currentUserId, fileRecord.AssignmentSubmission.Assignment.ClassroomId);
        bool isOwnerTeacher = userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
        bool isStudentOwner = fileRecord.AssignmentSubmission.StudentId == currentUserId;

        if (!isOwnerTeacher && !isStudentOwner)
        {
            _logger.LogWarning("User {UserId} forbidden from accessing content of file {FileId} on submission {SubmissionId}.", currentUserId, fileId, submissionId);
            return Forbid();
        }

        var (fileStream, _, _) = await _fileService.GetSubmissionFileAsync(
            fileRecord.FilePath,
            fileRecord.StoredFileName,
            fileRecord.FileName
        );

        if (fileStream == null)
        {
            _logger.LogError("File record {FileId} found in DB but file not found in storage: Path='{FilePath}', Name='{StoredFileName}'",
                       fileId, fileRecord.FilePath, fileRecord.StoredFileName);
           
            return NotFound(new { message = "File content not found in storage." });
        }

        string fileContent;
        try
        {
            using (var reader = new StreamReader(fileStream, Encoding.UTF8))
            {
                fileContent = await reader.ReadToEndAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading content stream for file {FileId}", fileId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error reading file content." });
        }

        return Content(fileContent, "text/plain", Encoding.UTF8);
    }

   
    [HttpPut("submissions/{submissionId}/files/{fileId}/content")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateSubmittedFileContent(int submissionId, int fileId)
    {
        string content;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            content = await reader.ReadToEndAsync();
        }

        if (content == null)
        {
            return BadRequest(new { message = "Request body cannot be empty." });
        }

        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        var fileRecord = await _context.SubmittedFiles
            .Include(f => f.AssignmentSubmission)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.AssignmentSubmissionId == submissionId);

        if (fileRecord == null)
        {
            return NotFound(new { message = "File record not found." });
        }

        if (fileRecord.AssignmentSubmission.StudentId != currentUserId)
        {
            _logger.LogWarning("User {UserId} forbidden from updating content of file {FileId} (not owner).", currentUserId, fileId);
            return Forbid();
        }

        if (fileRecord.AssignmentSubmission.Grade.HasValue)
        {
            _logger.LogWarning("User {UserId} attempted to update content of file {FileId} from graded submission {SubmissionId}.", currentUserId, fileId, submissionId);
            return BadRequest(new { message = "Cannot edit content of a graded submission." });
        }

        bool success = false;
        try
        {
            success = await _fileService.OverwriteSubmissionFileAsync(fileRecord.FilePath, fileRecord.StoredFileName, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File service failed during overwrite for file {FileId}.", fileId);
           
        }

        if (!success)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to update file content in storage." });
        }

        fileRecord.FileSize = Encoding.UTF8.GetByteCount(content);
        _context.Entry(fileRecord).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("User {UserId} successfully updated content for file {FileId}.", currentUserId, fileId);
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error updating SubmittedFile record {FileId} after content update.", fileId);
           
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error saving file metadata after update." });
        }

        return NoContent();
    }
    
    [HttpPost("submissions/{submissionId}/unsubmit")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UnsubmitStudentSubmission(int submissionId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new ProblemDetails { Title = "Unauthorized", Detail = ex.Message }); }

        _logger.LogInformation("User {UserId} attempting to unsubmit submission {SubmissionId}", currentUserId, submissionId);

        var submission = await _context.AssignmentSubmissions
           .Include(s => s.Assignment)
           .Include(s => s.Student)   
           .Include(s => s.SubmittedFiles)
           .FirstOrDefaultAsync(s => s.Id == submissionId);

        if (submission == null)
        {
            _logger.LogWarning("Unsubmit failed: Submission {SubmissionId} not found.", submissionId);
            return NotFound(new ProblemDetails { Title = "Submission Not Found", Detail = "The specified submission does not exist." });
        }

        if (submission.Assignment == null)
        {
            _logger.LogError("Critical: Assignment data missing for submission {SubmissionId} during unsubmit attempt.", submissionId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Internal Error", Detail = "Associated assignment data is missing."});
        }

        var userRole = await GetUserRoleInClassroom(currentUserId, submission.Assignment.ClassroomId);
        if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher)
        {
            _logger.LogWarning("User {UserId} (Role: {UserRole}) forbidden from unsubmitting submission {SubmissionId} for assignment {AssignmentId}.",
                currentUserId, userRole?.ToString() ?? "N/A", submissionId, submission.AssignmentId);
            return Forbid();
        }

        if (submission.SubmittedAt == null)
        {
            _logger.LogInformation("Submission {SubmissionId} is already in an 'in-progress' state (not turned in). No action taken.", submissionId);
            return BadRequest(new ProblemDetails { Title = "Invalid Operation", Detail = "This submission has not been turned in yet." });
        }

        submission.SubmittedAt = null;
        submission.IsLate = false;
        submission.Grade = null;
        submission.Feedback = null;
        submission.GradedAt = null;
        submission.GradedById = null;
        _context.Entry(submission).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("User {UserId} successfully unsubmitted submission {SubmissionId}.", currentUserId, submissionId);
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error while unsubmitting submission {SubmissionId} by user {UserId}.", submissionId, currentUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Database Error", Detail = "Could not save changes to the submission." });
        }

        var responseDto = new SubmissionDto
        {
            Id = submission.Id,
            AssignmentId = submission.AssignmentId,
            StudentId = submission.StudentId,
            StudentUsername = submission.Student?.Username ?? "N/A",
            SubmittedAt = submission.SubmittedAt,
            IsLate = submission.IsLate,          
            Grade = submission.Grade,            
            Feedback = submission.Feedback,        
            GradedAt = submission.GradedAt,          
            GradedById = submission.GradedById,      
            GradedByUsername = null,             
            SubmittedFiles = submission.SubmittedFiles.Select(file => new SubmittedFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = file.FileSize,
                UploadedAt = file.UploadedAt
            }).ToList()
        };

        return Ok(responseDto);
    }
}