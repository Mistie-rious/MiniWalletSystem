using System.Globalization;
using System.Text;
using Azure.Core;
using CsvHelper;

using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.EntityFrameworkCore;

using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Models.Enums;
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
        
        var wallet = await _context.Wallets
            .Include(w => w.User)
            .FirstOrDefaultAsync(w => w.Id == walletId);
        
        var username = wallet?.User.UserName;

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
            
            _logger.LogInformation("No transactions found for {username} to export.");
            // Return empty CSV instead of null
            return string.Empty;
        }
        const decimal ethToNairaRate = 4_500_000;
        var totalAmount = transactions.Sum(t => t.Amount);
        decimal totalNaira = transactions.Sum(t => t.Amount * ethToNairaRate);
        var ngCulture = CultureInfo.CreateSpecificCulture("en-NG");
        string totalNairaFormatted = totalNaira.ToString("C2", ngCulture);

        using (var memoryStream = new MemoryStream())
        using (var writer = new StreamWriter(memoryStream))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            // Add null handling configuration
            csv.Context.TypeConverterOptionsCache.GetOptions<string>();
            
            // Create records with null-safe property access
           

            var records = transactions.Select(t =>
            {
                // Determine direction from your Type enum
                var incoming = t.Type == TransactionType.Credit ? t.Amount : 0m;
                var outgoing = t.Type == TransactionType.Debit  ? t.Amount : 0m;

                // Convert to Naira
                var inNaira  = (incoming * ethToNairaRate).ToString("C2", ngCulture);
                var outNaira = (outgoing * ethToNairaRate).ToString("C2", ngCulture);

                return new
                {
                    IncomingAmount   = incoming,
                    IncomingNaira    = inNaira,
                    OutgoingAmount   = outgoing,
                    OutgoingNaira    = outNaira,
                    t.Status,
                    t.Type,
                    CreatedAt        = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    Description      = t.Description ?? string.Empty,
                };
            });



            // Write headers first to ensure they're always included
            csv.WriteHeader<dynamic>();
            csv.NextRecord();
            
            
            csv.WriteRecords(records);
            var totalIn  = transactions
                .Where(t => t.Type == TransactionType.Credit)
                .Sum(t => t.Amount);
            var totalOut = transactions
                .Where(t => t.Type == TransactionType.Debit)
                .Sum(t => t.Amount);

            var totalInN  = (totalIn  * ethToNairaRate).ToString("C2", ngCulture);
            var totalOutN = (totalOut * ethToNairaRate).ToString("C2", ngCulture);


            csv.NextRecord();
            csv.WriteField("Total Credit");
            csv.WriteField(totalIn.ToString("F6"));
            csv.WriteField(totalInN);
            csv.WriteField("Total Debit");
            csv.WriteField(totalOut.ToString("F6"));
            csv.WriteField(totalOutN);
            csv.NextRecord();


            await writer.FlushAsync();
            memoryStream.Position = 0; // Reset position to read from start
            var csvContent = Encoding.UTF8.GetString(memoryStream.ToArray());
            _logger.LogInformation("Exported {TransactionCount} transactions to CSV for wallet {WalletId}.", transactions.Count, walletId);
            return csvContent;
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error exporting transactions to CSV for wallet {WalletId}.", walletId);
        throw new Exception($"Failed to export transactions: {ex}", ex);
    }
}
        
        
        public async Task<byte[]> ExportTransactionsToPdfAsync(Guid walletId, DateTime? startDate, DateTime? endDate)
{
    try
    {
        var query = _context.Transactions
            .Where(t => t.WalletId == walletId);

        var wallet = await _context.Wallets
            .Include(w => w.User)
            .FirstOrDefaultAsync(w => w.Id == walletId);
        
        var username = wallet?.User?.UserName;

      

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
            _logger.LogInformation("No transactions found for {username} to export to PDF.");
            // Consider returning an empty PDF with a message instead of throwing an exception
            using (var memoryStream = new MemoryStream())
            {
                using (var writer = new PdfWriter(memoryStream))
                using (var pdf = new PdfDocument(writer))
                {
                    var document = new Document(pdf);
                    document.Add(new Paragraph("No transactions found for the specified period.")
                        .SetFontSize(14)
                        .SetTextAlignment(TextAlignment.CENTER));
                    document.Close();
                }
                return memoryStream.ToArray();
            }
        }

        using (var memoryStream = new MemoryStream())
        {
            using (var writer = new PdfWriter(memoryStream))
            using (var pdf = new PdfDocument(writer))
            {
                // Set document to landscape for more space
                var document = new Document(pdf, PageSize.A4.Rotate());
                
                // Add title with more space
                document.Add(new Paragraph($"Transaction History for {username}")
                    .SetFontSize(16)
                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))
                    .SetMarginBottom(10));

                // Add date range if specified with better formatting
                if (startDate.HasValue || endDate.HasValue)
                {
                    var dateRange = $"Period: {(startDate.HasValue ? startDate.Value.ToString("yyyy-MM-dd") : "Beginning")} to " +
                                    $"{(endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd") : "Present")}";
                    document.Add(new Paragraph(dateRange)
                        .SetFontSize(10)
                        .SetMarginBottom(15));
                }
                
                var totalAmount = transactions.Sum(t => t.Amount);
                

                var table = new Table(UnitValue.CreatePercentArray(
                        new float[] { 10, 10, 10, 12, 12, 19, 21 }))
                    .UseAllAvailableWidth();
                
                // Define styles
                Style headerStyle = new Style()
                    .SetBackgroundColor(new DeviceRgb(240, 240, 240))
                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))
                    .SetFontSize(10);
                
                Style cellStyle = new Style()
                    .SetPadding(5)
                    .SetFontSize(9);
                
                Style alternateCellStyle = new Style()
                    .SetBackgroundColor(new DeviceRgb(249, 249, 249))
                    .SetPadding(5)
                    .SetFontSize(9);

                // Add headers with styling
         
               
                AddHeaderCell(table, "Incoming (N)",      headerStyle);
                AddHeaderCell(table, "Outgoing (N)",     headerStyle);
                AddHeaderCell(table, "Asset",       headerStyle);
                AddHeaderCell(table, "Status",      headerStyle);
                AddHeaderCell(table, "Type",        headerStyle);
                AddHeaderCell(table, "Date",        headerStyle);
                AddHeaderCell(table, "Description", headerStyle);

                // Add transaction rows with alternating colors
                const decimal ethToNairaRate = 4_500_000;
                var ngCulture = CultureInfo.CreateSpecificCulture("en-NG");
                  
                    
                decimal totalNaira = transactions.Sum(t => t.Amount * ethToNairaRate);
                string totalNairaFormatted = totalNaira.ToString("C2", ngCulture);
                bool isAlternateRow = false;
                
                foreach (var tx in transactions)
         
                {
                    var rowStyle = isAlternateRow ? alternateCellStyle : cellStyle;
                    var incoming = tx.Type == TransactionType.Credit ? tx.Amount : 0m;
                    var outgoing = tx.Type == TransactionType.Debit  ? tx.Amount : 0m;
                    var inN       = (incoming * ethToNairaRate).ToString("C2", ngCulture);
                    var outN      = (outgoing * ethToNairaRate).ToString("C2", ngCulture);

                    AddCell(table, inN,       rowStyle);
                    AddCell(table, outN,      rowStyle);
                    AddCell(table, tx.Currency.ToString(), rowStyle);
                    AddCell(table, tx.Status.ToString(),   rowStyle);
                    AddCell(table, tx.Type.ToString(),     rowStyle);
                    AddCell(table, tx.CreatedAt.ToString("dddd, MMMM dd, yyyy h:mm tt"), rowStyle);
                    AddCell(table, tx.Description ?? "N/A", rowStyle);

                    isAlternateRow = !isAlternateRow;
                }


                document.Add(table);
                
                // Add footnote
                
                var totalIn  = transactions
                    .Where(t => t.Type == TransactionType.Credit)
                    .Sum(t => t.Amount);
                var totalOut = transactions
                    .Where(t => t.Type == TransactionType.Debit)
                    .Sum(t => t.Amount);

                var totalInN  = (totalIn  * ethToNairaRate).ToString("C2", ngCulture);
                var totalOutN = (totalOut * ethToNairaRate).ToString("C2", ngCulture);

                document.Add(new Paragraph(
                        $"Total Credit (₦): {totalInN}    Total Debit (₦): {totalOutN}")
                    .SetFont(PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD))
                    .SetFontSize(10)
                    .SetTextAlignment(TextAlignment.RIGHT));


                document.Add(new Paragraph($"Generated on {DateTime.Now:yyyy-MM-dd HH:mm}")
                    
                    .SetFontSize(8)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetMarginTop(14));
                
                document.Close();
            }

            _logger.LogInformation("Exported {TransactionCount} transactions to PDF for {username}.", transactions.Count, walletId);
            return memoryStream.ToArray();
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error exporting transactions to PDF for wallet {WalletId}.", walletId);
        throw;
    }
}

// Helper methods for table cell creation
private void AddHeaderCell(Table table, string text, Style style)
{
    Cell cell = new Cell()
        .Add(new Paragraph(text))
        .AddStyle(style)
        .SetTextAlignment(TextAlignment.CENTER)
        .SetVerticalAlignment(VerticalAlignment.MIDDLE);
    
    table.AddHeaderCell(cell);
}

private void AddCell(Table table, string text, Style style)
{
    Cell cell = new Cell()
        .Add(new Paragraph(text))
        .AddStyle(style);
    
    table.AddCell(cell);
}

private string TruncateWithEllipsis(string text, int length)
{
    if (string.IsNullOrEmpty(text) || text.Length <= length)
        return text;
    
    return text.Substring(0, length) + "...";
}
}