using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Security;

public sealed class CoopDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string? ApplicationName { get; set; }

    public string? KeyRingPath { get; set; }

    public string? ProtectionMode { get; set; }

    public string? CertificateThumbprint { get; set; }
}

public sealed class ReverseProxyTrustOptions
{
    public const string SectionName = "ReverseProxy";

    public bool Enabled { get; set; }

    public string[] KnownProxies { get; set; } = [];

    public string[] KnownNetworks { get; set; } = [];
}

public static class ProductionSecurityConfiguration
{
    public static void AddFoundation(WebApplicationBuilder builder)
    {
        var isProduction = builder.Environment.IsProduction();
        builder.Services.AddOptions<CoopDataProtectionOptions>()
            .Bind(builder.Configuration.GetSection(CoopDataProtectionOptions.SectionName))
            .Validate(options => ValidateDataProtection(
                    options,
                    isProduction,
                    builder.Environment.ContentRootPath,
                    out _),
                "Production requires a durable Data Protection application name, absolute key-ring path, and DPAPI or Certificate key protection.")
            .ValidateOnStart();

        builder.Services.AddOptions<ReverseProxyTrustOptions>()
            .Bind(builder.Configuration.GetSection(ReverseProxyTrustOptions.SectionName))
            .Validate(options => ValidateReverseProxy(options, out _),
                "Forwarded headers require at least one valid explicit known proxy or network.")
            .ValidateOnStart();

        var dataProtectionOptions = builder.Configuration
            .GetSection(CoopDataProtectionOptions.SectionName)
            .Get<CoopDataProtectionOptions>() ?? new CoopDataProtectionOptions();
        var dataProtection = builder.Services.AddDataProtection();
        if (!string.IsNullOrWhiteSpace(dataProtectionOptions.ApplicationName))
            dataProtection.SetApplicationName(dataProtectionOptions.ApplicationName);
        if (!string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionOptions.KeyRingPath));

        if (string.Equals(dataProtectionOptions.ProtectionMode, "DPAPI", StringComparison.OrdinalIgnoreCase) &&
            OperatingSystem.IsWindows())
            dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: false);
        else if (string.Equals(dataProtectionOptions.ProtectionMode, "DPAPI", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DPAPI Data Protection key encryption is supported only on Windows.");
        else if (string.Equals(dataProtectionOptions.ProtectionMode, "Certificate", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(dataProtectionOptions.CertificateThumbprint))
            dataProtection.ProtectKeysWithCertificate(dataProtectionOptions.CertificateThumbprint);

        var proxyOptions = builder.Configuration
            .GetSection(ReverseProxyTrustOptions.SectionName)
            .Get<ReverseProxyTrustOptions>() ?? new ReverseProxyTrustOptions();
        if (proxyOptions.Enabled)
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.RequireHeaderSymmetry = true;
                foreach (var proxy in proxyOptions.KnownProxies)
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                foreach (var network in proxyOptions.KnownNetworks)
                    options.KnownNetworks.Add(ParseNetwork(network));
            });
        }
    }

    public static void ValidateProductionHostAndCors(IHostEnvironment environment, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => origin.Contains('*', StringComparison.Ordinal) ||
                                  !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                                  uri.Scheme is not ("http" or "https") ||
                                  uri.AbsolutePath != "/" ||
                                  !string.IsNullOrEmpty(uri.Query) ||
                                  !string.IsNullOrEmpty(uri.Fragment)))
        {
            throw new InvalidOperationException("CORS origins must be explicit absolute HTTP(S) origins; wildcards are prohibited.");
        }

        if (!environment.IsProduction())
            return;

        var allowedHosts = configuration["AllowedHosts"]?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (allowedHosts.Length == 0 || allowedHosts.Any(host =>
                host.Contains('*', StringComparison.Ordinal) ||
                host.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                host.Contains('/', StringComparison.Ordinal)))
            throw new InvalidOperationException("Production AllowedHosts must contain exact host names and cannot use '*'.");
    }

    public static bool ValidateDataProtection(
        CoopDataProtectionOptions options,
        bool isProduction,
        out string error) => ValidateDataProtection(options, isProduction, null, out error);

    public static bool ValidateDataProtection(
        CoopDataProtectionOptions options,
        bool isProduction,
        string? contentRootPath,
        out string error)
    {
        error = string.Empty;
        if (!isProduction)
            return true;
        if (string.IsNullOrWhiteSpace(options.ApplicationName))
            return Fail("DataProtection:ApplicationName is required.", out error);
        if (string.IsNullOrWhiteSpace(options.KeyRingPath) || !Path.IsPathFullyQualified(options.KeyRingPath))
            return Fail("DataProtection:KeyRingPath must be an absolute durable path.", out error);
        if (!string.IsNullOrWhiteSpace(contentRootPath) && IsWithinDirectory(options.KeyRingPath, contentRootPath))
            return Fail("DataProtection:KeyRingPath must be outside the application deployment directory.", out error);
        if (string.Equals(options.ProtectionMode, "DPAPI", StringComparison.OrdinalIgnoreCase))
            return OperatingSystem.IsWindows() || Fail("DPAPI key protection requires Windows.", out error);
        if (string.Equals(options.ProtectionMode, "Certificate", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(options.CertificateThumbprint))
            return true;
        return Fail("DataProtection:ProtectionMode must be DPAPI or Certificate; Certificate requires a thumbprint.", out error);
    }

    public static bool ValidateReverseProxy(ReverseProxyTrustOptions options, out string error)
    {
        error = string.Empty;
        if (!options.Enabled)
            return true;
        if (options.KnownProxies.Length == 0 && options.KnownNetworks.Length == 0)
            return Fail("At least one known proxy or network is required.", out error);
        if (options.KnownProxies.Any(value => !IPAddress.TryParse(value, out _)))
            return Fail("KnownProxies contains an invalid IP address.", out error);
        try
        {
            foreach (var network in options.KnownNetworks)
                _ = ParseNetwork(network);
        }
        catch (FormatException exception)
        {
            return Fail(exception.Message, out error);
        }
        return true;
    }

    private static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseNetwork(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var prefix) ||
            !int.TryParse(parts[1], out var prefixLength) ||
            prefixLength < 0 ||
            prefixLength > (prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
        {
            throw new FormatException($"Known network '{value}' is not valid CIDR notation.");
        }

        return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength);
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static bool IsWithinDirectory(string candidatePath, string directoryPath)
    {
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(directory, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }
}
