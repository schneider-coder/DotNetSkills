using OpenAI.Chat;

namespace SkillChat.Services;

/// <summary>
/// Service for managing MCP server connections and executing tool calls.
/// </summary>
public interface IMcpClientService : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<ChatTool> GetAvailableTools();
    Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetConnectedServerNames();
}
