using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using SkillChat.Config;

namespace SkillChat.Services;

public class OpenAIService : ILlmService
{
    private readonly ChatClient _chatClient;
    private readonly OpenAIConfig _config;

    public OpenAIService(IOptions<OpenAIConfig> config)
    {
        _config = config.Value;
        var client = new OpenAIClient(_config.ApiKey);
        _chatClient = client.GetChatClient(_config.Model);
    }

    public async Task<ChatCompletionResult> GetCompletionAsync(
        IEnumerable<ChatMessage> messages,
        IEnumerable<ChatTool>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _config.MaxTokens,
            Temperature = _config.Temperature
        };

        if (tools != null)
        {
            foreach (var tool in tools)
                options.Tools.Add(tool);
        }

        var response = await _chatClient.CompleteChatAsync(
            messages.ToList(), options, cancellationToken);

        var completion = response.Value;
        return new ChatCompletionResult
        {
            TextResponse = completion.Content.Count > 0
                ? string.Join("", completion.Content.Select(c => c.Text))
                : null,
            ToolCalls = completion.ToolCalls,
            FinishReason = completion.FinishReason
        };
    }
}
