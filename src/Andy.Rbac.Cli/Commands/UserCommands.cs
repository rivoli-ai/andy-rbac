using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace Andy.Rbac.Cli.Commands;

public static class UserCommands
{
    public static Command Create(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var userCommand = new Command("user", "Manage users")
        {
            CreateSearchCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateGetCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateProvisionCommand(apiUrlOption, apiKeyOption),
            CreateDeactivateCommand(apiUrlOption, apiKeyOption)
        };

        return userCommand;
    }

    private static Command CreateSearchCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var queryArg = new Argument<string>("query", "Search query (email, name, or external ID)");
        var limitOption = new Option<int>(["--limit", "-l"], () => 20, "Maximum results");

        var cmd = new Command("search", "Search for users") { queryArg, limitOption };

        cmd.SetHandler(async (string query, int limit, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var result = await client.GetFromJsonAsync<PagedResult<UserDto>>(
                $"api/subjects?query={Uri.EscapeDataString(query)}&take={limit}");

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result?.Items, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            var table = new Table()
                .AddColumn("External ID")
                .AddColumn("Provider")
                .AddColumn("Email")
                .AddColumn("Name")
                .AddColumn("Active");

            foreach (var user in result?.Items ?? [])
            {
                table.AddRow(
                    user.ExternalId,
                    user.Provider,
                    user.Email ?? "[dim]-[/]",
                    user.DisplayName ?? "[dim]-[/]",
                    user.IsActive ? "[green]Yes[/]" : "[red]No[/]");
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[dim]Showing {result?.Items.Count ?? 0} of {result?.Total ?? 0} results[/]");
        }, queryArg, limitOption, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateGetCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var userArg = new Argument<string>("user", "User external ID");
        var providerOption = new Option<string>(["--provider", "-p"], () => "andy-auth", "Identity provider");

        var cmd = new Command("get", "Get user details") { userArg, providerOption };

        cmd.SetHandler(async (string userId, string provider, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var user = await client.GetFromJsonAsync<UserDetailDto>(
                $"api/subjects/by-external/{provider}/{Uri.EscapeDataString(userId)}");

            if (user == null)
            {
                AnsiConsole.MarkupLine($"[red]User '{userId}' not found[/]");
                return;
            }

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(user, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            AnsiConsole.MarkupLine($"[bold]{user.DisplayName ?? user.ExternalId}[/]");
            AnsiConsole.MarkupLine($"External ID: {user.ExternalId}");
            AnsiConsole.MarkupLine($"Provider: {user.Provider}");
            AnsiConsole.MarkupLine($"Email: {user.Email ?? "-"}");
            AnsiConsole.MarkupLine($"Active: {(user.IsActive ? "Yes" : "No")}");

            if (user.Roles.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Roles:[/]");
                foreach (var role in user.Roles)
                {
                    var scope = role.ResourceInstanceId != null ? $" [dim](on {role.ResourceInstanceId})[/]" : "";
                    var app = role.ApplicationCode != null ? $" [dim]({role.ApplicationCode})[/]" : "";
                    AnsiConsole.MarkupLine($"  - {role.RoleCode}{app}{scope}");
                }
            }
        }, userArg, providerOption, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateProvisionCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var userArg = new Argument<string>("external-id", "External ID (OAuth sub claim)");
        var providerArg = new Argument<string>("provider", "Identity provider (e.g., andy-auth, azure-ad)");
        var emailOption = new Option<string?>(["--email", "-e"], "Email address");
        var nameOption = new Option<string?>(["--name", "-n"], "Display name");

        var cmd = new Command("provision", "Provision a new user") { userArg, providerArg, emailOption, nameOption };

        cmd.SetHandler(async (string externalId, string provider, string? email, string? name, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var response = await client.PostAsJsonAsync("api/subjects", new
            {
                ExternalId = externalId,
                Provider = provider,
                Email = email,
                DisplayName = name
            });

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Provisioned user '{externalId}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, userArg, providerArg, emailOption, nameOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateDeactivateCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var userArg = new Argument<string>("user", "User external ID");
        var forceOption = new Option<bool>(["--force", "-f"], "Skip confirmation");

        var cmd = new Command("deactivate", "Deactivate a user") { userArg, forceOption };

        cmd.SetHandler(async (string userId, bool force, string apiUrl, string? apiKey) =>
        {
            if (!force && !AnsiConsole.Confirm($"Deactivate user '{userId}'?", false))
                return;

            using var client = CreateClient(apiUrl, apiKey);

            var result = await client.GetFromJsonAsync<PagedResult<UserDto>>($"api/subjects?query={Uri.EscapeDataString(userId)}");
            var user = result?.Items.FirstOrDefault();

            if (user == null)
            {
                AnsiConsole.MarkupLine($"[red]User '{userId}' not found[/]");
                return;
            }

            var response = await client.PostAsync($"api/subjects/{user.Id}/deactivate", null);

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Deactivated user '{userId}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, userArg, forceOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static HttpClient CreateClient(string apiUrl, string? apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(apiUrl) };
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        return client;
    }
}

record UserDto(Guid Id, string ExternalId, string Provider, string? Email, string? DisplayName, bool IsActive);
record UserDetailDto(Guid Id, string ExternalId, string Provider, string? Email, string? DisplayName, bool IsActive, List<UserRoleDto> Roles);
record UserRoleDto(string RoleCode, string? ApplicationCode, string? ResourceInstanceId);
