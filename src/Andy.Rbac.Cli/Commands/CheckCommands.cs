using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace Andy.Rbac.Cli.Commands;

public static class CheckCommands
{
    public static Command Create(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var checkCommand = new Command("check", "Check permissions")
        {
            CreatePermissionCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateListPermissionsCommand(apiUrlOption, apiKeyOption, outputOption)
        };

        return checkCommand;
    }

    private static Command CreatePermissionCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var userArg = new Argument<string>("user", "User external ID");
        var permArg = new Argument<string>("permission", "Permission code (e.g., andy-docs:document:read)");
        var resourceOption = new Option<string?>(["--resource", "-r"], "Resource instance ID");

        var cmd = new Command("permission", "Check if a user has a permission") { userArg, permArg, resourceOption };

        cmd.SetHandler(async (string userId, string permission, string? resourceId, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var response = await client.PostAsJsonAsync("api/check", new
            {
                SubjectId = userId,
                Permission = permission,
                ResourceInstanceId = resourceId
            });

            var result = await response.Content.ReadFromJsonAsync<CheckResult>();

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            if (result?.Allowed == true)
            {
                AnsiConsole.MarkupLine($"[green]✓ ALLOWED[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗ DENIED[/]: {result?.Reason ?? "Unknown"}");
            }
        }, userArg, permArg, resourceOption, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateListPermissionsCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var userArg = new Argument<string>("user", "User external ID");
        var appOption = new Option<string?>(["--app", "-a"], "Filter by application code");

        var cmd = new Command("list-permissions", "List all permissions for a user") { userArg, appOption };

        cmd.SetHandler(async (string userId, string? appCode, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var url = $"api/check/permissions/{Uri.EscapeDataString(userId)}";
            if (appCode != null)
                url += $"?applicationCode={appCode}";

            var result = await client.GetFromJsonAsync<PermissionsResult>(url);

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result?.Permissions, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            if (result?.Permissions.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No permissions found[/]");
                return;
            }

            // Group by app
            var grouped = (result?.Permissions ?? [])
                .GroupBy(p => p.Split(':')[0])
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                AnsiConsole.MarkupLine($"[bold]{group.Key}[/]");
                foreach (var perm in group.OrderBy(p => p))
                {
                    var parts = perm.Split(':');
                    if (parts.Length >= 3)
                    {
                        AnsiConsole.MarkupLine($"  [dim]{parts[1]}[/]:{parts[2]}");
                    }
                }
            }
        }, userArg, appOption, apiUrlOption, apiKeyOption, outputOption);

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

record CheckResult(bool Allowed, string? Reason);
record PermissionsResult(List<string> Permissions);
