using System.CommandLine;
using Andy.Rbac.Cli.Commands;
using Spectre.Console;

var rootCommand = new RootCommand("Andy RBAC CLI - Manage applications, roles, teams, and users")
{
    Name = "andy-rbac"
};

// Global options
var apiUrlOption = new Option<string>(
    ["--api-url", "-u"],
    () => Environment.GetEnvironmentVariable("ANDY_RBAC_URL") ?? "http://localhost:5000",
    "RBAC API base URL (or set ANDY_RBAC_URL environment variable)");

var apiKeyOption = new Option<string?>(
    ["--api-key", "-k"],
    () => Environment.GetEnvironmentVariable("ANDY_RBAC_API_KEY"),
    "API key for authentication (or set ANDY_RBAC_API_KEY environment variable)");

var outputOption = new Option<OutputFormat>(
    ["--output", "-o"],
    () => OutputFormat.Table,
    "Output format (table, json, csv)");

rootCommand.AddGlobalOption(apiUrlOption);
rootCommand.AddGlobalOption(apiKeyOption);
rootCommand.AddGlobalOption(outputOption);

// Add command groups
rootCommand.AddCommand(ApplicationCommands.Create(apiUrlOption, apiKeyOption, outputOption));
rootCommand.AddCommand(RoleCommands.Create(apiUrlOption, apiKeyOption, outputOption));
rootCommand.AddCommand(TeamCommands.Create(apiUrlOption, apiKeyOption, outputOption));
rootCommand.AddCommand(UserCommands.Create(apiUrlOption, apiKeyOption, outputOption));
rootCommand.AddCommand(CheckCommands.Create(apiUrlOption, apiKeyOption, outputOption));

// Version command
var versionCommand = new Command("version", "Show version information");
versionCommand.SetHandler(() =>
{
    AnsiConsole.MarkupLine("[bold]Andy RBAC CLI[/] v1.0.0");
});
rootCommand.AddCommand(versionCommand);

return await rootCommand.InvokeAsync(args);

public enum OutputFormat
{
    Table,
    Json,
    Csv
}
