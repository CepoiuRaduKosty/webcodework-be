
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace WebCodeWork.Hubs
{
    [Authorize]
    public class EvaluationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            _logger.LogInformation("SignalR client connected: {ConnectionId}, User: {UserId}", Context.ConnectionId, userId ?? "Anonymous");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            _logger.LogInformation("SignalR client disconnected: {ConnectionId}, User: {UserId}, Error: {Error}", Context.ConnectionId, userId ?? "Anonymous", exception?.Message);
            await base.OnDisconnectedAsync(exception);
        }

        private readonly ILogger<EvaluationHub> _logger;
        public EvaluationHub(ILogger<EvaluationHub> logger)
        {
            _logger = logger;
        }
    }
}