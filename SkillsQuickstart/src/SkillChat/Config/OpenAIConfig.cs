namespace SkillChat.Config;

/// <summary>
/// Configuration for OpenAI service connection.
/// </summary>
public class OpenAIConfig
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 4096;
    public float Temperature { get; set; } = 0.7f;
}
