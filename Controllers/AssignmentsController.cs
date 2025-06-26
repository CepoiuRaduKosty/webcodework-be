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

[Route("api/")]
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
    private async Task<bool> IsUserMemberOfClassroomByAssignment(int userId, int assignmentId)
    {
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
        return userId == assignment.CreatedById || userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
    }

    private async Task<bool> CanUserManageAssignmentTestCases(int userId, int assignmentId)
    {
        var assignment = await _context.Assignments
                                      .AsNoTracking()
                                      .Select(a => new { a.Id, a.ClassroomId, a.IsCodeAssignment })
                                      .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment == null || !assignment.IsCodeAssignment) return false;
        var userRole = await GetUserRoleInClassroom(userId, assignment.ClassroomId);
        return userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher;
    }
   
    [HttpPost("classrooms/{classroomId}/assignments")]
    [ProducesResponseType(typeof(AssignmentDetailsDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAssignment(int classroomId, [FromBody] CreateAssignmentDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        int currentUserId = GetCurrentUserId();

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
            DueDate = dto.DueDate?.ToUniversalTime(),
            MaxPoints = dto.MaxPoints,
            IsCodeAssignment = dto.IsCodeAssignment
        };

        _context.Assignments.Add(assignment);
        await _context.SaveChangesAsync();

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

   
    [HttpGet("classrooms/{classroomId}/assignments")]
    [ProducesResponseType(typeof(IEnumerable<AssignmentBasicDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssignmentsForClassroom(int classroomId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }
       
        var userRole = await GetUserRoleInClassroom(currentUserId, classroomId);
        if (userRole == null)
        {
            var classroomExists = await _context.Classrooms.AnyAsync(c => c.Id == classroomId);
            if (!classroomExists)
            {
                return NotFound(new { message = $"Classroom with ID {classroomId} not found." });
            }
            _logger.LogWarning("User {UserId} forbidden from accessing assignments for classroom {ClassroomId}.", currentUserId, classroomId);
            return Forbid();
        }
       
        var assignments = await _context.Assignments
            .Where(a => a.ClassroomId == classroomId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AssignmentBasicDto
            {
                Id = a.Id,
                Title = a.Title,
                CreatedAt = a.CreatedAt,
                DueDate = a.DueDate,
                MaxPoints = a.MaxPoints,
                IsCodeAssignment = a.IsCodeAssignment
            })
            .ToListAsync();
       
        if (userRole == ClassroomRole.Student)
        {
            var assignmentIds = assignments.Select(a => a.Id).ToList();
            if (assignmentIds.Any())
            {
                var submissions = await _context.AssignmentSubmissions
                    .Where(s => s.StudentId == currentUserId && assignmentIds.Contains(s.AssignmentId))
                    .Select(s => new
                    {
                        s.AssignmentId,
                        s.SubmittedAt,
                        s.Grade,
                        s.IsLate
                    })
                    .ToDictionaryAsync(s => s.AssignmentId);

                foreach (var dto in assignments)
                {
                    if (submissions.TryGetValue(dto.Id, out var submission))
                    {
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
                            dto.SubmissionStatus = "In Progress";
                        }
                    }
                    else
                    {
                        dto.SubmissionStatus = "Not Submitted";
                    }
                }
            }
        }
        return Ok(assignments);
    }

   
    [HttpGet("assignments/{assignmentId}")]
    [ProducesResponseType(typeof(AssignmentDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssignmentDetails(int assignmentId)
    {
        int currentUserId = GetCurrentUserId();
        var assignment = await _context.Assignments
             .Include(a => a.CreatedBy)
             .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null) return NotFound();
        if (!await IsUserMemberOfClassroomByAssignment(currentUserId, assignmentId))
        {
            return Forbid();
        }
       
        var dto = new AssignmentDetailsDto
        {
            Id = assignment.Id,
            Title = assignment.Title,
            Instructions = assignment.Instructions,
            CreatedAt = assignment.CreatedAt,
            DueDate = assignment.DueDate,
            MaxPoints = assignment.MaxPoints,
            CreatedById = assignment.CreatedById,
            CreatedByUsername = assignment.CreatedBy.Username,
            ClassroomId = assignment.ClassroomId,
            IsCodeAssignment = assignment.IsCodeAssignment,
        };
        return Ok(dto);
    }

   
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
                                       .Include(a => a.CreatedBy)
                                       .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment == null) return NotFound();

        if (!await CanUserManageAssignment(currentUserId, assignmentId))
        {
            return Forbid();
        }
       
        assignment.Title = dto.Title;
        assignment.Instructions = dto.Instructions;
        assignment.DueDate = dto.DueDate?.ToUniversalTime();
        assignment.MaxPoints = dto.MaxPoints;

        _context.Entry(assignment).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        var responseDto = new AssignmentDetailsDto
        {
            Id = assignment.Id,
            Title = assignment.Title,
            Instructions = assignment.Instructions,
            CreatedAt = assignment.CreatedAt,
            DueDate = assignment.DueDate,
            MaxPoints = assignment.MaxPoints,
            CreatedById = assignment.CreatedById,
            CreatedByUsername = assignment.CreatedBy.Username,
            ClassroomId = assignment.ClassroomId,
            IsCodeAssignment = assignment.IsCodeAssignment,
        };
        return Ok(responseDto);
    }
   
    [HttpDelete("assignments/{assignmentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAssignment(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }
       
        var assignment = await _context.Assignments
                                       .Include(a => a.TestCases)
                                       .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null)
        {
            return NotFound(new { message = $"Assignment with ID {assignmentId} not found." });
        }
       
        if (!await CanUserManageAssignment(currentUserId, assignmentId))
        {
            _logger.LogWarning("User {UserId} forbidden from deleting assignment {AssignmentId}.", currentUserId, assignmentId);
            return Forbid();
        }
       
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
       
        var filesToDelete = await _context.SubmittedFiles
            .Where(f => f.AssignmentSubmission.AssignmentId == assignmentId)
            .Select(f => new { f.FilePath, f.StoredFileName })
            .ToListAsync();

        if (filesToDelete.Any())
        {
            _logger.LogInformation("Found {FileCount} files to delete for assignment {AssignmentId}.", filesToDelete.Count, assignmentId);
            bool allFilesDeletedSuccessfully = true;
           
            foreach (var fileInfo in filesToDelete)
            {
                try
                {
                    bool deleted = await _fileService.DeleteSubmissionFileAsync(fileInfo.FilePath, fileInfo.StoredFileName);
                    if (!deleted)
                    {
                        _logger.LogWarning("Failed to delete or file not found in storage: Path='{FilePath}', Name='{StoredFileName}' during assignment {AssignmentId} deletion.",
                           fileInfo.FilePath, fileInfo.StoredFileName, assignmentId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting file Path='{FilePath}', Name='{StoredFileName}' during assignment {AssignmentId} deletion.",
                       fileInfo.FilePath, fileInfo.StoredFileName, assignmentId);
                    allFilesDeletedSuccessfully = false;
                }
            }

            if (!allFilesDeletedSuccessfully)
            {
                _logger.LogWarning("One or more files failed to delete during cleanup for assignment {AssignmentId}. Database record will still be removed.", assignmentId);
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

        _context.Assignments.Remove(assignment);

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully deleted assignment {AssignmentId} and associated DB records.", assignmentId);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error while deleting assignment {AssignmentId}.", assignmentId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to delete assignment from database after file cleanup." });
        }

        return NoContent();
    }

   
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
        if (validationResult != ValidationResult.Success && validationResult != null)
        {
            ModelState.AddModelError(validationResult.MemberNames.FirstOrDefault() ?? string.Empty, validationResult.ErrorMessage!);
            return BadRequest(ModelState);
        }
       
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

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
                Points = dto.Points,
                MaxExecutionTimeMs = dto.MaxExecutionTimeMs,
                MaxRamMB = dto.MaxRamMB,
                AddedAt = DateTime.UtcNow,
                AddedById = currentUserId,
                IsPrivate = dto.IsPrivate,
            };

            _context.TestCases.Add(testCase);
            await _context.SaveChangesAsync();

           
            var addedBy = await _context.Users
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(u => u.Id == currentUserId);

            var responseDto = new TestCaseDetailDto
            {
                Id = testCase.Id,
                InputFileName = testCase.InputFileName,
                ExpectedOutputFileName = testCase.ExpectedOutputFileName,
                Points = testCase.Points,
                MaxExecutionTimeMs = testCase.MaxExecutionTimeMs,
                MaxRamMB = testCase.MaxRamMB,
                AddedAt = testCase.AddedAt,
                AddedByUsername = addedBy?.Username ?? "N/A",
                IsPrivate = testCase.IsPrivate,
            };
            return StatusCode(StatusCodes.Status201Created, responseDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding test case for assignment {AssignmentId}", assignmentId);
            if (inputSaved && !string.IsNullOrEmpty(inputPath) && !string.IsNullOrEmpty(inputStoredName))
            {
                await _fileService.DeleteTestCaseFileAsync(inputPath, inputStoredName);
            }
           
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while adding the test case." });
        }
    }
   
    [HttpGet("assignments/{assignmentId}/testcases")]
    [ProducesResponseType(typeof(IEnumerable<TestCaseListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTestCases(int assignmentId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new ProblemDetails { Title = "Unauthorized", Detail = ex.Message }); }

        _logger.LogInformation("User {UserId} attempting to get test cases for assignment {AssignmentId}", currentUserId, assignmentId);
       
        var assignment = await _context.Assignments
                                      .AsNoTracking()
                                      .Select(a => new { a.Id, a.ClassroomId, a.IsCodeAssignment })
                                      .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = "Assignment not found." });
        }

        if (!assignment.IsCodeAssignment)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid Assignment Type", Detail = "Test cases are only applicable to code assignments." });
        }

        var userMembership = await _context.ClassroomMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(cm => cm.UserId == currentUserId && cm.ClassroomId == assignment.ClassroomId);

        if (userMembership == null)
        {
            _logger.LogWarning("User {UserId} is not a member of classroom {ClassroomId} and cannot view test cases for assignment {AssignmentId}.",
                currentUserId, assignment.ClassroomId, assignmentId);
            return Forbid();
        }

        var userRoleInClassroom = userMembership.Role;
       
        IQueryable<TestCase> query = _context.TestCases
           .AsNoTracking()
           .Where(tc => tc.AssignmentId == assignmentId);

        if (userRoleInClassroom == ClassroomRole.Student)
        {
            _logger.LogInformation("User {UserId} is a Student. Filtering for public test cases for assignment {AssignmentId}.", currentUserId, assignmentId);
            query = query.Where(tc => !tc.IsPrivate);
        }
        else if (userRoleInClassroom == ClassroomRole.Owner || userRoleInClassroom == ClassroomRole.Teacher)
        {
            _logger.LogInformation("User {UserId} is an Owner/Teacher. Fetching all test cases for assignment {AssignmentId}.", currentUserId, assignmentId);
           
        }
        else
        {
            _logger.LogWarning("User {UserId} has an unrecognized role ({UserRole}) in classroom {ClassroomId}. Denying access to test cases for assignment {AssignmentId}.",
                currentUserId, userRoleInClassroom, assignment.ClassroomId, assignmentId);
            return Forbid();
        }

        var testCases = await query
           .Include(tc => tc.AddedBy)
           .OrderBy(tc => tc.AddedAt)
           .Select(tc => new TestCaseListDto
           {
               Id = tc.Id,
               InputFileName = tc.InputFileName,
               ExpectedOutputFileName = tc.ExpectedOutputFileName,
               AddedAt = tc.AddedAt,
               AddedByUsername = tc.AddedBy.Username ?? "N/A",
               Points = tc.Points,
               MaxExecutionTimeMs = tc.MaxExecutionTimeMs,
               MaxRamMB = tc.MaxRamMB,
               IsPrivate = tc.IsPrivate
           })
           .ToListAsync();

        _logger.LogInformation("Returning {Count} test cases for assignment {AssignmentId} to user {UserId} with role {UserRole}.",
            testCases.Count, assignmentId, currentUserId, userRoleInClassroom);

        return Ok(testCases);
    }

   
    [HttpDelete("assignments/{assignmentId}/testcases/{testCaseId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
   
    public async Task<IActionResult> DeleteTestCase(int assignmentId, int testCaseId)
    {
        int currentUserId;
        try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

        if (!await CanUserManageAssignmentTestCases(currentUserId, assignmentId)) return Forbid();

        var testCase = await _context.TestCases.FirstOrDefaultAsync(tc => tc.Id == testCaseId && tc.AssignmentId == assignmentId);
        if (testCase == null) return NotFound(new { message = "Test case not found or does not belong to this assignment." });
       
        bool inputDeleted = false;
        bool outputDeleted = false;
        inputDeleted = await _fileService.DeleteTestCaseFileAsync(testCase.InputFilePath, testCase.InputStoredFileName);
        outputDeleted = await _fileService.DeleteTestCaseFileAsync(testCase.ExpectedOutputFilePath, testCase.ExpectedOutputStoredFileName);

        if (!inputDeleted || !outputDeleted)
        {
            _logger.LogWarning("Failed to delete one or both physical files for test case {TestCaseId}. DB record will still be removed.", testCaseId);
        }
       
        _context.TestCases.Remove(testCase);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}