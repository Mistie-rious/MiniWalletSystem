using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.EntityFrameworkCore;

using WalletBackend.Data;
using WalletBackend.Models;
using Document = iText.Layout.Document;


namespace WalletBackend.Services.ExportService;

public class ExportService: IExportService


{
    private readonly WalletContext _context;
    private readonly ILogger<ExportService> _logger;
    
    public ExportService(WalletContext context, ILogger<ExportService> logger)
    {
        _context = context;
        _logger = logger;
    }
    
    public async Task<string> ExportTransactionsToCsvAsync(Guid walletId, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                var query = _context.Transactions
                    .Where(t => t.WalletId == walletId);

                if (startDate.HasValue)
                {
                    query = query.Where(t => t.CreatedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(t => t.CreatedAt <= endDate.Value);
                }

                var transactions = await query
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();

                if (!transactions.Any())
                {
                    _logger.LogInformation("No transactions found for wallet {WalletId} to export.", walletId);
                    return null;
                }

                using (var memoryStream = new MemoryStream())
                using (var writer = new StreamWriter(memoryStream))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csv.WriteRecords(transactions.Select(t => new
                    {
                        TransactionHash = t.TransactionHash,
                        SenderAddress = t.SenderAddress,
                        ReceiverAddress = t.ReceiverAddress,
                        Amount = t.Amount,
                        Currency = t.Currency,
                        Status = t.Status.ToString(),
                        Type = t.Type.ToString(),
                        BlockNumber = t.BlockNumber,
                        CreatedAt = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        Description = t.Description
                    }));

                    await writer.FlushAsync();
                    var csvContent = Encoding.UTF8.GetString(memoryStream.ToArray());
                    _logger.LogInformation("Exported {TransactionCount} transactions to CSV for wallet {WalletId}.", transactions.Count, walletId);
                    return csvContent;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting transactions to CSV for wallet {WalletId}.", walletId);
                throw;
            }
        }
        
        
         public async Task<byte[]> ExportTransactionsToPdfAsync(Guid walletId, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                var query = _context.Transactions
                    .Where(t => t.WalletId == walletId);

                if (startDate.HasValue)
                {
                    query = query.Where(t => t.CreatedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(t => t.CreatedAt <= endDate.Value);
                }

                var transactions = await query
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();

                if (!transactions.Any())
                {
                    _logger.LogInformation("No transactions found for wallet {WalletId} to export to PDF.", walletId);
                    return null;
                }

                using (var memoryStream = new MemoryStream())
                {
                    using (var writer = new PdfWriter(memoryStream))
                    using (var pdf = new PdfDocument(writer))
                    {
                        var document = new Document(pdf);

                        // Add title
                        document.Add(new Paragraph($"Transaction History for Wallet {walletId}")
                            .SetFontSize(16)
                            .SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD)));

                        // Add date range if specified
                        if (startDate.HasValue || endDate.HasValue)
                        {
                            var dateRange = $"From: {(startDate.HasValue ? startDate.Value.ToString("yyyy-MM-dd") : "Beginning")} " +
                                            $"To: {(endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd") : "Now")}";
                            document.Add(new Paragraph(dateRange).SetFontSize(10));
                        }

                        // Create table with 10 columns
                        var table = new Table(UnitValue.CreatePercentArray(new float[] { 15, 15, 15, 10, 8, 8, 8, 8, 8, 15 }))
                            .UseAllAvailableWidth();

                        // Add headers
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Hash").SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Sender") .SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Receiver") .SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Amount").SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Currency").SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Status").SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Type").SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Block").SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Date").SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));
                        table.AddHeaderCell(new Cell().Add(new Paragraph("Description") .SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))));

                        // Add transaction rows
                        foreach (var tx in transactions)
                        {
                            table.AddCell(new Cell().Add(new Paragraph(tx.TransactionHash?.Substring(0, Math.Min(tx.TransactionHash.Length, 10)) + "...")));
                            table.AddCell(new Cell().Add(new Paragraph(tx.SenderAddress?.Substring(0, Math.Min(tx.SenderAddress.Length, 10)) + "...")));
                            table.AddCell(new Cell().Add(new Paragraph(tx.ReceiverAddress?.Substring(0, Math.Min(tx.ReceiverAddress.Length, 10)) + "...")));
                            table.AddCell(new Cell().Add(new Paragraph(tx.Amount.ToString("F6"))));
                            table.AddCell(new Cell().Add(new Paragraph(tx.Currency ?? "N/A")));
                            table.AddCell(new Cell().Add(new Paragraph(tx.Status.ToString())));
                            table.AddCell(new Cell().Add(new Paragraph(tx.Type.ToString())));
                            table.AddCell(new Cell().Add(new Paragraph(tx.BlockNumber ?? "N/A")));
                            table.AddCell(new Cell().Add(new Paragraph(tx.CreatedAt.ToString("yyyy-MM-dd HH:mm"))));
                            table.AddCell(new Cell().Add(new Paragraph(tx.Description ?? "N/A")));
                        }

                        document.Add(table);
                        document.Close();
                    }

                    _logger.LogInformation("Exported {TransactionCount} transactions to PDF for wallet {WalletId}.", transactions.Count, walletId);
                    return memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting transactions to PDF for wallet {WalletId}.", walletId);
                throw;
            }
        }
}