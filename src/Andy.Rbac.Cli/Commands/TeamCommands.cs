using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace Andy.Rbac.Cli.Commands;

public static class TeamCommands
{
    public static Command Create(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var teamCommand = new Command("team", "Manage teams")
        {
            CreateListCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateGetCommand(apiUrlOption, apiKeyOption, outputOption),
            CreateCreateCommand(apiUrlOption, apiKeyOption),
            CreateAddMemberCommand(apiUrlOption, apiKeyOption),
            CreateRemoveMemberCommand(apiUrlOption, apiKeyOption),
            CreateAssignRoleCommand(apiUrlOption, apiKeyOption)
        };

        return teamCommand;
    }

    private static Command CreateListCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var appOption = new Option<string?>(["--app", "-a"], "Filter by application code");
        var cmd = new Command("list", "List all teams") { appOption };

        cmd.SetHandler(async (string? appCode, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var url = "api/teams" + (appCode != null ? $"?applicationCode={appCode}" : "");
            var teams = await client.GetFromJsonAsync<List<TeamDto>>(url);

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(teams, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            var table = new Table()
                .AddColumn("Code")
                .AddColumn("Name")
                .AddColumn("Parent")
                .AddColumn("Members")
                .AddColumn("Active");

            foreach (var team in teams ?? [])
            {
                table.AddRow(
                    team.Code,
                    team.Name,
                    team.ParentTeamCode ?? "[dim]-[/]",
                    team.MemberCount.ToString(),
                    team.IsActive ? "[green]Yes[/]" : "[red]No[/]");
            }

            AnsiConsole.Write(table);
        }, appOption, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateGetCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption, Option<OutputFormat> outputOption)
    {
        var codeArg = new Argument<string>("code", "Team code");
        var cmd = new Command("get", "Get team details") { codeArg };

        cmd.SetHandler(async (string code, string apiUrl, string? apiKey, OutputFormat output) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var team = await client.GetFromJsonAsync<TeamDetailDto>($"api/teams/by-code/{code}");

            if (team == null)
            {
                AnsiConsole.MarkupLine($"[red]Team '{code}' not found[/]");
                return;
            }

            if (output == OutputFormat.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(team, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            AnsiConsole.MarkupLine($"[bold]{team.Name}[/] ({team.Code})");
            if (team.Description != null)
                AnsiConsole.MarkupLine($"[dim]{team.Description}[/]");
            AnsiConsole.MarkupLine($"Active: {(team.IsActive ? "Yes" : "No")}");

            if (team.Members.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Members:[/]");
                foreach (var member in team.Members)
                {
                    AnsiConsole.MarkupLine($"  - {member.SubjectDisplayName} ({member.MembershipRole})");
                }
            }

            if (team.Roles.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Assigned Roles:[/]");
                foreach (var role in team.Roles)
                {
                    AnsiConsole.MarkupLine($"  - {role.RoleCode}: {role.RoleName}");
                }
            }
        }, codeArg, apiUrlOption, apiKeyOption, outputOption);

        return cmd;
    }

    private static Command CreateCreateCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var codeArg = new Argument<string>("code", "Unique team code");
        var nameArg = new Argument<string>("name", "Display name");
        var parentOption = new Option<string?>(["--parent", "-p"], "Parent team code");
        var appOption = new Option<string?>(["--app", "-a"], "Application code");
        var descOption = new Option<string?>(["--description", "-d"], "Description");

        var cmd = new Command("create", "Create a new team") { codeArg, nameArg, parentOption, appOption, descOption };

        cmd.SetHandler(async (string code, string name, string? parent, string? app, string? desc, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);
            var response = await client.PostAsJsonAsync("api/teams", new
            {
                Code = code,
                Name = name,
                ParentTeamCode = parent,
                ApplicationCode = app,
                Description = desc
            });

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Created team '{code}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, codeArg, nameArg, parentOption, appOption, descOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateAddMemberCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var teamArg = new Argument<string>("team", "Team code");
        var userArg = new Argument<string>("user", "User external ID");
        var providerOption = new Option<string>(["--provider", "-p"], () => "andy-auth", "Identity provider");
        var roleOption = new Option<string>(["--role", "-r"], () => "Member", "Membership role (Member, Admin, Owner)");

        var cmd = new Command("add-member", "Add a member to a team") { teamArg, userArg, providerOption, roleOption };

        cmd.SetHandler(async (string teamCode, string userId, string provider, string role, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);

            var team = await client.GetFromJsonAsync<TeamDto>($"api/teams/by-code/{teamCode}");
            if (team == null)
            {
                AnsiConsole.MarkupLine($"[red]Team '{teamCode}' not found[/]");
                return;
            }

            var response = await client.PostAsJsonAsync($"api/teams/{team.Id}/members", new
            {
                SubjectExternalId = userId,
                SubjectProvider = provider,
                MembershipRole = role
            });

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Added '{userId}' to team '{teamCode}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, teamArg, userArg, providerOption, roleOption, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateRemoveMemberCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var teamArg = new Argument<string>("team", "Team code");
        var userArg = new Argument<string>("user", "User external ID");

        var cmd = new Command("remove-member", "Remove a member from a team") { teamArg, userArg };

        cmd.SetHandler(async (string teamCode, string userId, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);

            var team = await client.GetFromJsonAsync<TeamDetailDto>($"api/teams/by-code/{teamCode}");
            if (team == null)
            {
                AnsiConsole.MarkupLine($"[red]Team '{teamCode}' not found[/]");
                return;
            }

            var member = team.Members.FirstOrDefault(m => m.SubjectExternalId == userId);
            if (member == null)
            {
                AnsiConsole.MarkupLine($"[red]User '{userId}' is not a member of team '{teamCode}'[/]");
                return;
            }

            var response = await client.DeleteAsync($"api/teams/{team.Id}/members/{member.SubjectId}");

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Removed '{userId}' from team '{teamCode}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, teamArg, userArg, apiUrlOption, apiKeyOption);

        return cmd;
    }

    private static Command CreateAssignRoleCommand(Option<string> apiUrlOption, Option<string?> apiKeyOption)
    {
        var teamArg = new Argument<string>("team", "Team code");
        var roleArg = new Argument<string>("role", "Role code");

        var cmd = new Command("assign-role", "Assign a role to a team (all members inherit)") { teamArg, roleArg };

        cmd.SetHandler(async (string teamCode, string roleCode, string apiUrl, string? apiKey) =>
        {
            using var client = CreateClient(apiUrl, apiKey);

            var team = await client.GetFromJsonAsync<TeamDto>($"api/teams/by-code/{teamCode}");
            if (team == null)
            {
                AnsiConsole.MarkupLine($"[red]Team '{teamCode}' not found[/]");
                return;
            }

            var response = await client.PostAsJsonAsync($"api/teams/{team.Id}/roles", new { RoleCode = roleCode });

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[green]Assigned role '{roleCode}' to team '{teamCode}'[/]");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[red]Error: {error}[/]");
            }
        }, teamArg, roleArg, apiUrlOption, apiKeyOption);

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

record TeamDto(Guid Id, string Code, string Name, string? Description, string? ParentTeamCode, string? ApplicationCode, int MemberCount, bool IsActive);
record TeamDetailDto(Guid Id, string Code, string Name, string? Description, string? ParentTeamCode, string? ApplicationCode, int MemberCount, bool IsActive, List<TeamMemberDto> Members, List<TeamRoleDto> Roles);
record TeamMemberDto(Guid SubjectId, string SubjectExternalId, string SubjectDisplayName, string MembershipRole);
record TeamRoleDto(string RoleCode, string RoleName, string? ResourceInstanceId);
