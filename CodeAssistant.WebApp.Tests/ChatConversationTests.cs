using System.Runtime.CompilerServices;
using CodeAssistant.WebApp.Models;
using CodeAssistant.WebApp.Services;
using LLMLib;
using LLMLib.DTO;
using Xunit;

namespace CodeAssistant.WebApp.Tests
{
    public class ChatConversationTests
    {
        [Fact]
        public async Task SendAsync_MapsTextAndUsageInStreamOrder()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            await using FakeLLMService service = new();
            await using ChatConversation chat = await ChatConversation.CreateAsync(service, new ChatSettings(true, null, null), timeout.Token);
            List<ChatChunk> chunks = [];

            await foreach (ChatChunk chunk in chat.SendAsync("hello", timeout.Token))
            {
                chunks.Add(chunk);
            }

            Assert.Equal(
                [
                    new ChatChunk(ChatChunkKind.Text, "Hi"),
                    new ChatChunk(ChatChunkKind.Usage, string.Empty, null, 0.02m, 0.05m),
                    new ChatChunk(ChatChunkKind.Text, "!"),
                ],
                chunks);
        }

        private sealed class FakeLLMService : ILLMService
        {
            public Task<ILLMSession> CreateSessionAsync(LLMSessionOptions? options = null, CancellationToken cancellationToken = default)
                => Task.FromResult<ILLMSession>(new FakeLLMSession());

            public Task<ILLMSession?> OpenSessionAsync(string sessionId, LLMSessionOptions? options = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public Task<IReadOnlyList<ChatSessionInfo>> GetSessionsAsync(CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class FakeLLMSession : ILLMSession
        {
            public string SessionId => "test-session";
            public decimal SessionCost => 0.05m;

            public async IAsyncEnumerable<LLMStreamItem> AskStreamingAsync(string prompt, IReadOnlyList<ChatAttachment>? attachments = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return new LLMTextChunk("Hi");
                yield return new LLMUsageUpdate(0.02m, 0.05m);
                yield return new LLMTextChunk("!");
            }

            public Task<IReadOnlyList<ChatMessageInfo>> GetMessagesAsync(CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
