using Microsoft.AspNetCore.Http;

namespace COOPAI.API.Models.Import;

public class ImportFileRequest
{
    public IFormFile? File { get; set; }

    public string? ValidationFileHash { get; set; }

    public List<ApprovedErrorSkipSelection> SkipRows { get; set; } = new();
}
