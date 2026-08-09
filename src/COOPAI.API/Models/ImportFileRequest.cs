using Microsoft.AspNetCore.Http;

namespace COOPAI.API.Models.Import;

public class ImportFileRequest
{
    public IFormFile? File { get; set; }
}