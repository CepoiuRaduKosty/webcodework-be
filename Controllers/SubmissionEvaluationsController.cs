// Controllers/SubmissionEvaluationsController.cs (in your Main Backend project)
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // For IConfiguration
using Microsoft.Extensions.Logging;    // For ILogger
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;               // For HttpClient
using System.Net.Http.Headers;       // For MediaTypeWithQualityHeaderValue
using System.Net.Http.Json;          // For PostAsJsonAsync, ReadFromJsonAsync
using System.Security.Claims;
using System.Threading.Tasks;
using WebCodeWork.Data;      // Your DbContext (e.g., ApplicationDbContext)
using WebCodeWork.Models;    // Your Models (AssignmentSubmission, Assignment, TestCase, SubmittedFile)
using WebCodeWork.Enums;     // Your Enums (ClassroomRole)
using WebCodeWork.Dtos;      // The CodeRunnerDtos defined above

namespace YourMainBackend.Controllers
{
    [Route("api/submission-evaluations")]
    [ApiController]
    [Authorize] // Protect the controller (e.g., with JWT Bearer auth for your users)
    public class SubmissionEvaluationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context; // Your main DbContext
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SubmissionEvaluationsController> _logger;

        public SubmissionEvaluationsController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<SubmissionEvaluationsController> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new UnauthorizedAccessException("User ID not found or invalid in token.");
            return userId;
        }

        /// <summary>
        /// Triggers the evaluation of a student's submission for a code assignment.
        /// </summary>
        /// <param name="submissionId">The ID of the AssignmentSubmission to evaluate.</param>
        /// <returns>The evaluation results from the CodeRunnerService.</returns>
        [HttpPost("{submissionId}/trigger")]
        [ProducesResponseType(typeof(CodeRunnerEvaluateResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> TriggerEvaluation(int submissionId)
        {
            int currentUserId;
            try { currentUserId = GetCurrentUserId(); }
            catch (UnauthorizedAccessException) { return Unauthorized(); }

            _logger.LogInformation("User {UserId} triggering evaluation for submission {SubmissionId}", currentUserId, submissionId);

            // 1. Fetch Submission and related data
            var submission = await _context.AssignmentSubmissions
                .Include(s => s.Student) // For student info, if needed for auth later
                .Include(s => s.SubmittedFiles) // To find solution.c
                .Include(s => s.Assignment)
                    .ThenInclude(a => a!.Classroom) // For classroom ID for auth
                .Include(s => s.Assignment)
                    .ThenInclude(a => a!.TestCases) // To get all test cases
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
            {
                return NotFound(new ProblemDetails { Title = "Submission Not Found", Detail = $"Submission with ID {submissionId} not found."});
            }

            // 2. Authorization Check: Example - Teacher/Owner of the classroom
            // (Adjust this to your actual authorization logic for triggering evaluations)
            var classroomId = submission.Assignment.ClassroomId;
            var userRoleInClassroom = await _context.ClassroomMembers
                .Where(cm => cm.UserId == currentUserId && cm.ClassroomId == classroomId)
                .Select(cm => (ClassroomRole?)cm.Role)
                .FirstOrDefaultAsync();

            if (userRoleInClassroom != ClassroomRole.Owner && userRoleInClassroom != ClassroomRole.Teacher)
            {
                 _logger.LogWarning("User {UserId} (Role: {Role}) forbidden to trigger evaluation for submission {SubmissionId} in classroom {ClassroomId}",
                    currentUserId, userRoleInClassroom?.ToString() ?? "None", submissionId, classroomId);
                return Forbid();
            }

            // 3. Validation
            if (submission.Assignment == null) // Should not happen with Include
            {
                _logger.LogError("Assignment data missing for submission {SubmissionId}", submissionId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Internal Error", Detail = "Assignment data is missing."});
            }
            if (!submission.Assignment.IsCodeAssignment)
            {
                return BadRequest(new ProblemDetails { Title = "Not a Code Assignment", Detail = "This assignment is not configured for code evaluation."});
            }

            var solutionFile = submission.SubmittedFiles.FirstOrDefault(f => f.FileName.Equals("solution.c", StringComparison.OrdinalIgnoreCase));
            if (solutionFile == null)
            {
                return BadRequest(new ProblemDetails { Title = "Solution File Missing", Detail = "No 'solution.c' file found in this submission."});
            }

            if (submission.Assignment.TestCases == null || !submission.Assignment.TestCases.Any())
            {
                return BadRequest(new ProblemDetails { Title = "No Test Cases", Detail = "No test cases configured for this assignment."});
            }

            // 4. Construct Request for CodeRunnerService
            // Paths must be the full paths in Azure Blob Storage, as expected by the runner.
            // Example assumes FilePath is the directory and StoredFileName is the actual blob name.
            var codeRunnerRequest = new CodeRunnerEvaluateRequest
            {
                Language = "c", // Fixed for now
                Version = "latest", // Or determine from assignment if needed
                CodeFilePath = Path.Combine(solutionFile.FilePath, solutionFile.StoredFileName).Replace("\\", "/"),
                TestCases = submission.Assignment.TestCases.Select(tc => new CodeRunnerTestCaseInfo
                {
                    InputFilePath = Path.Combine(tc.InputFilePath, tc.InputStoredFileName).Replace("\\", "/"),
                    ExpectedOutputFilePath = Path.Combine(tc.ExpectedOutputFilePath, tc.ExpectedOutputStoredFileName).Replace("\\", "/"),
                    MaxExecutionTimeMs = tc.MaxExecutionTimeMs,
                    MaxRamMB = tc.MaxRamMB,
                    TestCaseId = tc.Id.ToString() // Pass TestCase DB ID for correlation
                }).ToList()
            };

            // 5. Call CodeRunnerService
            var runnerBaseUrl = _configuration.GetValue<string>("CodeRunnerService:BaseUrl");
            var runnerApiKey = _configuration.GetValue<string>("CodeRunnerService:ApiKey");

            if (string.IsNullOrEmpty(runnerBaseUrl) || string.IsNullOrEmpty(runnerApiKey))
            {
                _logger.LogCritical("CodeRunnerService URL or API Key not configured in main backend.");
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Configuration Error", Detail = "Code runner service is not configured."});
            }

            var httpClient = _httpClientFactory.CreateClient("CodeRunnerClient"); // Use a named client
            httpClient.BaseAddress = new Uri(runnerBaseUrl);
            httpClient.DefaultRequestHeaders.Clear(); // Clear any defaults if reusing client
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", runnerApiKey);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _logger.LogInformation("Calling CodeRunnerService for submission {SubmissionId}...", submissionId);
            HttpResponseMessage runnerHttpResponse;
            try
            {
                // The CodeRunnerService orchestrator's endpoint is POST /api/evaluate/orchestrate
                runnerHttpResponse = await httpClient.PostAsJsonAsync("/api/evaluate/orchestrate", codeRunnerRequest);
            }
            catch (HttpRequestException httpEx)
            {
                 _logger.LogError(httpEx, "HTTP request to CodeRunnerService failed for submission {SubmissionId}.", submissionId);
                 return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails { Title = "Runner Service Unreachable", Detail = "Could not connect to the code runner service."});
            }

            // 6. Process Response from CodeRunnerService
            if (runnerHttpResponse.IsSuccessStatusCode)
            {
                var runnerResponse = await runnerHttpResponse.Content.ReadFromJsonAsync<CodeRunnerEvaluateResponse>();
                if (runnerResponse == null)
                {
                    _logger.LogError("Failed to deserialize response from CodeRunnerService for submission {SubmissionId}.", submissionId);
                     return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Runner Response Error", Detail = "Invalid response from code runner service."});
                }
                _logger.LogInformation("Evaluation completed for submission {SubmissionId}. Overall Status: {OverallStatus}", submissionId, runnerResponse.OverallStatus);
                return Ok(runnerResponse); // Return the raw response from the runner for now
            }
            else
            {
                var errorContent = await runnerHttpResponse.Content.ReadAsStringAsync();
                _logger.LogError("CodeRunnerService returned error for submission {SubmissionId}. Status: {StatusCode}. Body: {ErrorBody}",
                    submissionId, runnerHttpResponse.StatusCode, errorContent);
                return StatusCode((int)runnerHttpResponse.StatusCode, new ProblemDetails { Title = "Runner Service Error", Detail = $"Code runner service failed: {errorContent}" });
            }
        }
    }
}