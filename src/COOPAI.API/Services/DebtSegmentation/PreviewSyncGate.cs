namespace COOPAI.API.Services.DebtSegmentation;

/// <summary>
/// Serializes Preview workbook imports across independently updating sources.
/// Query-only analysis does not use this gate.
/// </summary>
public sealed class PreviewSyncGate
{
    internal SemaphoreSlim Semaphore { get; } = new(1, 1);
}
