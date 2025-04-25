// Controllers/TestCasesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebCodeWork.Data;
using WebCodeWork.Dtos; // If needed for responses
using WebCodeWork.Enums;
using WebCodeWork.Models;
using WebCodeWork.Services;
using System.IO;
using System.Text;

namespace WebCodeWork.Controllers
{
    [Route("api/testcases")] // Base route for test case specific actions
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

        // --- Helper Methods ---
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId)) { throw new UnauthorizedAccessException("User ID not found in token."); }
            return userId;
        }

        // Fetches TestCase and verifies user permission (Owner/Teacher)
        private async Task<(TestCase? testCase, IActionResult? errorResult)> GetTestCaseAndVerifyAccess(int testCaseId, int currentUserId)
        {
            var testCase = await _context.TestCases
                .Include(tc => tc.Assignment) // Need Assignment for ClassroomId and IsCodeAssignment check
                .FirstOrDefaultAsync(tc => tc.Id == testCaseId);

            if (testCase == null)
            {
                return (null, NotFound(new { message = "Test case not found." }));
            }

            // Verify IsCodeAssignment flag (redundant if only code assignments have test cases, but safe)
            if (!testCase.Assignment.IsCodeAssignment)
            {
                 _logger.LogWarning("Attempt to access test case {TestCaseId} for non-code assignment {AssignmentId}", testCaseId, testCase.AssignmentId);
                 // Treat as Not Found or Forbidden? Let's use Forbidden.
                 return (null, Forbid());
            }

            // Verify user is Owner/Teacher in the classroom
            var userRole = await _context.ClassroomMembers
                                 .Where(cm => cm.UserId == currentUserId && cm.ClassroomId == testCase.Assignment.ClassroomId)
                                 .Select(cm => (ClassroomRole?)cm.Role) // Select nullable role
                                 .FirstOrDefaultAsync();

            if (userRole != ClassroomRole.Owner && userRole != ClassroomRole.Teacher)
            {
                 _logger.LogWarning("User {UserId} forbidden from accessing test case {TestCaseId}.", currentUserId, testCaseId);
                return (null, Forbid());
            }

            return (testCase, null); // Success, return test case and no error
        }


        // --- Content Endpoints ---

        // GET /api/testcases/{testCaseId}/input/content
        [HttpGet("{testCaseId}/input/content")]
        [Produces("text/plain")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTestCaseInputContent(int testCaseId)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

            var (testCase, errorResult) = await GetTestCaseAndVerifyAccess(testCaseId, currentUserId);
            if (errorResult != null) return errorResult;
            // testCase is guaranteed not null here

            var (fileStream, _, _) = await _fileService.GetTestCaseFileAsync(
                testCase!.InputFilePath, // Use null-forgiving operator as testCase is checked
                testCase.InputStoredFileName,
                testCase.InputFileName
            );

            if (fileStream == null) return NotFound(new { message = "Input file content not found in storage." });

            string fileContent;
            try
            {
                using (var reader = new StreamReader(fileStream, Encoding.UTF8)) { fileContent = await reader.ReadToEndAsync(); }
            }
            catch (Exception ex) { /* Log error, return 500 */ return StatusCode(500); }

            return Content(fileContent, "text/plain", Encoding.UTF8);
        }

        // GET /api/testcases/{testCaseId}/output/content
        [HttpGet("{testCaseId}/output/content")]
        [Produces("text/plain")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        // ... other response types ...
        public async Task<IActionResult> GetTestCaseOutputContent(int testCaseId)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

            var (testCase, errorResult) = await GetTestCaseAndVerifyAccess(testCaseId, currentUserId);
            if (errorResult != null) return errorResult;

            var (fileStream, _, _) = await _fileService.GetTestCaseFileAsync(
                testCase!.ExpectedOutputFilePath,
                testCase.ExpectedOutputStoredFileName,
                testCase.ExpectedOutputFileName
            );

            if (fileStream == null) return NotFound(new { message = "Expected output file content not found in storage." });

            string fileContent;
            try
            {
                using (var reader = new StreamReader(fileStream, Encoding.UTF8)) { fileContent = await reader.ReadToEndAsync(); }
            }
            catch (Exception ex) { /* Log error, return 500 */ return StatusCode(500); }

            return Content(fileContent, "text/plain", Encoding.UTF8);
        }

        // PUT /api/testcases/{testCaseId}/input/content
        [HttpPut("{testCaseId}/input/content")]
        [Consumes("text/plain")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        // ... other response types ...
        public async Task<IActionResult> UpdateTestCaseInputContent(int testCaseId, [FromBody] string content)
        {
             if (content == null) return BadRequest(new { message = "Request body cannot be empty." });

             int currentUserId;
             try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

             var (testCase, errorResult) = await GetTestCaseAndVerifyAccess(testCaseId, currentUserId);
             if (errorResult != null) return errorResult;

            // Overwrite file content
             bool success = false;
             try { success = await _fileService.OverwriteSubmissionFileAsync(testCase!.InputFilePath, testCase.InputStoredFileName, content); }
             catch(Exception ex) { _logger.LogError(ex, "File service failed during input overwrite for test case {TestCaseId}.", testCaseId); }

             if (!success) return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to update input file content in storage." });

             // Update DB? (e.g., file size, modified date if tracking) - Optional for now
             // testCase.InputFileSize = Encoding.UTF8.GetByteCount(content);
             // _context.Entry(testCase).State = EntityState.Modified;
             // try { await _context.SaveChangesAsync(); } catch { /* Log, maybe return 500 */ }

            return NoContent();
        }

        // PUT /api/testcases/{testCaseId}/output/content
        [HttpPut("{testCaseId}/output/content")]
        [Consumes("text/plain")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        // ... other response types ...
        public async Task<IActionResult> UpdateTestCaseOutputContent(int testCaseId, [FromBody] string content)
        {
            if (content == null) return BadRequest(new { message = "Request body cannot be empty." });

            int currentUserId;
            try { currentUserId = GetCurrentUserId(); } catch { return Unauthorized(); }

            var (testCase, errorResult) = await GetTestCaseAndVerifyAccess(testCaseId, currentUserId);
            if (errorResult != null) return errorResult;

             // Overwrite file content
             bool success = false;
             try { success = await _fileService.OverwriteSubmissionFileAsync(testCase!.ExpectedOutputFilePath, testCase.ExpectedOutputStoredFileName, content); }
             catch(Exception ex) { _logger.LogError(ex, "File service failed during output overwrite for test case {TestCaseId}.", testCaseId); }

             if (!success) return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to update output file content in storage." });

             // Update DB? (e.g., file size, modified date if tracking) - Optional for now

            return NoContent();
        }
    }
}