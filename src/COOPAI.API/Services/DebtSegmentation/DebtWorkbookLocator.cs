using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.DebtSegmentation;

public sealed record DebtWorkbookLocation(
    bool Found,
    string Path,
    IReadOnlyList<string> Diagnostics);

public interface IDebtWorkbookLocator
{
    DebtWorkbookLocation Locate();
}

public sealed class DebtWorkbookLocator(
    IOptions<DebtSegmentationPreviewOptions> options,
    IHostEnvironment environment,
    OperationalDebtWorkbookReader reader) : IDebtWorkbookLocator
{
    public DebtWorkbookLocation Locate()
    {
        var configured = options.Value.WorkbookPath;
        if (string.IsNullOrWhiteSpace(configured))
            return new DebtWorkbookLocation(false, string.Empty, ["Debt segmentation workbook path is not configured."]);

        var resolved = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
        if (DebtWorkbookAcquirer.IsNetworkPath(resolved))
        {
            if (Path.GetExtension(resolved).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(resolved).StartsWith("~$", StringComparison.Ordinal))
                return new DebtWorkbookLocation(true, resolved, []);
            return new DebtWorkbookLocation(false, resolved,
                ["The configured network source must be an exact .xlsx workbook path."]);
        }
        if (File.Exists(resolved))
        {
            var probe = reader.Probe(resolved);
            return new DebtWorkbookLocation(probe.IsMatch, resolved, probe.Issues);
        }

        if (!Directory.Exists(resolved))
            return new DebtWorkbookLocation(false, resolved, ["Controlled workbook file or directory was not found."]);

        var diagnostics = new List<string>();
        foreach (var path in Directory.EnumerateFiles(resolved, "*.xlsx", SearchOption.TopDirectoryOnly)
                     .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
                     .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                     .ThenByDescending(path => new FileInfo(path).Length))
        {
            var probe = reader.Probe(path);
            if (probe.IsMatch)
                return new DebtWorkbookLocation(true, path, diagnostics);
            diagnostics.Add($"{Path.GetFileName(path)}: {string.Join(" ", probe.Issues)}");
        }

        if (diagnostics.Count == 0)
            diagnostics.Add("No completed .xlsx candidates were found in the controlled directory.");
        return new DebtWorkbookLocation(false, resolved, diagnostics);
    }
}
