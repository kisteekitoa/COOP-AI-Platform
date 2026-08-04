using COOPAI.API.Services.Import;
using Microsoft.AspNetCore.Mvc;

namespace COOPAI.API.Controllers;

[ApiController]
[Route("api/import")]
public class ImportController : ControllerBase
{
    private readonly IExcelImportService _service;

    public ImportController(IExcelImportService service)
    {
        _service = service;
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new
        {
            success = true,
            message = "COOP-AI Import Engine Ready"
        });
    }

    [HttpGet("preview")]
    public IActionResult Preview()
    {
        var filePath = @"D:\Import\Loan.xlsx";

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new
            {
                success = false,
                message = $"File not found : {filePath}"
            });
        }

        var result = _service.Preview(filePath);

        return Ok(result);
    }
}