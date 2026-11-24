using System.Collections.Concurrent;
using System.Threading.Channels;
using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Planning;
using CodeAssistant.WebApp.Models;
using Microsoft.AspNetCore.SignalR;

namespace CodeAssistant.WebApp.Services
{
    /// <summary>Runs the feature planner in the background, forwarding its progress to a client and waiting for the client's answers and reviews.</summary>
    public sealed class PlanningRun : IPlanningInteraction, IAsyncDisposable
    {
        private readonly IClientProxy _client;
        private readonly ILogger _logger;
        private readonly Channel<(string Method, object?[] Args)> _events = Channel.CreateUnbounded<(string, object?[])>(new UnboundedChannelOptions { SingleReader = true });
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _questions = new();
        private readonly CancellationTokenSource _cancellation = new();
        private TaskCompletionSource<PlanReview>? _review;
        private Task _run = Task.CompletedTask;
        private Task _pump = Task.CompletedTask;

        private PlanningRun(IClientProxy client, ILogger logger)
        {
            _client = client;
            _logger = logger;
        }

        public static PlanningRun Start(IFeaturePlanner planner, FeatureRequest request, IClientProxy client, ILogger logger)
        {
            PlanningRun run = new(client, logger);
            run._pump = run.PumpAsync();
            run._run = Task.Run(() => run.RunAsync(planner, request));
            return run;
        }

        public void OnPhaseChanged(PlanningPhase phase)
        {
            Send("PlanPhase", phase);
        }

        public void OnAssistantMessage(string markdown)
        {
            Send("PlanMessage", markdown);
        }

        public void OnToolInvoked(string toolName, string summary)
        {
            Send("PlanTool", toolName, summary);
        }

        public async Task<string> AskUserAsync(PlannerQuestion question, CancellationToken cancellationToken)
        {
            string id = Guid.NewGuid().ToString("N");
            TaskCompletionSource<string> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _questions[id] = answer;
            try
            {
                Send("PlanQuestion", new PendingQuestion(id, question.Question, question.Choices, question.AllowFreeform));
                return await answer.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _questions.TryRemove(id, out _);
            }
        }

        public Task<PlanReview> ReviewPhaseAsync(PlanningPhase phase, string markdown, CancellationToken cancellationToken)
        {
            return WaitForReviewAsync("PhaseReview", [phase, markdown], cancellationToken);
        }

        public Task<PlanReview> ReviewPlanAsync(FeaturePlan plan, string markdown, CancellationToken cancellationToken)
        {
            return WaitForReviewAsync("PlanReview", [markdown], cancellationToken);
        }

        private async Task<PlanReview> WaitForReviewAsync(string method, object?[] args, CancellationToken cancellationToken)
        {
            TaskCompletionSource<PlanReview> review = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _review, review);
            try
            {
                Send(method, args);
                return await review.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.CompareExchange(ref _review, null, review);
            }
        }

        public void Answer(string questionId, string answer)
        {
            if (!_questions.TryGetValue(questionId, out TaskCompletionSource<string>? pending) || !pending.TrySetResult(answer))
            {
                throw new HubException("This question is no longer pending.");
            }
        }

        public void Review(bool approved, string? feedback)
        {
            if (!approved && string.IsNullOrWhiteSpace(feedback))
            {
                throw new HubException("Explain what should change.");
            }

            if (Interlocked.Exchange(ref _review, null) is not { } review || !review.TrySetResult(new PlanReview(approved, feedback)))
            {
                throw new HubException("Nothing is waiting for review.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync();
            await Task.WhenAll(_run, _pump);
            _cancellation.Dispose();
        }

        private async Task RunAsync(IFeaturePlanner planner, FeatureRequest request)
        {
            try
            {
                FeaturePlanResult result = await planner.PlanAsync(request, this, _cancellation.Token);
                Send("PlanCompleted", new SavedPlan(result.MarkdownPath, result.JsonPath));
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                Send("PlanCancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Feature planning failed.");
                Send("PlanFailed", ex.Message);
            }
            finally
            {
                _events.Writer.TryComplete();
            }
        }

        /// <summary>Sends events one at a time so the client receives them in order.</summary>
        private async Task PumpAsync()
        {
            await foreach ((string method, object?[] args) in _events.Reader.ReadAllAsync())
            {
                try
                {
                    await _client.SendCoreAsync(method, args);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not send {Method} to the client.", method);
                }
            }
        }

        private void Send(string method, params object?[] args)
        {
            _events.Writer.TryWrite((method, args));
        }
    }
}
