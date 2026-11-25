using System.Collections.Concurrent;
using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Planning;
using CodeAssistant.WebApp.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeAssistant.WebApp.Tests
{
    public class PlanningRunTests
    {
        [Fact]
        public async Task Start_SendsUsageBeforeCompletion()
        {
            RecordingClient client = new();
            PlanningRun run = PlanningRun.Start(new FakeFeaturePlanner(), new FeatureRequest(), client, NullLogger.Instance);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            try
            {
                while (!client.Events.Any(e => e.Method == "PlanCompleted"))
                {
                    await Task.Delay(10, timeout.Token);
                }
            }
            finally
            {
                await run.DisposeAsync();
            }

            Assert.Collection(client.Events,
                usage =>
                {
                    Assert.Equal("PlanUsage", usage.Method);
                    Assert.Equal(new object?[] { 0.01m, 0.03m }, usage.Args);
                },
                completed => Assert.Equal("PlanCompleted", completed.Method));
        }

        private sealed class FakeFeaturePlanner : IFeaturePlanner
        {
            public Task<FeaturePlanResult> PlanAsync(FeatureRequest request, IPlanningInteraction interaction, CancellationToken cancellationToken = default)
            {
                interaction.OnUsage(0.01m, 0.03m);
                return Task.FromResult(new FeaturePlanResult([], "s"));
            }
        }

        private sealed class RecordingClient : IClientProxy
        {
            public ConcurrentQueue<(string Method, object?[] Args)> Events { get; } = new();

            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                Events.Enqueue((method, args));
                return Task.CompletedTask;
            }
        }
    }
}
