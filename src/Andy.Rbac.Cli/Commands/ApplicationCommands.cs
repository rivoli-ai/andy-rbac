using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace Andy.Rbac.Cli.Commands;

public static class ApplicationCommands
{
    public static Command Create(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var appCommand = new Command("app", "Manage applications")
        {
            new Command("list", "List all applications").Also(cmd =>
            {
                cmd.SetHandler(async (string apiUrl, string? apiKey, OutputFormat output) =>
                {
                    using var client = CreateClient(apiUrl, apiKey);
                    var apps = await client.GetFromJsonAsync<List<ApplicationDto>>("api/applications");

                    if (output == OutputFormat.Json)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true }));
                        return;
                    }

                    var table = new Table()
                        .AddColumn("Code")
                        .AddColumn("Name")
                        .AddColumn("Resources")
                        .AddColumn("Roles");

                    foreach (var app in apps ?? [])
                    {
                        table.AddRow(app.Code, app.Name, app.ResourceTypeCount.ToString(), app.RoleCount.ToString());
                    }

                    AnsiConsole.Write(table);
                }, apiUrlOption, apiKeyOption, outputOption);
            }),

            CreateGetCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateCreateCommand(apiUrlOption, apiKeyOption),
            CreateDeleteCommand(apiUrlOption, apiKeyOption),
            CreateAddResourceTypeCommand(apiUrlOption, apiKeyOption)
        };

        return appCommand;
    }

    private static Command CreateGetCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var codeArg = new Argument<string>("code", "Application code");
        var cmd = new Command("get", "Get application details") { codeArg };

        cmd.SetHandler(async (string code, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var app = await client.GetFromJsonAsync<ApplicationDetailDto>($"api/applications/by-code/{code}");

            if (app == null)
            {
                AnsiConsole.MarkupLine($"[red]Application '{code}' not found[/]");
                return;
            }

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(app, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            AnsiConsole.MarkupLine($"[bold]{app.Name}[/] ({app.Code})");
            if (app.Description != null)
                AnsiConsole.MarkupLine($"[dim]{app.Description}[/]");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Resource Types:[/]");
            foreach (var rt in app.ResourceTypes)
            {
                AnsiConsole.MarkupLine($"  - {rt.Code}: {rt.Name} {(rt.SupportsInstances ? "[dim](instances)[/]" : "")}");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Roles:[/]");
            foreach (var role in app.Roles)
            {
                AnsiConsole.MarkupLine($"  - {role.Code}: {role.Name} {(role.IsSystem ? "[dim](system)[/]" : "")}");
            }
        }, codeArg, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateCreateCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var codeArg = new Argument<string>("code", "Unique application code");
        var nameArg = new Argument<string>("name", "Display name");
        var descOption = new Option<string?>(["--description", "-d"], "Description");

        var cmd = new Command("create", "Create a new application") { codeArg, nameArg, descOption };

        cmd.SetHandler(async (string code, string name, string? description, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var response = await client.PostAsJsonAsync("api/applications", new { Code = code, Name = name, Description = description });

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Created application '{code}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, codeArg, nameArg, descOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateDeleteCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var codeArg = new Argument<string>("code", "Application code");
        var forceOption = new Option<bool>(["--force", "-f"], "Skip confirmation");

        var cmd = new Command("delete", "Delete an application") { codeArg, forceOption };

        cmd.SetHandler(async (string code, bool force, string apiUrl, string? apiKey) =>
        {
            if (!force)
            {
                if (!AnsiConsole.Confirm($"Delete application '{code}' and all associated data?", false))
                    return;
            }

            using var client = CreateClient(apiUrl, apiKey);

            // Get app ID first
            var app = await client.GetFromJsonAsync<ApplicationDto>($"api/applications/by-code/{code}");
            if (app == null)
            {
                AnsiConsole.MarkupLine($"[red]Application '{code}' not found[/]");
                return;
            }

            var response = await client.DeleteAsync($"api/applications/{app.Id}");

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Deleted application '{code}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, codeArg, forceOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateAddResourceTypeCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var appCodeArg = new Argument<string>("app-code", "Application code");
        var codeArg = new Argument<string>("code", "Resource type code");
        var nameArg = new Argument<string>("name", "Display name");
        var noInstancesOption = new Option<bool>("--no-instances", "Resource type does not support instances");

        var cmd = new Command("add-resource-type", "Add a resource type to an application")
        {
            appCodeArg, codeArg, nameArg, noInstancesOption
        };

        cmd.SetHandler(async (string appCode, string code, string name, bool noInstances, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);

            var app = await client.GetFromJsonAsync<ApplicationDto>($"api/applications/by-code/{appCode}");
            if (app == null)
            {
                AnsiConsole.MarkupLine($"[red]Application '{appCode}' not found[/]");
                return;
            }

            var response = await client.PostAsJsonAsync($"api/applications/{app.Id}/resource-types",
                new { Code = code, Name = name, SupportsInstances = !noInstances });

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Added resource type '{code}' to '{appCode}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, appCodeArg, codeArg, nameArg, noInstancesOption, apiUrlOption, apiKeyOption);

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

internal static class CommandExtensions
{
    public static Command Also(this Command cmd, Action<Command> configure)
    {
        configure(cmd);
        return cmd;
    }
}

// DTOs for deserialization
record ApplicationDto(Guid Id, string Code, string Name, string? Description, int ResourceTypeCount, int RoleCount);
record ApplicationDetailDto(Guid Id, string Code, string Name, string? Description, List<ResourceTypeDto> ResourceTypes, List<RoleSummaryDto> Roles);
record ResourceTypeDto(Guid Id, string Code, string Name, string? Description, bool SupportsInstances);
record RoleSummaryDto(Guid Id, string Code, string Name, bool IsSystem);
