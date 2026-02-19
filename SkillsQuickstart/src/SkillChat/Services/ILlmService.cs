using OpenAI.Chat;

namespace SkillChat.Services;

/// <summary>
/// Represents the result of a chat completion.
/// </summary>
public class ChatCompletionResult
{
    public string? TextResponse { get; init; }
    public IReadOnlyList<ChatToolCall> ToolCalls { get; init; } = Array.Empty<ChatToolCall>();
    public bool HasToolCalls => ToolCalls.Count > 0;
    public ChatFinishReason? FinishReason { get; init; }
}

/// <summary>
/// Service for interacting with an LLM provider.
/// </summary>
public interface ILlmService
{
    Task<ChatCompletionResult> GetCompletionAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ChatTool>? tools = null,
        CancellationToken cancellationToken = default);
}
