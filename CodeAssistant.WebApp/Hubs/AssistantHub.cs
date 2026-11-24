using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Planning;
using CodeAssistant.WebApp.Models;
using CodeAssistant.WebApp.Services;
using LLMLib;
using Microsoft.AspNetCore.SignalR;

namespace CodeAssistant.WebApp.Hubs
{
    /// <summary>
    /// Chat: <see cref="StartChat"/> then <see cref="SendMessage"/> (streamed).
    /// Planning: <see cref="StartPlan"/>, then answer the PlanQuestion / PlanReview events until PlanCompleted, PlanFailed or PlanCancelled.
    /// </summary>
    public sealed class AssistantHub : Hub
    {
        private readonly ILLMService _llm;
        private readonly IFeaturePlanner _planner;
        private readonly AssistantConnections _connections;
        private readonly IHubContext<AssistantHub> _hubContext;
        private readonly ILogger<PlanningRun> _planningLogger;

        public AssistantHub(ILLMService llm, IFeaturePlanner planner, AssistantConnections connections, IHubContext<AssistantHub> hubContext, ILogger<PlanningRun> planningLogger)
        {
            _llm = llm;
            _planner = planner;
            _connections = connections;
            _hubContext = hubContext;
            _planningLogger = planningLogger;
        }

        private ConnectionState State => _connections.Get(Context.ConnectionId);

        public async Task StartChat(ChatSettings settings)
        {
            ConnectionState state = State;
            await state.StopChatAsync();
            state.Chat = await ChatConversation.CreateAsync(_llm, settings, Context.ConnectionAborted);
        }

        public IAsyncEnumerable<ChatChunk> SendMessage(string message, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new HubException("The message is empty.");
            }

            ChatConversation chat = State.Chat ?? throw new HubException("Start a chat first.");
            return chat.SendAsync(message, cancellationToken);
        }

        public async Task StartPlan(PlanSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Description))
            {
                throw new HubException("Describe the feature to plan.");
            }

            if (string.IsNullOrWhiteSpace(settings.RepositoryRoot) || !Path.IsPathFullyQualified(settings.RepositoryRoot.Trim()) || !Directory.Exists(settings.RepositoryRoot.Trim()))
            {
                throw new HubException($"Repository '{settings.RepositoryRoot}' is not the absolute path of an existing directory.");
            }

            ConnectionState state = State;
            await state.StopPlanAsync();
            FeatureRequest request = new()
            {
                Description = settings.Description.Trim(),
                RepositoryRoot = Path.GetFullPath(settings.RepositoryRoot.Trim()),
                Model = settings.Model,
                ReasoningEffort = settings.ReasoningEffort,
            };
            state.Plan = PlanningRun.Start(_planner, request, _hubContext.Clients.Client(Context.ConnectionId), _planningLogger);
        }

        public void AnswerQuestion(string questionId, string answer)
        {
            PlanningRun plan = State.Plan ?? throw new HubException("No plan is in progress.");
            plan.Answer(questionId, answer);
        }

        public void ReviewPlan(bool approved, string? feedback)
        {
            PlanningRun plan = State.Plan ?? throw new HubException("No plan is in progress.");
            plan.Review(approved, feedback);
        }

        public Task CancelPlan()
        {
            return State.StopPlanAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await _connections.RemoveAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
