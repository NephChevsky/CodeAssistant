using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CodeAssistant.Lib.Chat;
using CodeAssistant.WebApp.Models;
using LLMLib;
using LLMLib.DTO;
using Microsoft.AspNetCore.SignalR;

namespace CodeAssistant.WebApp.Services
{
    /// <summary>A chat session with read-only file system tools. Tool calls are streamed alongside the reply text.</summary>
    public sealed class ChatConversation : IAsyncDisposable
    {
        private ILLMSession _session = null!;
        private ChannelWriter<ChatChunk>? _currentTurn;
        private int _busy;

        private ChatConversation()
        {
        }

        public static async Task<ChatConversation> CreateAsync(ILLMService llm, ChatSettings settings, CancellationToken cancellationToken)
        {
            ChatConversation chat = new();
            chat._session = await llm.CreateSessionAsync(new LLMSessionOptions
            {
                SystemMessage = ChatAssistant.SystemMessage(settings.ProductionMode),
                Model = settings.Model,
                ReasoningEffort = settings.ReasoningEffort,
                Tools = ChatAssistant.CreateTools(chat.OnToolInvoked),
            }, cancellationToken);
            return chat;
        }

        public async IAsyncEnumerable<ChatChunk> SendAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1)
            {
                throw new HubException("A reply is already in progress.");
            }

            Channel<ChatChunk> channel = Channel.CreateUnbounded<ChatChunk>(new UnboundedChannelOptions { SingleReader = true });
            Volatile.Write(ref _currentTurn, channel.Writer);
            Task producer = ProduceAsync(message, channel.Writer, cancellationToken);
            try
            {
                await foreach (ChatChunk chunk in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return chunk;
                }
            }
            finally
            {
                // Waits for the reply to stop before another turn can start; its failure is already reported through the channel.
                await Task.WhenAny(producer);
                Volatile.Write(ref _currentTurn, null);
                Volatile.Write(ref _busy, 0);
            }
        }

        public ValueTask DisposeAsync()
        {
            return _session.DisposeAsync();
        }

        private async Task ProduceAsync(string message, ChannelWriter<ChatChunk> writer, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (LLMStreamItem item in _session.AskStreamingAsync(message, cancellationToken: cancellationToken))
                {
                    switch (item)
                    {
                        case LLMTextChunk t:
                            writer.TryWrite(new ChatChunk(ChatChunkKind.Text, t.Text));
                            break;
                        case LLMUsageUpdate u:
                            writer.TryWrite(new ChatChunk(ChatChunkKind.Usage, string.Empty, null, u.ResponseCost, u.SessionCost));
                            break;
                    }
                }

                writer.TryComplete();
            }
            catch (Exception ex)
            {
                writer.TryComplete(ex);
            }
        }

        private void OnToolInvoked(string toolName, string summary)
        {
            Volatile.Read(ref _currentTurn)?.TryWrite(new ChatChunk(ChatChunkKind.Tool, summary, toolName));
        }
    }
}
