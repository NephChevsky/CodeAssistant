namespace CodeAssistant.WebApp.Models
{
    public sealed record ChatSettings(bool ProductionMode, string? Model, string? ReasoningEffort);

    public sealed record PlanSettings(string Description, string RepositoryRoot, string? Model, string? ReasoningEffort);

    public enum ChatChunkKind
    {
        Text,
        Tool,
        Usage,
    }

    /// <summary>A piece of a streamed chat reply: assistant text, a tool call summary (<see cref="ToolName"/> set), or a usage update with empty text and costs in US dollars.</summary>
    public sealed record ChatChunk(ChatChunkKind Kind, string Text, string? ToolName = null, decimal? ResponseCost = null, decimal? SessionCost = null);

    public sealed record PendingQuestion(string Id, string Question, IReadOnlyList<string> Choices, bool AllowFreeform);

    public sealed record SavedPlan(string MarkdownPath, string JsonPath);

    public sealed record RepositoryInfo(string Name, string Path);
}
