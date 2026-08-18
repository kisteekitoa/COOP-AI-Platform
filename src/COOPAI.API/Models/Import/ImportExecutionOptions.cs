namespace COOPAI.API.Models.Import;

public class ImportExecutionOptions
{
    public string? ValidationFileHash { get; init; }

    public IReadOnlyCollection<ApprovedErrorSkipSelection> ApprovedErrorSkips { get; init; }
        = Array.Empty<ApprovedErrorSkipSelection>();
}

public class ApprovedErrorSkipSelection
{
    public int RowNumber { get; set; }

    public string ContractNo { get; set; } = string.Empty;
}
