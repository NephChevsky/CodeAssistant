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
    /// Planning: <see cref="StartPlan"/>, then answer the PlanQuestion events and the PhaseReview / PlanReview events (with <see cref="ReviewPlan"/>)
    /// until PlanCompleted, PlanFailed or PlanCancelled.
    /// </summary>
    public sealed class AssistantHub : Hub
    {
        private readonly ILLMService _llm;
        private readonly IFeaturePlanner _planner;
        private readonly AssistantConnections _connections;
        private readonly IHubContext<AssistantHub> _hubContext;
        private readonly ILogger<PlanningRun> _planningLogger;
        private readonly IConfiguration _configuration;

        public AssistantHub(ILLMService llm, IFeaturePlanner planner, AssistantConnections connections, IHubContext<AssistantHub> hubContext, ILogger<PlanningRun> planningLogger, IConfiguration configuration)
        {
            _llm = llm;
            _planner = planner;
            _connections = connections;
            _hubContext = hubContext;
            _planningLogger = planningLogger;
            _configuration = configuration;
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

        /// <summary>Git repositories directly under the configured DevFolder (empty when it is not set or does not exist).</summary>
        public IReadOnlyList<RepositoryInfo> GetRepositories()
        {
            string? devFolder = _configuration["DevFolder"];
            if (string.IsNullOrWhiteSpace(devFolder) || !Directory.Exists(devFolder))
            {
                return [];
            }

            return new DirectoryInfo(devFolder).EnumerateDirectories()
                .Where(d => Path.Exists(Path.Combine(d.FullName, ".git")))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new RepositoryInfo(d.Name, d.FullName))
                .ToList();
        }

        public async Task StartPlan(PlanSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Description))
            {
                throw new HubException("Describe the feature to plan.");
            }

            string? devFolder = _configuration["DevFolder"];
            string repositoryRoot = settings.RepositoryRoot?.Trim() ?? string.Empty;

            // No repository selected means multi-repository planning across the whole DevFolder.
            bool multiRepository = repositoryRoot.Length == 0;
            if (multiRepository)
            {
                repositoryRoot = devFolder?.Trim() ?? string.Empty;
            }

            if (repositoryRoot.Length == 0 || !Path.IsPathFullyQualified(repositoryRoot) || !Directory.Exists(repositoryRoot))
            {
                throw new HubException($"Repository '{repositoryRoot}' is not the absolute path of an existing directory.");
            }

            ConnectionState state = State;
            await state.StopPlanAsync();
            FeatureRequest request = new()
            {
                Description = settings.Description.Trim(),
                MultiRepository = multiRepository,
                RepositoryRoot = Path.GetFullPath(repositoryRoot),
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
