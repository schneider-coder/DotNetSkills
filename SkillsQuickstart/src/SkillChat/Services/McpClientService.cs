using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Chat;
using SkillChat.Config;

namespace SkillChat.Services;

public class McpClientService : IMcpClientService
{
    private readonly McpServersConfig _config;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly Dictionary<string, (string ServerName, McpClientTool Tool)> _toolRegistry = new();
    private bool _initialized;

    public McpClientService(IOptions<McpServersConfig> config, IConfiguration configuration)
    {
        _config = config.Value;
        _configuration = configuration;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        foreach (var serverConfig in _config.Servers.Where(s => s.Enabled))
        {
            try
            {
                Console.WriteLine($"  Connecting to MCP server: {serverConfig.Name} ({serverConfig.Type})...");

                var clientOptions = new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = "SkillChat", Version = "1.0.0" }
                };

                McpClient client;

                if (serverConfig.Type == McpTransportType.Http)
                {
                    if (string.IsNullOrEmpty(serverConfig.Endpoint))
                        throw new InvalidOperationException($"HTTP MCP server '{serverConfig.Name}' requires an Endpoint.");

                    client = await McpClient.CreateAsync(
                        new HttpClientTransport(new HttpClientTransportOptions
                        {
                            Name = serverConfig.Name,
                            Endpoint = new Uri(serverConfig.Endpoint)
                        }), clientOptions);
                }
                else
                {
                    var resolvedArguments = serverConfig.Arguments
                        .Select(ResolvePathIfRelative).ToList();

                    var transportConfig = new StdioClientTransportOptions
                    {
                        Command = serverConfig.Command,
                        Arguments = resolvedArguments,
                        Name = serverConfig.Name
                    };

                    if (serverConfig.Environment.Count > 0)
                    {
                        var envVars = new Dictionary<string, string?>();
                        foreach (var kvp in serverConfig.Environment)
                        {
                            var value = string.IsNullOrEmpty(kvp.Value) ? _configuration[kvp.Key] : kvp.Value;
                            envVars[kvp.Key] = value;
                        }
                        transportConfig.EnvironmentVariables = envVars;
                    }

                    client = await McpClient.CreateAsync(
                        new StdioClientTransport(transportConfig), clientOptions);
                }

                _clients[serverConfig.Name] = client;

                var tools = await client.ListToolsAsync();
                foreach (var tool in tools)
                {
                    _toolRegistry[tool.Name] = (serverConfig.Name, tool);
                    Console.WriteLine($"    Registered tool: {tool.Name}");
                }

                Console.WriteLine($"  Connected to {serverConfig.Name} with {tools.Count} tools");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Failed to connect to {serverConfig.Name}: {ex.Message}");
            }
        }

        _initialized = true;
    }

    public IReadOnlyList<ChatTool> GetAvailableTools()
    {
        var chatTools = new List<ChatTool>();
        foreach (var (toolName, (_, tool)) in _toolRegistry)
        {
            var schemaJson = tool.JsonSchema.ValueKind != JsonValueKind.Undefined
                ? tool.JsonSchema.GetRawText() : "{}";
            chatTools.Add(ChatTool.CreateFunctionTool(
                toolName, tool.Description ?? string.Empty, BinaryData.FromString(schemaJson)));
        }
        return chatTools;
    }

    public async Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        if (!_toolRegistry.TryGetValue(toolName, out var entry))
            return $"Error: Tool '{toolName}' not found.";

        if (!_clients.TryGetValue(entry.ServerName, out var client))
            return $"Error: MCP server '{entry.ServerName}' not connected.";

        try
        {
            var arguments = string.IsNullOrEmpty(argumentsJson)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson) ?? new();

            var result = await entry.Tool.CallAsync(arguments);
            var textParts = result.Content.OfType<TextContentBlock>().Select(c => c.Text).Where(t => t != null);
            return string.Join("\n", textParts);
        }
        catch (Exception ex)
        {
            return $"Error executing tool '{toolName}': {ex.Message}";
        }
    }

    public IReadOnlyList<string> GetConnectedServerNames() => _clients.Keys.ToList();

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            if (client is IAsyncDisposable ad) await ad.DisposeAsync();
            else if (client is IDisposable d) d.Dispose();
        }
        _clients.Clear();
        _toolRegistry.Clear();
    }

    private static string ResolvePathIfRelative(string argument)
    {
        if (argument.StartsWith('@') || argument.StartsWith("-")) return argument;
        if ((argument.EndsWith(".dll") || argument.EndsWith(".exe") || argument.Contains(".."))
            && !Path.IsPathRooted(argument))
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, argument));
        return argument;
    }
}
