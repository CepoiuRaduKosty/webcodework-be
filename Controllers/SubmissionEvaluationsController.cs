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
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WebCodeWork.Services;

namespace YourMainBackend.Controllers
{
    [Route("api/submission-evaluations")]
    [ApiController]
    public class SubmissionEvaluationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SubmissionEvaluationsController> _logger;
        private readonly IHubContext<EvaluationHub> _evaluationHubContext;
        private readonly EvaluationTrackerService _evaluationTrackerService;


        public SubmissionEvaluationsController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<SubmissionEvaluationsController> logger,
            IHubContext<EvaluationHub> evaluationHubContext,
            EvaluationTrackerService evaluationTrackerService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _evaluationHubContext = evaluationHubContext;
            _evaluationTrackerService = evaluationTrackerService;
        }

        private static readonly HashSet<string> SupportedLanguages = new HashSet<string>
        {
            "c", "java", "rust", "go", "python"
        };

        private async Task<bool> CanUserTriggerEvaluation(int currentUserId, int classroomId, int submissionId)
        {
            var userRoleInClassroom = await _context.ClassroomMembers
                .Where(cm => cm.UserId == currentUserId && cm.ClassroomId == classroomId)
                .Select(cm => (ClassroomRole?)cm.Role)
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

        private string GetCurrentUserIdStringThrows()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim))
                throw new UnauthorizedAccessException("User ID (string) not found in token.");
            return userIdClaim;
        }


        [HttpPost("{submissionId}/trigger")]
        [Authorize]
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
                currentUserIdString = GetCurrentUserIdStringThrows();
                currentUserId = int.Parse(currentUserIdString);
            }
            catch (UnauthorizedAccessException) { return Unauthorized(); }

            var normalizedLanguage = language.ToLowerInvariant();
            if (!SupportedLanguages.Contains(normalizedLanguage))
            {
                return BadRequest(new ProblemDetails { Title = "Unsupported Language", Detail = $"The language '{language}' is not supported for evaluation." });
            }

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

            if (submission.Assignment == null)
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

            var codeRunnerRequest = new CodeRunnerEvaluateRequest
            {
                Language = normalizedLanguage,
                Version = "latest",
                SubmissionId = submissionId,
                CodeFilePath = Path.Combine(solutionFile.FilePath, solutionFile.StoredFileName).Replace("\\", "/"),
                TestCases = submission.Assignment.TestCases.Select(tc => new CodeRunnerTestCaseInfo
                {
                    InputFilePath = Path.Combine(tc.InputFilePath, tc.InputStoredFileName).Replace("\\", "/"),
                    ExpectedOutputFilePath = Path.Combine(tc.ExpectedOutputFilePath, tc.ExpectedOutputStoredFileName).Replace("\\", "/"),
                    MaxExecutionTimeMs = tc.MaxExecutionTimeMs,
                    MaxRamMB = tc.MaxRamMB,
                    TestCaseId = tc.Id.ToString(),
                }).ToList()
            };

            var runnerBaseUrl = _configuration.GetValue<string>("CodeRunnerService:BaseUrl");
            var runnerApiKey = _configuration.GetValue<string>("CodeRunnerService:ApiKey");

            if (string.IsNullOrEmpty(runnerBaseUrl) || string.IsNullOrEmpty(runnerApiKey))
            {
                _logger.LogCritical("[Background] CodeRunnerService URL or API Key not configured for User {UserId}, Submission {SubmissionId}.", currentUserIdString, submissionId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            else
            {
                var httpClient = _httpClientFactory.CreateClient("CodeRunnerClient");
                httpClient.BaseAddress = new Uri(runnerBaseUrl);
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add(_configuration.GetValue<string>("CodeRunnerService:ApiHeaderName")!, runnerApiKey);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage runnerHttpResponse = await httpClient.PostAsJsonAsync("/api/evaluate/orchestrate", codeRunnerRequest);
                if (runnerHttpResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Evaluation process for submission {SubmissionId} started in background for User {UserId}.", submissionId, currentUserId);
                    _evaluationTrackerService.TrackSubmission(submissionId, _submissionId =>
                    {
                        _logger.LogInformation("[Background] Sending evaluation result timeout via SignalR to User {UserId} for Submission {SubmissionId}",
                            currentUserIdString, submissionId);
                        _ = _evaluationHubContext.Clients.User(currentUserIdString).SendAsync(
                            "ReceiveEvaluationResult",
                            new EvaluationResultSignalRD
                            {
                                SubmissionId = submissionId,
                                EvaluatedLanguage = language,
                                OverallStatus = EvaluationStatus.TimeLimitExceeded,
                                CompilationSuccess = false,
                                Results = new List<CodeRunnerTestCaseResult>(),
                            },
                            submissionId,
                            language
                        );
                    });
                    return Accepted(new { message = "Evaluation process started. You will be notified when results are ready.", submissionId });
                }
                else
                {
                    var errorContent = await runnerHttpResponse.Content.ReadAsStringAsync();
                    _logger.LogError("[Background] CodeRunnerService returned error for User {UserId}, Submission {SubmissionId}. Status: {StatusCode}. Body: {ErrorBody}",
                        currentUserIdString, submissionId, runnerHttpResponse.StatusCode, errorContent);
                    return StatusCode(StatusCodes.Status500InternalServerError);
                }
            }
        }

        [HttpPost("{submissionId}/submit")]
        [Authorize(AuthenticationSchemes = "ApiKey")]
        public async Task<IActionResult> OrchestratorSubmitResult(int submissionId, [FromBody, Required] CodeRunnerEvaluateResponse orchestratorResponse)
        {
            int? pointsObtained = null;
            int? totalPossiblePoints = null;
            string finalOverallStatus = EvaluationStatus.InternalError;
            _logger.LogInformation($"Received request for submitting results for: {submissionId}; response: {orchestratorResponse}");

            if (!_evaluationTrackerService.IsTracked(submissionId))
            {
                _logger.LogWarning($"Tried to submit result of evaluation that is not tracked, for submission {submissionId}.");
                return NotFound(new ProblemDetails { Title = "Submission Not Found", Detail = $"Submission with ID {submissionId} not found." });
            }

            var currentSubmission = await _context.AssignmentSubmissions
                .Include(s => s.Student)
                .Include(s => s.SubmittedFiles)
                .Include(s => s.Assignment)
                    .ThenInclude(a => a!.Classroom)
                .Include(s => s.Assignment)
                    .ThenInclude(a => a!.TestCases)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (currentSubmission == null)
            {
                return NotFound(new ProblemDetails { Title = "Submission Not Found", Detail = $"Submission with ID {submissionId} not found." });
            }

            if (orchestratorResponse != null)
            {
                finalOverallStatus = orchestratorResponse.OverallStatus;

                if (orchestratorResponse.CompilationSuccess && orchestratorResponse.Results != null)
                {
                    pointsObtained = 0;
                    totalPossiblePoints = 0;

                    var testCasePointsMap = currentSubmission.Assignment.TestCases
                        .ToDictionary(tc => tc.Id.ToString(), tc => tc.Points);

                    foreach (var tcResult in orchestratorResponse.Results)
                    {
                        if (tcResult.TestCaseId != null && testCasePointsMap.TryGetValue(tcResult.TestCaseId, out var pointsForThisCase))
                        {
                            totalPossiblePoints = (totalPossiblePoints ?? 0) + pointsForThisCase;
                            if (tcResult.Status == EvaluationStatus.Accepted)
                            {
                                pointsObtained = (pointsObtained ?? 0) + pointsForThisCase;
                            }
                        }
                    }
                }
                else if (!orchestratorResponse.CompilationSuccess)
                {
                    pointsObtained = 0;
                    totalPossiblePoints = currentSubmission.Assignment.TestCases.Sum(tc => tc.Points);
                }

                currentSubmission.LastEvaluationPointsObtained = pointsObtained;
                currentSubmission.LastEvaluationTotalPossiblePoints = totalPossiblePoints;
                currentSubmission.LastEvaluatedAt = DateTime.UtcNow;
                currentSubmission.LastEvaluationOverallStatus = finalOverallStatus;
                currentSubmission.LastEvaluatedLanguage = orchestratorResponse.Language;

            }
            else
            {
                currentSubmission.LastEvaluationOverallStatus = EvaluationStatus.InternalError;
                currentSubmission.LastEvaluatedAt = DateTime.UtcNow;
            }

            var signalRPayload = new EvaluationResultSignalRD
            {
                SubmissionId = submissionId,
                EvaluatedLanguage = orchestratorResponse?.Language ?? "none",
                OverallStatus = orchestratorResponse?.OverallStatus ?? EvaluationStatus.InternalError,
                CompilationSuccess = orchestratorResponse?.CompilationSuccess ?? false,
                CompilerOutput = orchestratorResponse?.CompilerOutput,
                Results = orchestratorResponse?.Results ?? new List<CodeRunnerTestCaseResult>(),
                PointsObtained = pointsObtained,
                TotalPossiblePoints = totalPossiblePoints,
            };

            foreach (var tc in signalRPayload.Results)
            {
                var dbtc = currentSubmission.Assignment.TestCases.FirstOrDefault(t => t.Id.ToString() == tc.TestCaseId);
                tc.TestCaseName = dbtc!.InputFileName;
            }

            if (orchestratorResponse != null)
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false,
                };
                currentSubmission.LastEvaluationDetailsJson = JsonSerializer.Serialize(signalRPayload, jsonOptions);
                try
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("[Background] Successfully saved evaluation results for Submission {SubmissionId}", submissionId);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "[Background] Failed to save evaluation results for Submission {SubmissionId}", submissionId);
                }
            }
            else
            {
                currentSubmission.LastEvaluationDetailsJson = JsonSerializer.Serialize(new { error = "Failed to get response from runner service." });
                try { await _context.SaveChangesAsync(); } catch (Exception ex) { _logger.LogError(ex, "Failed to save internal error state to submission {SubmissionId}", submissionId); }
            }

            foreach (var tc in signalRPayload.Results)
            {
                var dbtc = currentSubmission.Assignment.TestCases.FirstOrDefault(t => t.Id.ToString() == tc.TestCaseId);
                if (dbtc!.IsPrivate)
                {
                    tc.IsPrivate = true;
                    tc.TestCaseInputPath = "";
                    tc.Stdout = "";
                    tc.Message = "";
                    tc.TestCaseId = "";
                    tc.Stderr = "";
                }
                else
                {
                    tc.IsPrivate = false;
                }
            }

            var currentUserId = currentSubmission.StudentId;

            _logger.LogInformation("[Background] Sending evaluation result via SignalR to User {UserId} for Submission {SubmissionId}. Points: {Obtained}/{Possible}",
                currentUserId, submissionId, pointsObtained, totalPossiblePoints);
            _ = _evaluationHubContext.Clients.User(currentUserId.ToString()).SendAsync(
                "ReceiveEvaluationResult",
                signalRPayload,
                submissionId,
                orchestratorResponse?.Language
            );
            _evaluationTrackerService.CompleteSubmission(submissionId);

            return Ok();
        }
    }
}