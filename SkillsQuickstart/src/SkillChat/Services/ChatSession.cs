using OpenAI.Chat;
using SkillsCore.Models;

namespace SkillChat.Services;

/// <summary>
/// Interactive chat service that maintains conversation history across turns.
/// Unlike SkillExecutor which runs a single request, this keeps the full
/// conversation context so the LLM can reference earlier messages.
/// </summary>
public class ChatSession
{
    private readonly ILlmService _llmService;
    private readonly IMcpClientService _mcpClientService;
    private readonly List<ChatMessage> _messages = new();

    public int MessageCount => _messages.Count;

    public ChatSession(ILlmService llmService, IMcpClientService mcpClientService)
    {
        _llmService = llmService;
        _mcpClientService = mcpClientService;
    }

    /// <summary>
    /// Loads a skill as the system prompt for this chat session.
    /// </summary>
    public void LoadSkill(SkillDefinition skill)
    {
        var systemPrompt = BuildSystemPrompt(skill);

        // Remove any existing system message
        _messages.RemoveAll(m => m is SystemChatMessage);

        // Insert system message at the beginning
        _messages.Insert(0, new SystemChatMessage(systemPrompt));
    }

    /// <summary>
    /// Sends a user message and returns the assistant's response,
    /// executing any tool calls along the way.
    /// </summary>
    public async Task<ChatTurnResult> SendMessageAsync(
        string userMessage,
        int maxToolRounds = 30,
        CancellationToken cancellationToken = default)
    {
        _messages.Add(new UserChatMessage(userMessage));

        var tools = _mcpClientService.GetAvailableTools();
        var toolCallRecords = new List<ToolCallRecord>();
        var toolRounds = 0;

        while (toolRounds < maxToolRounds)
        {
            toolRounds++;

            var result = await _llmService.GetCompletionAsync(_messages, tools, cancellationToken);

            if (result.HasToolCalls)
            {
                Console.WriteLine($"    Tool round {toolRounds}: {result.ToolCalls.Count} call(s)");

                _messages.Add(new AssistantChatMessage(result.ToolCalls));

                foreach (var toolCall in result.ToolCalls)
                {
                    Console.WriteLine($"    Executing: {toolCall.FunctionName}");

                    var toolResult = await _mcpClientService.ExecuteToolAsync(
                        toolCall.FunctionName,
                        toolCall.FunctionArguments.ToString(),
                        cancellationToken);

                    toolCallRecords.Add(new ToolCallRecord
                    {
                        ToolName = toolCall.FunctionName,
                        Arguments = toolCall.FunctionArguments.ToString(),
                        Result = toolResult.Length > 200 ? toolResult[..200] + "..." : toolResult
                    });

                    _messages.Add(new ToolChatMessage(toolCall.Id, toolResult));

                    Console.WriteLine($"    Result: {(toolResult.Length > 100 ? toolResult[..100] + "..." : toolResult)}");
                }

                continue;
            }

            // Final response
            if (result.FinishReason == ChatFinishReason.Stop)
            {
                var responseText = result.TextResponse ?? string.Empty;

                // Add assistant response to conversation history
                _messages.Add(new AssistantChatMessage(responseText));

                return new ChatTurnResult
                {
                    Response = responseText,
                    ToolCalls = toolCallRecords,
                    Success = true
                };
            }

            break;
        }

        return new ChatTurnResult
        {
            Response = "Maximum tool rounds reached.",
            ToolCalls = toolCallRecords,
            Success = false,
            Error = "Maximum tool rounds exceeded"
        };
    }

    /// <summary>
    /// Clears all conversation history (keeps the system prompt if loaded).
    /// </summary>
    public void ClearHistory()
    {
        var systemMessage = _messages.FirstOrDefault(m => m is SystemChatMessage);
        _messages.Clear();
        if (systemMessage != null)
            _messages.Add(systemMessage);
    }

    private static string BuildSystemPrompt(SkillDefinition skill)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine($"# {skill.Name}");
        prompt.AppendLine();
        prompt.AppendLine(skill.Description);
        prompt.AppendLine();

        if (!string.IsNullOrEmpty(skill.Instructions))
        {
            prompt.AppendLine("## Instructions");
            prompt.AppendLine();
            prompt.AppendLine(skill.Instructions);
        }

        if (skill.TotalResourceCount > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## Available Resources");
            prompt.AppendLine();
            foreach (var resource in skill.AllResources)
                prompt.AppendLine($"- {resource.ResourceType}: {resource.RelativePath}");
        }

        return prompt.ToString();
    }
}

/// <summary>
/// Result of a single chat turn (user message → assistant response).
/// </summary>
public class ChatTurnResult
{
    public string Response { get; init; } = string.Empty;
    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = Array.Empty<ToolCallRecord>();
    public bool Success { get; init; }
    public string? Error { get; init; }
}

public class ToolCallRecord
{
    public string ToolName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
}
