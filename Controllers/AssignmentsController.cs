// Controllers/AssignmentsController.cs
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

    private async Task<bool> CanUserManageAssignmentTestCases(int userId, int assignmentId)
    {
        // Checks if user can manage test cases for the assignment (Owner/Teacher + IsCodeAssignment)
        var assignment = await _context.Assignments
                                      .AsNoTracking()
                                      .Select(a => new { a.Id, a.ClassroomId, a.IsCodeAssignment })
                                      .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment == null || !assignment.IsCodeAssignment) return false; // Must exist and be code assignment

        var userRole = await GetUserRoleInClassroom(userId, assignment.ClassroomId);
        return userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
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
            MaxPoints = dto.MaxPoints,
            IsCodeAssignment = dto.IsCodeAssignment
        };

        _context.Assignments.Add(assignment);
        await _context.SaveChangesAsync();

        // Map to response DTO (fetch creator username if needed)
        var createdBy = await _context.Users.FindAsync(currentUserId);
        var responseDto = new AssignmentDetailsDto
        {
            CreatedByUsername = createdBy?.Username ?? "N/A",
            CreatedById = currentUserId,
            ClassroomId = classroomId,
            Id = assignment.Id,
            Title = assignment.Title,
            CreatedAt = assignment.CreatedAt,
            DueDate = assignment.DueDate,
            MaxPoints = assignment.MaxPoints,
            IsCodeAssignment = assignment.IsCodeAssignment,
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
                MaxPoints = a.MaxPoints,
                IsCodeAssignment = a.IsCodeAssignment
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
            IsCodeAssignment = assignment.IsCodeAssignment,
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

        var assignment = await _context.Assignments
                                       .Include(a => a.CreatedBy) // Needed for response DTO
                                       .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment == null) return NotFound();

        // Auth Check: User must be Owner/Teacher in the classroom or original creator
        if (!await CanUserManageAssignment(currentUserId, assignmentId))
        {
            return Forbid();
        }

        // Prevent changing IsCodeAssignment if test cases exist? Optional check.
        if (assignment.IsCodeAssignment != dto.IsCodeAssignment && await _context.TestCases.AnyAsync(tc => tc.AssignmentId == assignmentId))
        {
            return BadRequest(new { message = "Cannot change the assignment type (code/non-code) when test cases already exist." });
        }

        // Update fields
        assignment.Title = dto.Title;
        assignment.Instructions = dto.Instructions;
        assignment.DueDate = dto.DueDate?.ToUniversalTime();
        assignment.MaxPoints = dto.MaxPoints;
        assignment.IsCodeAssignment = dto.IsCodeAssignment;

        _context.Entry(assignment).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        // Return updated details (similar to GetAssignmentDetails)
        var responseDto = new AssignmentDetailsDto
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
            IsCodeAssignment = assignment.IsCodeAssignment,
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
                                       .Include(a => a.TestCases) // <<-- Include test cases
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


        // --- Delete Associated Test Case Files ---
        _logger.LogInformation("Starting test case file cleanup for assignment {AssignmentId} deletion.", assignmentId);
        if (assignment.TestCases.Any())
        {
            foreach (var testCase in assignment.TestCases)
            {
                try { await _fileService.DeleteTestCaseFileAsync(testCase.InputFilePath, testCase.InputStoredFileName); }
                catch (Exception ex) { _logger.LogError(ex, "Error deleting test case input file {FileName} during assignment {AssignmentId} deletion.", testCase.InputFileName, assignmentId); }

                try { await _fileService.DeleteTestCaseFileAsync(testCase.ExpectedOutputFilePath, testCase.ExpectedOutputStoredFileName); }
                catch (Exception ex) { _logger.LogError(ex, "Error deleting test case output file {FileName} during assignment {AssignmentId} deletion.", testCase.ExpectedOutputFileName, assignmentId); }
            }
            _logger.LogInformation("Finished test case file cleanup attempt for assignment {AssignmentId}.", assignmentId);
        }
        else
        {
            _logger.LogInformation("No test case files to cleanup for assignment {AssignmentId}.", assignmentId);
        }

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

    // POST /api/assignments/{assignmentId}/testcases - Add Test Case Pair
    [HttpPost("assignments/{assignmentId}/testcases")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(TestCaseDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AddTestCase(int assignmentId, [FromForm] AddTestCaseDto dto)
    {
        var validationResult = dto.ValidateFilenames(new ValidationContext(dto));
        if (validationResult != ValidationResult.Success && validationResult != null) // Added null check for safety
        {
            ModelState.AddModelError(validationResult.MemberNames.FirstOrDefault() ?? string.Empty, validationResult.ErrorMessage!);
            return BadRequest(ModelState);
        }
        // ModelState will also catch the [Required] and [Range] for Points automatically

        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        if (!await CanUserManageAssignmentTestCases(currentUserId, assignmentId))
        {
            _logger.LogWarning("User {UserId} forbidden from adding test cases to assignment {AssignmentId}.", currentUserId, assignmentId);
            return Forbid();
        }

        string inputFileName = dto.InputFile?.FileName ?? dto.InputFileName!;
        string outputFileName = dto.OutputFile?.FileName ?? dto.OutputFileName!;

        string inputStoredName = string.Empty, inputPath = string.Empty;
        string outputStoredName = string.Empty, outputPath = string.Empty;
        bool inputSaved = false;

        try
        {
            if (dto.InputFile != null && dto.InputFile.Length > 0)
            {
                (inputStoredName, inputPath) = await _fileService.SaveTestCaseFileAsync(assignmentId, "input", dto.InputFile);
            }
            else
            {
                (inputStoredName, inputPath) = await _fileService.CreateEmptyFileAsync(assignmentId, inputFileName);
            }
            inputSaved = true;

            if (dto.OutputFile != null && dto.OutputFile.Length > 0)
            {
                (outputStoredName, outputPath) = await _fileService.SaveTestCaseFileAsync(assignmentId, "output", dto.OutputFile);
            }
            else
            {
                (outputStoredName, outputPath) = await _fileService.CreateEmptyFileAsync(assignmentId, outputFileName);
            }

            var testCase = new TestCase
            {
                AssignmentId = assignmentId,
                InputFileName = inputFileName,
                InputStoredFileName = inputStoredName,
                InputFilePath = inputPath,
                ExpectedOutputFileName = outputFileName,
                ExpectedOutputStoredFileName = outputStoredName,
                ExpectedOutputFilePath = outputPath,
                Points = dto.Points, // <<-- Save the points
                AddedAt = DateTime.UtcNow,
                AddedById = currentUserId
            };

            _context.TestCases.Add(testCase);
            await _context.SaveChangesAsync();

            // --- Map to DTO ---
            var addedBy = await _context.Users
                                      .AsNoTracking() // Read-only
                                      .FirstOrDefaultAsync(u => u.Id == currentUserId);

            var responseDto = new TestCaseDetailDto
            {
                Id = testCase.Id,
                InputFileName = testCase.InputFileName,
                ExpectedOutputFileName = testCase.ExpectedOutputFileName,
                Points = testCase.Points, // <<-- Include points in response
                AddedAt = testCase.AddedAt,
                AddedByUsername = addedBy?.Username ?? "N/A" // Handle if user somehow not found
            };
            // --- End Map to DTO ---

            // --- CreatedAtAction ---
            // For CreatedAtAction to work, you need a "Get" endpoint for a single test case.
            // Let's assume it would be in TestCasesController named "GetTestCaseById".
            // If you don't have it yet, returning StatusCode(201, dto) is fine.
            // For example:
            // return CreatedAtAction("GetTestCaseById", "TestCases", new { testCaseId = testCase.Id }, responseDto);
            // For now, if that endpoint doesn't exist:
            return StatusCode(StatusCodes.Status201Created, responseDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding test case for assignment {AssignmentId}", assignmentId);
            if (inputSaved && !string.IsNullOrEmpty(inputPath) && !string.IsNullOrEmpty(inputStoredName))
            {
                 // Best effort cleanup of already saved input file if subsequent steps fail
                 await _fileService.DeleteTestCaseFileAsync(inputPath, inputStoredName);
            }
            // Note: If output file was saved but DB failed, it won't be cleaned up here. More complex transaction management would be needed.
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while adding the test case." });
        }
    }

    // GET /api/assignments/{assignmentId}/testcases - List Test Cases
    [HttpGet("assignments/{assignmentId}/testcases")]
    [ProducesResponseType(typeof(IEnumerable<TestCaseListDto>), StatusCodes.Status200OK)]
    // ... other response types ...
    public async Task<IActionResult> GetTestCases(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        // Auth Check: Must be Owner/Teacher and IsCodeAssignment
        if (!await CanUserManageAssignmentTestCases(currentUserId, assignmentId))
        {
            // Need to differentiate between forbidden and assignment not found/not code assignment
            var assignmentExists = await _context.Assignments.AnyAsync(a => a.Id == assignmentId);
            if (!assignmentExists) return NotFound(new { message = "Assignment not found." });
            return Forbid();
        }

        var testCases = await _context.TestCases
           .Where(tc => tc.AssignmentId == assignmentId)
           .Include(tc => tc.AddedBy) // Include user for username
           .OrderBy(tc => tc.AddedAt) // Or by name?
           .Select(tc => new TestCaseListDto
           {
               Id = tc.Id,
               InputFileName = tc.InputFileName,
               ExpectedOutputFileName = tc.ExpectedOutputFileName,
               AddedAt = tc.AddedAt,
               AddedByUsername = tc.AddedBy.Username ?? "N/A"
           })
           .ToListAsync();

        return Ok(testCases);
    }

    // DELETE /api/assignments/{assignmentId}/testcases/{testCaseId} - Delete Test Case Pair
    [HttpDelete("assignments/{assignmentId}/testcases/{testCaseId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // ... other response types ...
    public async Task<IActionResult> DeleteTestCase(int assignmentId, int testCaseId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        // Auth Check: Must be Owner/Teacher and IsCodeAssignment
        if (!await CanUserManageAssignmentTestCases(currentUserId, assignmentId)) return Forbid();

        var testCase = await _context.TestCases.FirstOrDefaultAsync(tc => tc.Id == testCaseId && tc.AssignmentId == assignmentId);
        if (testCase == null) return NotFound(new { message = "Test case not found or does not belong to this assignment." });

        // Delete physical files first
        bool inputDeleted = false;
        bool outputDeleted = false;
        inputDeleted = await _fileService.DeleteTestCaseFileAsync(testCase.InputFilePath, testCase.InputStoredFileName);
        outputDeleted = await _fileService.DeleteTestCaseFileAsync(testCase.ExpectedOutputFilePath, testCase.ExpectedOutputStoredFileName);

        if (!inputDeleted || !outputDeleted)
        {
            _logger.LogWarning("Failed to delete one or both physical files for test case {TestCaseId}. DB record will still be removed.", testCaseId);
            // Decide if you want to stop here or continue with DB deletion
        }

        // Delete DB record
        _context.TestCases.Remove(testCase);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}