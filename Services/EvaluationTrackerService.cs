using System.Collections.Concurrent;

namespace WebCodeWork.Services
{
    public class EvaluationTrackerService
    {
        private readonly ILogger<EvaluationTrackerService> _logger;
        private readonly IConfiguration _config;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _trackedSubmissions = new();

        private readonly int EXPIRATION_MAX_SECONDS;

        public EvaluationTrackerService(IConfiguration config, ILogger<EvaluationTrackerService> logger)
        {
            _config = config;
            _logger = logger;

            EXPIRATION_MAX_SECONDS = _config.GetValue<int>("CodeRunnerService:MaxTimeSecondsPerEvaluation");
        }

        public void TrackSubmission(int submissionId, Action<int> onTimeout)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(EXPIRATION_MAX_SECONDS));
            cts.Token.Register(() =>
            {
                onTimeout(submissionId);
                if (_trackedSubmissions.TryRemove(submissionId, out var _cts))
                {
                    _cts.Dispose();
                }
            });
            if (!_trackedSubmissions.TryAdd(submissionId, cts))
            {
                cts.Dispose();
                _logger.LogInformation("Attempted to track submission {SubmissionId} which is already being tracked.", submissionId);
            }
            else
            {
                _logger.LogInformation("Started tracking submission {SubmissionId} with a timeout of {Timeout} s.", submissionId, EXPIRATION_MAX_SECONDS);
            }
        }

        public void CompleteSubmission(int submissionId)
        {
            if (_trackedSubmissions.TryRemove(submissionId, out var cts))
            {
                _logger.LogInformation("Completing tracking for submission {SubmissionId}.", submissionId);
                cts.Dispose();
            }
        }
        
        public bool IsTracked(int submissionId)
        {
            return _trackedSubmissions.ContainsKey(submissionId);
        }
    }
}