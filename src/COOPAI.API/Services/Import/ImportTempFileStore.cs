using COOPAI.API.Security;
using Microsoft.Extensions.Options;

namespace COOPAI.API.Services.Import;

public interface IImportTempFileStore
{
    Task<ImportTempFileLease> SaveAsync(IFormFile file, CancellationToken cancellationToken);

    void DeleteStaleFiles();
}

public sealed class ImportUploadException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ImportTempFileLease : IAsyncDisposable
{
    private readonly ImportTempFileStore _owner;
    private bool _disposed;

    internal ImportTempFileLease(ImportTempFileStore owner, string filePath)
    {
        _owner = owner;
        FilePath = filePath;
    }

    public string FilePath { get; }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _owner.DeleteOwnedFile(FilePath);
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class ImportTempFileStore : IImportTempFileStore
{
    private const string OwnedFilePrefix = "coopai-import-";
    private readonly ImportUploadOptions _options;
    private readonly string _rootPath;
    private readonly HashSet<string> _allowedExtensions;

    public ImportTempFileStore(IOptions<ImportUploadOptions> options)
    {
        _options = options.Value;
        _rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.TempDirectory)
            ? Path.Combine(Path.GetTempPath(), "COOPAI-Imports")
            : _options.TempDirectory);
        _allowedExtensions = _options.AllowedExtensions
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ImportTempFileLease> SaveAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
            throw new ImportUploadException("FileRequired", "A non-empty Excel workbook is required.");
        if (file.Length > _options.MaxUploadBytes)
            throw new ImportUploadException("UploadTooLarge", $"The workbook exceeds the {_options.MaxUploadBytes} byte upload limit.");

        var extension = NormalizeExtension(Path.GetExtension(Path.GetFileName(file.FileName)));
        if (!_allowedExtensions.Contains(extension))
            throw new ImportUploadException("UnsupportedFileType", "Only configured Excel workbook types are supported.");

        Directory.CreateDirectory(_rootPath);
        DeleteStaleFiles();
        var filePath = Path.Combine(_rootPath, $"{OwnedFilePrefix}{Guid.NewGuid():N}{extension}");

        try
        {
            await using var source = file.OpenReadStream();
            await using var destination = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            long totalBytes = 0;
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                    break;
                totalBytes += bytesRead;
                if (totalBytes > _options.MaxUploadBytes)
                    throw new ImportUploadException("UploadTooLarge", $"The workbook exceeds the {_options.MaxUploadBytes} byte upload limit.");
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            return new ImportTempFileLease(this, filePath);
        }
        catch
        {
            DeleteOwnedFile(filePath);
            throw;
        }
    }

    public void DeleteStaleFiles()
    {
        if (!Directory.Exists(_rootPath))
            return;

        var cutoff = DateTime.UtcNow.AddHours(-_options.RetentionHours);
        foreach (var filePath in Directory.EnumerateFiles(_rootPath, $"{OwnedFilePrefix}*"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(filePath) < cutoff)
                    DeleteOwnedFile(filePath);
            }
            catch (IOException)
            {
                // An active import owns the file. A later retention pass can retry.
            }
            catch (UnauthorizedAccessException)
            {
                // Do not broaden permissions or delete outside subsystem ownership.
            }
        }
    }

    internal void DeleteOwnedFile(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var parent = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (!string.Equals(parent, _rootPath, StringComparison.OrdinalIgnoreCase) ||
            !fileName.StartsWith(OwnedFilePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete a file not owned by the import temp subsystem.");
        }

        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    private static string NormalizeExtension(string extension) =>
        extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
}
