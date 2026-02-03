using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace Andy.Rbac.Cli.Commands;

public static class RoleCommands
{
    public static Command Create(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var roleCommand = new Command("role", "Manage roles")
        {
            CreateListCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateGetCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateCreateCommand(apiUrlOption, apiKeyOption),
            CreateDeleteCommand(apiUrlOption, apiKeyOption),
            CreateAssignCommand(apiUrlOption, apiKeyOption),
            CreateRevokeCommand(apiUrlOption, apiKeyOption)
        };

        return roleCommand;
    }

    private static Command CreateListCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var appOption = new Option<string?>(["--app", "-a"], "Filter by application code");
        var cmd = new Command("list", "List all roles") { appOption };

        cmd.SetHandler(async (string? appCode, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var url = "api/roles" + (appCode != null ? $"?applicationCode={appCode}" : "");
            var roles = await client.GetFromJsonAsync<List<RoleDto>>(url);

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(roles, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            var table = new Table()
                .AddColumn("Code")
                .AddColumn("Name")
                .AddColumn("Application")
                .AddColumn("System");

            foreach (var role in roles ?? [])
            {
                table.AddRow(
                    role.Code,
                    role.Name,
                    role.ApplicationCode ?? "[dim](global)[/]",
                    role.IsSystem ? "[green]Yes[/]" : "No");
            }

            AnsiConsole.Write(table);
        }, appOption, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateGetCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var codeArg = new Argument<string>("code", "Role code");
        var cmd = new Command("get", "Get role details") { codeArg };

        cmd.SetHandler(async (string code, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var roles = await client.GetFromJsonAsync<List<RoleDetailDto>>("api/roles");
            var role = roles?.FirstOrDefault(r => r.Code == code);

            if (role == null)
            {
                AnsiConsole.MarkupLine($"[red]Role '{code}' not found[/]");
                return;
            }

            var fullRole = await client.GetFromJsonAsync<RoleDetailDto>($"api/roles/{role.Id}");

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(fullRole, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            AnsiConsole.MarkupLine($"[bold]{fullRole!.Name}[/] ({fullRole.Code})");
            if (fullRole.Description != null)
                AnsiConsole.MarkupLine($"[dim]{fullRole.Description}[/]");
            AnsiConsole.MarkupLine($"Application: {fullRole.ApplicationCode ?? "(global)"}");
            AnsiConsole.MarkupLine($"System: {(fullRole.IsSystem ? "Yes" : "No")}");

            if (fullRole.Permissions.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Permissions:[/]");
                foreach (var perm in fullRole.Permissions)
                {
                    AnsiConsole.MarkupLine($"  - {perm}");
                }
            }
        }, codeArg, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateCreateCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var codeArg = new Argument<string>("code", "Unique role code");
        var nameArg = new Argument<string>("name", "Display name");
        var appOption = new Option<string?>(["--app", "-a"], "Application code (omit for global role)");
        var parentOption = new Option<string?>(["--parent", "-p"], "Parent role code for inheritance");
        var descOption = new Option<string?>(["--description", "-d"], "Description");

        var cmd = new Command("create", "Create a new role") { codeArg, nameArg, appOption, parentOption, descOption };

        cmd.SetHandler(async (string code, string name, string? appCode, string? parentCode, string? description, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var response = await client.PostAsJsonAsync("api/roles", new
            {
                Code = code,
                Name = name,
                ApplicationCode = appCode,
                ParentRoleCode = parentCode,
                Description = description
            });

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Created role '{code}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, codeArg, nameArg, appOption, parentOption, descOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateDeleteCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var codeArg = new Argument<string>("code", "Role code");
        var forceOption = new Option<bool>(["--force", "-f"], "Skip confirmation");

        var cmd = new Command("delete", "Delete a role") { codeArg, forceOption };

        cmd.SetHandler(async (string code, bool force, string apiUrl, string? apiKey) =>
        {
            if (!force && !AnsiConsole.Confirm($"Delete role '{code}'?", false))
                return;

            using var client = CreateClient(apiUrl, apiKey);
            var roles = await client.GetFromJsonAsync<List<RoleDto>>("api/roles");
            var role = roles?.FirstOrDefault(r => r.Code == code);

            if (role == null)
            {
                AnsiConsole.MarkupLine($"[red]Role '{code}' not found[/]");
                return;
            }

            var response = await client.DeleteAsync($"api/roles/{role.Id}");

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Deleted role '{code}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, codeArg, forceOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateAssignCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var roleArg = new Argument<string>("role", "Role code");
        var userArg = new Argument<string>("user", "User external ID");
        var resourceOption = new Option<string?>(["--resource", "-r"], "Resource instance ID to scope the assignment");

        var cmd = new Command("assign", "Assign a role to a user") { roleArg, userArg, resourceOption };

        cmd.SetHandler(async (string roleCode, string userId, string? resourceId, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);

            // Get subject
            var subjects = await client.GetFromJsonAsync<PagedResult<SubjectDto>>(
                $"api/subjects?query={Uri.EscapeDataString(userId)}");
            var subject = subjects?.Items.FirstOrDefault();

            if (subject == null)
            {
                AnsiConsole.MarkupLine($"[red]User '{userId}' not found[/]");
                return;
            }

            var response = await client.PostAsJsonAsync($"api/subjects/{subject.Id}/roles", new
            {
                RoleCode = roleCode,
                ResourceInstanceId = resourceId
            });

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Assigned role '{roleCode}' to '{userId}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, roleArg, userArg, resourceOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateRevokeCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var roleArg = new Argument<string>("role", "Role code");
        var userArg = new Argument<string>("user", "User external ID");
        var resourceOption = new Option<string?>(["--resource", "-r"], "Resource instance ID");

        var cmd = new Command("revoke", "Revoke a role from a user") { roleArg, userArg, resourceOption };

        cmd.SetHandler(async (string roleCode, string userId, string? resourceId, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);

            var subjects = await client.GetFromJsonAsync<PagedResult<SubjectDto>>(
                $"api/subjects?query={Uri.EscapeDataString(userId)}");
            var subject = subjects?.Items.FirstOrDefault();

            if (subject == null)
            {
                AnsiConsole.MarkupLine($"[red]User '{userId}' not found[/]");
                return;
            }

            var url = $"api/subjects/{subject.Id}/roles/{roleCode}";
            if (resourceId != null)
                url += $"?resourceInstanceId={Uri.EscapeDataString(resourceId)}";

            var response = await client.DeleteAsync(url);

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Revoked role '{roleCode}' from '{userId}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, roleArg, userArg, resourceOption, apiUrlOption, apiKeyOption);

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

record RoleDto(Guid Id, string Code, string Name, string? Description, string? ApplicationCode, bool IsSystem);
record RoleDetailDto(Guid Id, string Code, string Name, string? Description, string? ApplicationCode, bool IsSystem, List<string> Permissions);
record SubjectDto(Guid Id, string ExternalId, string Provider, string? Email, string? DisplayName, bool IsActive);
record PagedResult<T>(List<T> Items, int Total, int Skip, int Take);
