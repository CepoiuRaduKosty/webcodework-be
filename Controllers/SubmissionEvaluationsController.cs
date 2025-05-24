// Controllers/SubmissionEvaluationsController.cs (in your Main Backend project)
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Claims;
using WebCodeWork.Data;
using WebCodeWork.Enums;
using WebCodeWork.Dtos;
using Microsoft.AspNetCore.SignalR;
using WebCodeWork.Hubs;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;

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
        private readonly IHubContext<EvaluationHub> _evaluationHubContext;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public SubmissionEvaluationsController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<SubmissionEvaluationsController> logger,
            IHubContext<EvaluationHub> evaluationHubContext,
            IServiceScopeFactory serviceScopeFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _evaluationHubContext = evaluationHubContext;
            _serviceScopeFactory = serviceScopeFactory;
        }

        private static readonly HashSet<string> SupportedLanguages = new HashSet<string>
        {
            "c", "java", "rust", "go", "python"
        };

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new UnauthorizedAccessException("User ID not found or invalid in token.");
            return userId;
        }

        private string GetCurrentUserIdStringThrows()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim))
                throw new UnauthorizedAccessException("User ID (string) not found in token.");
            return userIdClaim;
        }

        private async Task<bool> CanUserTriggerEvaluation(int currentUserId, int classroomId, int submissionId)
        {
            var userRoleInClassroom = await _context.ClassroomMembers
                .Where(cm => cm.UserId == currentUserId && cm.ClassroomId == classroomId)
                .Select(cm => (ClassroomRole?)cm.Role) // Select nullable role
                .FirstOrDefaultAsync();

            if (userRoleInClassroom == ClassroomRole.Owner || userRoleInClassroom == ClassroomRole.Teacher)
            {
                _logger.LogInformation("User {UserId} is an Owner/Teacher in classroom {ClassroomId}, allowed to trigger evaluation for submission {SubmissionId}.",
                    currentUserId, classroomId, submissionId);
                return true;
            }
            var submissionOwnerId = await _context.AssignmentSubmissions
                .Where(s => s.Id == submissionId)
                .Select(s => (int?)s.StudentId)
                .FirstOrDefaultAsync();

            if (submissionOwnerId.HasValue)
            {
                if (submissionOwnerId.Value == currentUserId)
                {
                    _logger.LogInformation("User {UserId} is the student owner of submission {SubmissionId}, allowed to trigger evaluation.",
                        currentUserId, submissionId);
                    return true;
                }
                else
                {
                    _logger.LogWarning("User {UserId} is not the owner of submission {SubmissionId} and not a Teacher/Owner in classroom {ClassroomId}. Forbidden.",
                        currentUserId, submissionId, classroomId);
                    return false;
                }
            }
            else
            {
                _logger.LogWarning("User {UserId} attempted to trigger evaluation for non-existent submission {SubmissionId}.",
                    currentUserId, submissionId);
                return false;
            }
        }


        [HttpPost("{submissionId}/trigger")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TriggerEvaluation(int submissionId, [FromQuery, Required] string language)
        {
            int currentUserId;
            string currentUserIdString;
            try
            {
                currentUserId = GetCurrentUserId();
                currentUserIdString = GetCurrentUserIdStringThrows();
            }
            catch (UnauthorizedAccessException) { return Unauthorized(); }

            var normalizedLanguage = language.ToLowerInvariant();
            if (!SupportedLanguages.Contains(normalizedLanguage))
            {
                return BadRequest(new ProblemDetails { Title = "Unsupported Language", Detail = $"The language '{language}' is not supported for evaluation." });
            }

            _logger.LogInformation("User {UserId} triggering evaluation for submission {SubmissionId}", currentUserId, submissionId);

            var submission = await _context.AssignmentSubmissions
                .Include(s => s.Student)
                .Include(s => s.SubmittedFiles)
                .Include(s => s.Assignment)
                    .ThenInclude(a => a!.Classroom)
                .Include(s => s.Assignment)
                    .ThenInclude(a => a!.TestCases)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
            {
                return NotFound(new ProblemDetails { Title = "Submission Not Found", Detail = $"Submission with ID {submissionId} not found." });
            }

            if (!await CanUserTriggerEvaluation(currentUserId, submission.Assignment.ClassroomId, submissionId)) return Forbid();

            if (submission.Assignment == null) // Should not happen with Include
            {
                _logger.LogError("Assignment data missing for submission {SubmissionId}", submissionId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "Internal Error", Detail = "Assignment data is missing." });
            }
            if (!submission.Assignment.IsCodeAssignment)
            {
                return BadRequest(new ProblemDetails { Title = "Not a Code Assignment", Detail = "This assignment is not configured for code evaluation." });
            }

            var solutionFile = submission.SubmittedFiles.FirstOrDefault(f => f.FileName.Equals("solution", StringComparison.OrdinalIgnoreCase));
            if (solutionFile == null)
            {
                return BadRequest(new ProblemDetails { Title = "Solution File Missing", Detail = "No 'solution' file found in this submission." });
            }

            if (submission.Assignment.TestCases == null || !submission.Assignment.TestCases.Any())
            {
                return BadRequest(new ProblemDetails { Title = "No Test Cases", Detail = "No test cases configured for this assignment." });
            }

            _ = Task.Run(async () =>
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var scopedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var scopedHttpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                    var scopedConfiguration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<SubmissionEvaluationsController>>();

                    scopedLogger.LogInformation("[Background] Starting actual evaluation for User {UserId}, Submission {SubmissionId}", currentUserIdString, submissionId);

                    var codeRunnerRequest = new CodeRunnerEvaluateRequest
                    {
                        Language = normalizedLanguage,
                        Version = "latest",
                        CodeFilePath = Path.Combine(solutionFile.FilePath, solutionFile.StoredFileName).Replace("\\", "/"),
                        TestCases = submission.Assignment.TestCases.Select(tc => new CodeRunnerTestCaseInfo
                        {
                            InputFilePath = Path.Combine(tc.InputFilePath, tc.InputStoredFileName).Replace("\\", "/"),
                            ExpectedOutputFilePath = Path.Combine(tc.ExpectedOutputFilePath, tc.ExpectedOutputStoredFileName).Replace("\\", "/"),
                            MaxExecutionTimeMs = tc.MaxExecutionTimeMs,
                            MaxRamMB = tc.MaxRamMB,
                            TestCaseId = tc.Id.ToString()
                        }).ToList()
                    };

                    var runnerBaseUrl = scopedConfiguration.GetValue<string>("CodeRunnerService:BaseUrl");
                    var runnerApiKey = scopedConfiguration.GetValue<string>("CodeRunnerService:ApiKey");
                    CodeRunnerEvaluateResponse? finalRunnerResponse = null;

                    if (string.IsNullOrEmpty(runnerBaseUrl) || string.IsNullOrEmpty(runnerApiKey))
                    {
                        scopedLogger.LogCritical("[Background] CodeRunnerService URL or API Key not configured for User {UserId}, Submission {SubmissionId}.", currentUserIdString, submissionId);
                        finalRunnerResponse = new CodeRunnerEvaluateResponse { OverallStatus = "ConfigurationError", CompilationSuccess = false, Results = new List<CodeRunnerTestCaseResult>() };
                    }
                    else
                    {
                        var httpClient = scopedHttpClientFactory.CreateClient("CodeRunnerClient");
                        httpClient.BaseAddress = new Uri(runnerBaseUrl);
                        httpClient.DefaultRequestHeaders.Clear();
                        httpClient.DefaultRequestHeaders.Add("X-Api-Key", runnerApiKey);
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        httpClient.Timeout = TimeSpan.FromMinutes(20);

                        try
                        {
                            HttpResponseMessage runnerHttpResponse = await httpClient.PostAsJsonAsync("/api/evaluate/orchestrate", codeRunnerRequest);
                            if (runnerHttpResponse.IsSuccessStatusCode)
                            {
                                finalRunnerResponse = await runnerHttpResponse.Content.ReadFromJsonAsync<CodeRunnerEvaluateResponse>();
                            }
                            else
                            {
                                var errorContent = await runnerHttpResponse.Content.ReadAsStringAsync();
                                scopedLogger.LogError("[Background] CodeRunnerService returned error for User {UserId}, Submission {SubmissionId}. Status: {StatusCode}. Body: {ErrorBody}",
                                    currentUserIdString, submissionId, runnerHttpResponse.StatusCode, errorContent);
                                finalRunnerResponse = new CodeRunnerEvaluateResponse { OverallStatus = $"RunnerError: {runnerHttpResponse.StatusCode}", CompilationSuccess = false, Results = new List<CodeRunnerTestCaseResult>() };
                            }
                        }
                        catch (TaskCanceledException tex) when (tex.InnerException is TimeoutException) // HttpClient timeout
                        {
                            scopedLogger.LogError(tex, "[Background] HttpClient request to CodeRunnerService timed out for User {UserId}, Submission {SubmissionId}.", currentUserIdString, submissionId);
                            finalRunnerResponse = new CodeRunnerEvaluateResponse { OverallStatus = "RunnerTimeout", CompilationSuccess = false, Results = new List<CodeRunnerTestCaseResult>() };
                        }
                        catch (HttpRequestException httpEx)
                        {
                            scopedLogger.LogError(httpEx, "[Background] HTTP request to CodeRunnerService failed for User {UserId}, Submission {SubmissionId}.", currentUserIdString, submissionId);
                            finalRunnerResponse = new CodeRunnerEvaluateResponse { OverallStatus = "RunnerUnreachable", CompilationSuccess = false, Results = new List<CodeRunnerTestCaseResult>() };
                        }
                        catch (Exception ex)
                        {
                            scopedLogger.LogError(ex, "[Background] Unexpected error during CodeRunnerService call for User {UserId}, Submission {SubmissionId}.", currentUserIdString, submissionId);
                            finalRunnerResponse = new CodeRunnerEvaluateResponse { OverallStatus = "OrchestratorError", CompilationSuccess = false, Results = new List<CodeRunnerTestCaseResult>() };
                        }
                    }

                    if (finalRunnerResponse != null)
                    {
                        scopedLogger.LogInformation("[Background] Sending evaluation result via SignalR to User {UserId} for Submission {SubmissionId}.", currentUserIdString, submissionId);
                        await _evaluationHubContext.Clients.User(currentUserIdString).SendAsync("ReceiveEvaluationResult", finalRunnerResponse, submissionId);
                    }
                }
            });

            // Return an immediate response to the client
            _logger.LogInformation("Evaluation process for submission {SubmissionId} started in background for User {UserId}.", submissionId, currentUserId);
            return Accepted(new { message = "Evaluation process started. You will be notified when results are ready.", submissionId = submissionId });
        }

    }
}