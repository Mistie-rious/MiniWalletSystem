using System.Text;
using Microsoft.AspNetCore.Mvc;
using WalletBackend.Services.ExportService;

namespace WalletBackend.Controllers;
[Route("api/[controller]")]
[ApiController]
public class ExportController : ControllerBase


{
    private readonly ILogger<ExportController> _logger;
    private readonly IExportService _exportService;
    
    public ExportController(IExportService exportService, ILogger<ExportController> logger )
    {
        _exportService = exportService;
        _logger = logger;


    }

    [HttpGet("csv/{walletId}")]
    public async Task<IActionResult> ExportTransactionsToCsv(
        Guid walletId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            _logger.LogInformation("This executes");
            var csvContent = await _exportService.ExportTransactionsToCsvAsync(walletId, startDate, endDate);
            if (string.IsNullOrEmpty(csvContent))
            {
                return NotFound(new { Message = "No transactions found to export" });
            }

            var bytes = Encoding.UTF8.GetBytes(csvContent);
            return File(bytes, "text/csv", $"transactions_{walletId}_{DateTime.UtcNow:yyyyMMdd}.csv");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Message = $"Failed to export transactions: {ex}" });
        }
    }

    [HttpGet("pdf/{walletId}")]
    public async Task<IActionResult> ExportTransactionsToPdf(
        Guid walletId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var pdfBytes = await _exportService.ExportTransactionsToPdfAsync(walletId, startDate, endDate);
            if (pdfBytes == null)
            {
                return NotFound(new { Message = "No transactions found to export" });
            }

            return File(pdfBytes, "application/pdf", $"transactions_{walletId}_{DateTime.UtcNow:yyyyMMdd}.pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = $"Failed to export transactions to PDF: {ex.Message}" });
        }
    }
}