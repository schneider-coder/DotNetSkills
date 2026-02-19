using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SkillsCore.Config;
using SkillsCore.Models;
using SkillsCore.Services;
using SkillChat.Config;
using SkillChat.Services;
using Spectre.Console;

// ═══════════════════════════════════════════════════════════════════════════
// Skills Chat - Interactive Chat with Conversation History
// ═══════════════════════════════════════════════════════════════════════════

AnsiConsole.Write(
    new FigletText("Skills Chat")
        .LeftJustified()
        .Color(Color.Cyan1));

AnsiConsole.MarkupLine("[dim]Interactive chat with conversation history & MCP tools[/]\n");

// ─────────────────────────────────────────────────────────────────────────────
// Setup: Configuration and Dependency Injection
// ─────────────────────────────────────────────────────────────────────────────

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<AzureOpenAIConfig>()
    .Build();

var services = new ServiceCollection();

services.AddSingleton<IConfiguration>(configuration);
services.Configure<SkillsConfig>(configuration.GetSection(SkillsConfig.SectionName));
services.Configure<LlmProviderConfig>(configuration.GetSection(LlmProviderConfig.SectionName));
services.Configure<AzureOpenAIConfig>(configuration.GetSection(AzureOpenAIConfig.SectionName));
services.Configure<OpenAIConfig>(configuration.GetSection(OpenAIConfig.SectionName));
services.Configure<McpServersConfig>(configuration.GetSection(McpServersConfig.SectionName));

services.AddSingleton<ISkillLoader, SkillLoaderService>();

var llmProvider = configuration.GetSection(LlmProviderConfig.SectionName).Get<LlmProviderConfig>()?.Provider.ToLower() ?? "azure";
if (llmProvider == "openai")
    services.AddSingleton<ILlmService, OpenAIService>();
else
    services.AddSingleton<ILlmService, AzureOpenAIService>();

services.AddSingleton<IMcpClientService, McpClientService>();

var serviceProvider = services.BuildServiceProvider();

var skillLoader = serviceProvider.GetRequiredService<ISkillLoader>();
var llmService = serviceProvider.GetRequiredService<ILlmService>();
var mcpClientService = serviceProvider.GetRequiredService<IMcpClientService>();

// ─────────────────────────────────────────────────────────────────────────────
// Step 1: Discover Skills
// ─────────────────────────────────────────────────────────────────────────────

IReadOnlyList<SkillDefinition> skills = null!;

await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("[cyan]Discovering skills...[/]", async ctx =>
    {
        skills = await skillLoader.DiscoverSkillsAsync();
    });

if (skills.Count == 0)
{
    AnsiConsole.MarkupLine("[red]No skills found.[/]");
    return;
}

var skillsTable = new Table()
    .Border(TableBorder.Rounded)
    .AddColumn("[bold]Skill[/]")
    .AddColumn("[bold]Description[/]");

foreach (var skill in skills)
{
    skillsTable.AddRow(
        $"[cyan]{skill.Name}[/]",
        skill.Description.Length > 60 ? skill.Description[..60] + "..." : skill.Description);
}

AnsiConsole.Write(new Panel(skillsTable).Header($"[bold green]Available Skills ({skills.Count})[/]").BorderColor(Color.Green));
AnsiConsole.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// Step 2: Connect to MCP Servers
// ─────────────────────────────────────────────────────────────────────────────

try
{
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("[cyan]Connecting to MCP servers...[/]", async ctx =>
        {
            await mcpClientService.InitializeAsync();
        });

    var tools = mcpClientService.GetAvailableTools();
    AnsiConsole.MarkupLine($"[yellow]MCP tools available: {tools.Count}[/]\n");
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[yellow]Warning:[/] MCP servers: {ex.Message}");
    AnsiConsole.MarkupLine("[dim]Continuing without tool support...[/]\n");
}

// ─────────────────────────────────────────────────────────────────────────────
// Step 3: Select a Skill
// ─────────────────────────────────────────────────────────────────────────────

var selectedSkill = AnsiConsole.Prompt(
    new SelectionPrompt<SkillDefinition>()
        .Title("[bold]Select a skill for this chat session:[/]")
        .PageSize(10)
        .MoreChoicesText("[grey](Move up and down to reveal more skills)[/]")
        .UseConverter(s => $"{s.Name} [dim]({s.Id})[/]")
        .AddChoices(skills));

SkillDefinition? loadedSkill = null;
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync($"[cyan]Loading {selectedSkill.Name}...[/]", async ctx =>
    {
        loadedSkill = await skillLoader.LoadSkillAsync(selectedSkill.Id);
    });

if (loadedSkill == null)
{
    AnsiConsole.MarkupLine($"[red]Failed to load skill '{selectedSkill.Id}'.[/]");
    return;
}

// ─────────────────────────────────────────────────────────────────────────────
// Step 4: Start Chat Session
// ─────────────────────────────────────────────────────────────────────────────

var chatSession = new ChatSession(llmService, mcpClientService);
chatSession.LoadSkill(loadedSkill);

AnsiConsole.Write(new Rule($"[bold cyan]Chat: {loadedSkill.Name}[/]").LeftJustified());
AnsiConsole.MarkupLine($"[dim]{loadedSkill.Description}[/]");
AnsiConsole.MarkupLine("[dim]Type your messages below. Commands: /clear (reset history), /skill (switch skill), /quit (exit)[/]\n");

var chatRunning = true;
while (chatRunning)
{
    var userInput = AnsiConsole.Prompt(
        new TextPrompt<string>("[bold green]You:[/]")
            .PromptStyle("white"));

    // Handle commands
    switch (userInput.Trim().ToLower())
    {
        case "/quit" or "/exit" or "/q":
            chatRunning = false;
            continue;

        case "/clear":
            chatSession.ClearHistory();
            AnsiConsole.MarkupLine("[dim]Conversation history cleared.[/]\n");
            continue;

        case "/skill":
            var newSkill = AnsiConsole.Prompt(
                new SelectionPrompt<SkillDefinition>()
                    .Title("[bold]Switch to skill:[/]")
                    .PageSize(10)
                    .UseConverter(s => $"{s.Name} [dim]({s.Id})[/]")
                    .AddChoices(skills));

            var loaded = await skillLoader.LoadSkillAsync(newSkill.Id);
            if (loaded != null)
            {
                loadedSkill = loaded;
                chatSession.ClearHistory();
                chatSession.LoadSkill(loadedSkill);
                AnsiConsole.Write(new Rule($"[bold cyan]Chat: {loadedSkill.Name}[/]").LeftJustified());
                AnsiConsole.MarkupLine("[dim]Switched skill and cleared history.[/]\n");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Failed to load skill '{newSkill.Id}'.[/]");
            }
            continue;

        case "/help":
            AnsiConsole.MarkupLine("[dim]/clear  - Clear conversation history[/]");
            AnsiConsole.MarkupLine("[dim]/skill  - Switch to a different skill[/]");
            AnsiConsole.MarkupLine("[dim]/quit   - Exit the chat[/]\n");
            continue;
    }

    if (string.IsNullOrWhiteSpace(userInput))
        continue;

    // Send message and display response
    AnsiConsole.WriteLine();

    var result = await chatSession.SendMessageAsync(userInput);

    AnsiConsole.WriteLine();

    if (result.ToolCalls.Count > 0)
    {
        var toolsTable = new Table()
            .Border(TableBorder.Simple)
            .AddColumn("[bold]Tool[/]")
            .AddColumn("[bold]Result Preview[/]");

        foreach (var tc in result.ToolCalls)
        {
            toolsTable.AddRow(
                $"[yellow]{Markup.Escape(tc.ToolName)}[/]",
                Markup.Escape(tc.Result.Length > 60 ? tc.Result[..60] + "..." : tc.Result));
        }

        AnsiConsole.Write(toolsTable);
        AnsiConsole.WriteLine();
    }

    if (!result.Success)
    {
        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(result.Error ?? "Unknown error")}[/]\n");
    }
    else
    {
        AnsiConsole.Write(new Panel(
            new Markup(Markup.Escape(result.Response)))
            .Header("[bold cyan]Assistant[/]")
            .BorderColor(Color.Cyan1)
            .Expand());
        AnsiConsole.WriteLine();
    }
}

// Cleanup
AnsiConsole.MarkupLine("[dim]Goodbye![/]");
if (mcpClientService is IAsyncDisposable disposable)
    await disposable.DisposeAsync();
