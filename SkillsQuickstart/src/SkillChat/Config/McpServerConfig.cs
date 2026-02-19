namespace SkillChat.Config;

/// <summary>
/// Configuration for MCP server connections.
/// </summary>
public class McpServersConfig
{
    public const string SectionName = "McpServers";

    public List<McpServerEntry> Servers { get; set; } = new();
}

/// <summary>
/// Transport type for MCP server connections.
/// </summary>
public enum McpTransportType
{
    Stdio,
    Http
}

/// <summary>
/// Configuration for a single MCP server.
/// </summary>
public class McpServerEntry
{
    public string Name { get; set; } = string.Empty;
    public McpTransportType Type { get; set; } = McpTransportType.Stdio;
    public string Command { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = new();
    public Dictionary<string, string> Environment { get; set; } = new();
    public string Endpoint { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
