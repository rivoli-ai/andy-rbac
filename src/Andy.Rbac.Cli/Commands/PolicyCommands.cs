// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace Andy.Rbac.Cli.Commands;

public static class PolicyCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    public static Command Create(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var policyCommand = new Command("policy", "Manage policies (Epic V)")
        {
            CreateListCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateGetCommand(apiUrlOption, apiKeyOption, outputOption),
        };

        return policyCommand;
    }

    private static Command CreateListCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var criticalityOption = new Option<string?>(
            ["--criticality", "-c"],
            "Filter by criticality (Low, Medium, High, Critical)");

        var cmd = new Command("list", "List all policies in the catalog") { criticalityOption };

        cmd.SetHandler(async (string? criticality, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var policies = await client.GetFromJsonAsync<List<PolicyDto>>("api/policies", JsonOptions) ?? [];

            if (!string.IsNullOrEmpty(criticality))
            {
                policies = policies
                    .Where(p => string.Equals(p.Criticality, criticality, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(policies, JsonOptions));
                return;
            }

            if (output == OutputFormat.Csv)
            {
                Console.WriteLine("code,name,criticality,system,description");
                foreach (var p in policies)
                {
                    Console.WriteLine($"{Csv(p.Code)},{Csv(p.Name)},{Csv(p.Criticality)},{p.IsSystem},{Csv(p.Description ?? string.Empty)}");
                }
                return;
            }

            var table = new Table()
                .AddColumn("Code")
                .AddColumn("Name")
                .AddColumn("Criticality")
                .AddColumn("System")
                .AddColumn("Description");

            foreach (var p in policies)
            {
                table.AddRow(
                    p.Code,
                    p.Name,
                    p.Criticality,
                    p.IsSystem ? "yes" : "no",
                    p.Description ?? "[dim]—[/]");
            }

            AnsiConsole.Write(table);
        }, criticalityOption, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateGetCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var codeArg = new Argument<string>("code", "Policy code (e.g., 'high-risk') or UUID");
        var cmd = new Command("get", "Get a policy by code or id") { codeArg };

        cmd.SetHandler(async (string code, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);

            // Resolve via /by-code first; fall back to /{id} if it parses as a Guid.
            var path = Guid.TryParse(code, out _) ? $"api/policies/{code}" : $"api/policies/by-code/{code}";

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(path);
            }
            catch (HttpRequestException ex)
            {
                AnsiConsole.MarkupLine($"[red]Could not reach RBAC API at {apiUrl}: {ex.Message}[/]");
                Environment.Exit(3);
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                AnsiConsole.MarkupLine($"[red]Policy not found: {code}[/]");
                Environment.Exit(2);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error ({(int)response.StatusCode}): {error}[/]");
                Environment.Exit(4);
                return;
            }

            var policy = await response.Content.ReadFromJsonAsync<PolicyDto>(JsonOptions);
            if (policy == null)
            {
                AnsiConsole.MarkupLine("[red]Empty response from API[/]");
                Environment.Exit(4);
                return;
            }

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(policy, JsonOptions));
                return;
            }

            AnsiConsole.MarkupLine($"[bold]{policy.Name}[/] ({policy.Code})");
            AnsiConsole.MarkupLine($"[dim]Criticality:[/] {policy.Criticality}");
            AnsiConsole.MarkupLine($"[dim]System:[/] {(policy.IsSystem ? "yes" : "no")}");
            if (!string.IsNullOrEmpty(policy.Description))
                AnsiConsole.MarkupLine($"[dim]Description:[/] {policy.Description}");

            if (policy.Rules is { Count: > 0 })
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Rules:[/]");
                foreach (var (k, v) in policy.Rules)
                {
                    AnsiConsole.MarkupLine($"  [dim]{k}:[/] {v}");
                }
            }
        }, codeArg, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static HttpClient CreateClient(string apiUrl, string? apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(apiUrl) };
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        return client;
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

internal record PolicyDto(
    Guid Id,
    string Code,
    string Name,
    string Criticality,
    Dictionary<string, object>? Rules,
    string? Description,
    bool IsSystem);
