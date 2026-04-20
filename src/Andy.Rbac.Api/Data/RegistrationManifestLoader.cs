// Copyright (c) Rivoli AI 2026. All rights reserved.
using System.Text.Json;

namespace Andy.Rbac.Api.Data;

/// <summary>
/// Discovers and parses registration manifests on startup. See
/// <see cref="RegistrationManifest"/> for the wire format and the andy-service-template
/// docs for discovery conventions.
/// </summary>
public static class RegistrationManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<RegistrationManifest> LoadAll(IConfiguration configuration, ILogger logger)
    {
        var paths = ResolveSearchPaths(configuration);
        var manifests = new Dictionary<string, RegistrationManifest>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            foreach (var file in EnumerateManifestFiles(path, logger))
            {
                var manifest = TryLoad(file, logger);
                if (manifest is null) continue;
                if (manifests.ContainsKey(manifest.Service.Name)) continue;
                manifests[manifest.Service.Name] = manifest;
                logger.LogInformation("Loaded registration manifest for {Service} from {File}.",
                    manifest.Service.Name, file);
            }
        }

        logger.LogInformation("Registration manifest load complete: {Count} manifest(s).", manifests.Count);
        return manifests.Values.ToList();
    }

    private static IReadOnlyList<string> ResolveSearchPaths(IConfiguration configuration)
    {
        var paths = new List<string>();

        var configured = configuration.GetSection("Registrations:ManifestPaths").Get<string[]>();
        if (configured is not null) paths.AddRange(configured);

        var envVar = Environment.GetEnvironmentVariable("REGISTRATIONS__MANIFEST_PATHS");
        if (!string.IsNullOrWhiteSpace(envVar))
        {
            var sep = OperatingSystem.IsWindows() ? ';' : ':';
            paths.AddRange(envVar.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (paths.Count == 0)
        {
            const string defaultPath = "/etc/andy/registrations";
            if (Directory.Exists(defaultPath)) paths.Add(defaultPath);
        }

        return paths;
    }

    private static IEnumerable<string> EnumerateManifestFiles(string path, ILogger logger)
    {
        if (File.Exists(path)) { yield return path; yield break; }
        if (!Directory.Exists(path))
        {
            logger.LogDebug("Registration manifest path {Path} does not exist; skipping.", path);
            yield break;
        }
        foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly))
            yield return file;
    }

    private static RegistrationManifest? TryLoad(string file, ILogger logger)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var manifest = JsonSerializer.Deserialize<RegistrationManifest>(stream, JsonOptions);
            if (manifest is null || manifest.Service is null || string.IsNullOrWhiteSpace(manifest.Service.Name))
            {
                logger.LogWarning("Manifest {File} missing required 'service.name'; skipping.", file);
                return null;
            }
            return manifest;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Manifest {File} failed to parse; skipping.", file);
            return null;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Manifest {File} could not be read; skipping.", file);
            return null;
        }
    }
}
