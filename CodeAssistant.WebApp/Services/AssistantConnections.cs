using System.Collections.Concurrent;

namespace CodeAssistant.WebApp.Services
{
    /// <summary>Chat and planning state of each connected client, dropped when the client disconnects.</summary>
    public sealed class AssistantConnections
    {
        private readonly ConcurrentDictionary<string, ConnectionState> _states = new();

        public ConnectionState Get(string connectionId)
        {
            return _states.GetOrAdd(connectionId, _ => new ConnectionState());
        }

        public async Task RemoveAsync(string connectionId)
        {
            if (_states.TryRemove(connectionId, out ConnectionState? state))
            {
                await state.StopChatAsync();
                await state.StopPlanAsync();
            }
        }
    }

    /// <summary>Hub invocations of a connection run one at a time, so this state is not accessed concurrently.</summary>
    public sealed class ConnectionState
    {
        public ChatConversation? Chat { get; set; }

        public PlanningRun? Plan { get; set; }

        public async Task StopChatAsync()
        {
            if (Chat is { } chat)
            {
                Chat = null;
                await chat.DisposeAsync();
            }
        }

        public async Task StopPlanAsync()
        {
            if (Plan is { } plan)
            {
                Plan = null;
                await plan.DisposeAsync();
            }
        }
    }
}
