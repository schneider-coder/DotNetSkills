namespace SkillChat.Config;

/// <summary>
/// Configuration for Azure OpenAI service connection.
/// </summary>
public class AzureOpenAIConfig
{
    public const string SectionName = "AzureOpenAI";

    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4096;
    public float Temperature { get; set; } = 0.7f;
}
