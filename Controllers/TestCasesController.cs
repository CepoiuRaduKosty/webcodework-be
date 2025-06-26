using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebCodeWork.Data;
using WebCodeWork.Dtos;
using WebCodeWork.Enums;
using WebCodeWork.Models;
using WebCodeWork.Services;
using System.IO;
using System.Text;

namespace WebCodeWork.Controllers
{
    [Route("api/testcases")]
    [ApiController]
    [Authorize]
    public class TestCasesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TestCasesController> _logger;
        private readonly IFileStorageService _fileService;

        public TestCasesController(ApplicationDbContext context, ILogger<TestCasesController> logger, IFileStorageService fileService)
        {
            _context = context;
            _logger = logger;
            _fileService = fileService;
        }
       
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId)) { throw new UnauthorizedAccessException("User ID not found in token."); }
            return userId;
        }
       
        private async Task<(TestCase? testCase, IActionResult? errorResult)> GetTestCaseAndVerifyAccess(int testCaseId, int currentUserId, bool trackEntity = false)
        {
            var query = _context.TestCases
                .Include(tc => tc.Assignment)
                    .ThenInclude(a => a!.Classroom)
                .AsQueryable();

            if (!trackEntity)
            {
                query = query.AsNoTracking();
            }

            var testCase = await query.FirstOrDefaultAsync(tc => tc.Id == testCaseId);

            if (testCase == null)
            {
                return (null, NotFound(new ProblemDetails { Title = "Not Found", Detail = "Test case not found." }));
            }
            if (testCase.Assignment == null)
            {
                 _logger.LogError("Assignment data missing for test case {TestCaseId}", testCaseId);
                 return (null, StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Internal Error", Detail = "Associated assignment data is missing."}));
            }
            if (!testCase.Assignment.IsCodeAssignment)
            {
                _logger.LogWarning("Attempt to access test case {TestCaseId} for non-code assignment {AssignmentId}", testCaseId, testCase.AssignmentId);
                return (null, BadRequest(new ProblemDetails { Title = "Invalid Operation", Detail = "Test cases are only applicable to code assignments."}));
            }

            var userRole = await _context.ClassroomMembers
                                 .Where(cm => cm.UserId == currentUserId && cm.ClassroomId == testCase.Assignment.ClassroomId)
                                 .Select(cm => (ClassroomRole?)cm.Role)
                                 .FirstOrDefaultAsync();

            if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher)
            {
                _logger.LogWarning("User {UserId} (Role: {UserRole}) forbidden from accessing/modifying test case {TestCaseId}.",
                    currentUserId, userRole?.ToString() ?? "None", testCaseId);
                return (null, Forbid());
            }

            return (testCase, null);
        }

        [HttpGet("{testCaseId}/input/content")]
        [Produces("text/plain")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTestCaseInputContent(int testCaseId)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new ProblemDetails { Title = "Unauthorized", Detail = ex.Message }); }

            _logger.LogDebug("User {UserId} attempting to get input content for test case {TestCaseId}", currentUserId, testCaseId);

            var testCase = await _context.TestCases
                .Include(tc => tc.Assignment)
                .AsNoTracking()
                .FirstOrDefaultAsync(tc => tc.Id == testCaseId);

            if (testCase == null)
            {
                return NotFound(new ProblemDetails { Title = "Not Found", Detail = "Test case not found." });
            }
            if (testCase.Assignment == null)
            {
                 _logger.LogError("Assignment data missing for test case {TestCaseId}", testCaseId);
                 return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Internal Error", Detail = "Associated assignment data is missing."});
            }
            if (!testCase.Assignment.IsCodeAssignment)
            {
                return BadRequest(new ProblemDetails { Title = "Invalid Assignment Type", Detail = "Test case content is only applicable to code assignments." });
            }

            var userRole = await _context.ClassroomMembers
                .Where(cm => cm.UserId == currentUserId && cm.ClassroomId == testCase.Assignment.ClassroomId)
                .Select(cm => (ClassroomRole?)cm.Role)
                .FirstOrDefaultAsync();

            bool canAccessContent = false;
            if (userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher)
            {
                canAccessContent = true;
            }
            else if (userRole == ClassroomRole.Student)
            {
                if (!testCase.IsPrivate)
                {
                    canAccessContent = true;
                }
                else
                {
                    _logger.LogWarning("Student {UserId} forbidden from accessing content of private test case {TestCaseId}.", currentUserId, testCaseId);
                }
            }

            if (!canAccessContent)
            {
                 _logger.LogWarning("User {UserId} (Role: {UserRole}) access denied for content of test case {TestCaseId} (IsPrivate: {IsPrivate}).",
                    currentUserId, userRole?.ToString() ?? "Not a member", testCaseId, testCase.IsPrivate);
                return Forbid();
            }

            var (fileStream, _, _) = await _fileService.GetTestCaseFileAsync(
                testCase.InputFilePath,
                testCase.InputStoredFileName,
                testCase.InputFileName
            );

            if (fileStream == null)
            {
                _logger.LogError("Input file for TestCase {TestCaseId} not found in storage. Path: {Path}, StoredName: {StoredName}",
                    testCaseId, testCase.InputFilePath, testCase.InputStoredFileName);
                return NotFound(new ProblemDetails { Title = "File Not Found", Detail = "Input file content not found in storage." });
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
                _logger.LogError(ex, "Error reading input content stream for test case {TestCaseId}", testCaseId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "File Read Error", Detail = "Error reading file content." });
            }

            return Content(fileContent, "text/plain", Encoding.UTF8);
        }

        [HttpGet("{testCaseId}/output/content")]
        [Produces("text/plain")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTestCaseOutputContent(int testCaseId)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new ProblemDetails { Title = "Unauthorized", Detail = ex.Message }); }

            _logger.LogDebug("User {UserId} attempting to get output content for test case {TestCaseId}", currentUserId, testCaseId);

            var testCase = await _context.TestCases
                .Include(tc => tc.Assignment)
                .AsNoTracking()
                .FirstOrDefaultAsync(tc => tc.Id == testCaseId);

            if (testCase == null)
                return NotFound(new ProblemDetails { Title = "Not Found", Detail = "Test case not found." });
            if (testCase.Assignment == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Internal Error", Detail = "Associated assignment data is missing."});
            if (!testCase.Assignment.IsCodeAssignment)
                return BadRequest(new ProblemDetails { Title = "Invalid Assignment Type", Detail = "Test case content is only applicable to code assignments." });

            var userRole = await _context.ClassroomMembers
                .Where(cm => cm.UserId == currentUserId && cm.ClassroomId == testCase.Assignment.ClassroomId)
                .Select(cm => (ClassroomRole?)cm.Role)
                .FirstOrDefaultAsync();

            bool canAccessContent = false;
            if (userRole == ClassroomRole.Owner || userRole == ClassroomRole.Teacher)
            {
                canAccessContent = true;
            }
            else if (userRole == ClassroomRole.Student)
            {
                if (!testCase.IsPrivate) canAccessContent = true;
                else _logger.LogWarning("Student {UserId} forbidden from accessing content of private test case {TestCaseId}.", currentUserId, testCaseId);
            }

            if (!canAccessContent)
            {
                 _logger.LogWarning("User {UserId} (Role: {UserRole}) access denied for content of test case {TestCaseId} (IsPrivate: {IsPrivate}).",
                    currentUserId, userRole?.ToString() ?? "Not a member", testCaseId, testCase.IsPrivate);
                return Forbid();
            }

            var (fileStream, _, _) = await _fileService.GetTestCaseFileAsync(
                testCase.ExpectedOutputFilePath,
                testCase.ExpectedOutputStoredFileName,
                testCase.ExpectedOutputFileName
            );

            if (fileStream == null)
            {
                 _logger.LogError("Output file for TestCase {TestCaseId} not found in storage. Path: {Path}, StoredName: {StoredName}",
                    testCaseId, testCase.ExpectedOutputFilePath, testCase.ExpectedOutputStoredFileName);
                return NotFound(new ProblemDetails { Title = "File Not Found", Detail = "Expected output file content not found in storage." });
            }

            string fileContent;
            try
            {
                using (var reader = new StreamReader(fileStream, Encoding.UTF8)) { fileContent = await reader.ReadToEndAsync(); }
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error reading output content stream for test case {TestCaseId}", testCaseId);
                 return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "File Read Error", Detail = "Error reading file content." });
            }

            return Content(fileContent, "text/plain", Encoding.UTF8);
        }

        [HttpPut("{testCaseId}/input/content")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTestCaseInputContent(int testCaseId)
        {
           
            string content;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                content = await reader.ReadToEndAsync();
            }

            if (content == null) return BadRequest(new { message = "Request body cannot be empty." });

            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

            var (testCase, errorResult) = await GetTestCaseAndVerifyAccess(testCaseId, currentUserId);
            if (errorResult != null) return errorResult;

            bool success = false;
            try { success = await _fileService.OverwriteSubmissionFileAsync(testCase!.InputFilePath, testCase.InputStoredFileName, content); }
            catch (Exception ex) { _logger.LogError(ex, "File service failed during input overwrite for test case {TestCaseId}.", testCaseId); }

            if (!success) return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to update input file content in storage." });
            return NoContent();
        }

        [HttpPut("{testCaseId}/output/content")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTestCaseOutputContent(int testCaseId)
        {
            string content;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                content = await reader.ReadToEndAsync();
            }

            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

            var (testCase, errorResult) = await GetTestCaseAndVerifyAccess(testCaseId, currentUserId);
            if (errorResult != null) return errorResult;

            bool success = false;
            try { success = await _fileService.OverwriteSubmissionFileAsync(testCase!.ExpectedOutputFilePath, testCase.ExpectedOutputStoredFileName, content); }
            catch (Exception ex) { _logger.LogError(ex, "File service failed during output overwrite for test case {TestCaseId}.", testCaseId); }

            if (!success) return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to update output file content in storage." });
            return NoContent();
        }

        [HttpPatch("{testCaseId}/privacy")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTestCasePrivacy(int testCaseId, [FromBody] UpdateTestCasePrivacyDto dto)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException) { return Unauthorized(); }

            _logger.LogInformation("User {UserId} attempting to update privacy for test case {TestCaseId} to IsPrivate={IsPrivate}",
                currentUserId, testCaseId, dto.IsPrivate);

           
            var (testCase, errorResult) = await GetTestCaseAndVerifyAccess(testCaseId, currentUserId, trackEntity: true);
            if (errorResult != null)
            {
                return errorResult;
            }

            if (testCase!.IsPrivate == dto.IsPrivate)
            {
                _logger.LogInformation("Privacy for test case {TestCaseId} already set to {IsPrivate}. No change made.", testCaseId, dto.IsPrivate);
                return NoContent();
            }

            testCase.IsPrivate = dto.IsPrivate;
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully updated privacy for test case {TestCaseId} to IsPrivate={IsPrivate} by User {UserId}",
                    testCaseId, dto.IsPrivate, currentUserId);
                return NoContent();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency error while updating privacy for test case {TestCaseId}.", testCaseId);
                return Conflict(new ProblemDetails { Title = "Conflict", Detail = "The test case was modified by another user. Please refresh and try again."});
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error while updating privacy for test case {TestCaseId}.", testCaseId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Database Error", Detail = "Could not update test case privacy." });
            }
        }
    }
}