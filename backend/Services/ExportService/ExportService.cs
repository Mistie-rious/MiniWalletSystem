using System.Globalization;
using CsvHelper;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.Rendering;
using WalletBackend.Models;

namespace WalletBackend.Services.ExportService;

public class ExportService: IExportService
{
    public async Task<Stream> ExportToCsvAsync(IEnumerable<Transaction> transactions)
    {
        var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            await csv.WriteRecordsAsync(transactions);
        }
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task<Stream> ExportToPdfAsync(IEnumerable<Transaction> transactions)
    {
        return await Task.Run(() =>
        {
           
            var document = new Document();
            var section = document.AddSection();
            section.AddParagraph("Transaction History").Format.Font.Size = 14;

         
            var table = section.AddTable();
            table.AddColumn("50pt");  // ID
            table.AddColumn("100pt"); // Date
            table.AddColumn("150pt"); // From
            table.AddColumn("150pt"); // To
            table.AddColumn("100pt"); // Amount
            table.AddColumn("100pt"); // Status

         
            var headerRow = table.AddRow();
            headerRow.HeadingFormat = true;
            headerRow.Format.Font.Bold = true;
            headerRow.Cells[0].AddParagraph("ID");
            headerRow.Cells[1].AddParagraph("Date");
            headerRow.Cells[2].AddParagraph("From");
            headerRow.Cells[3].AddParagraph("To");
            headerRow.Cells[4].AddParagraph("Amount");
            headerRow.Cells[5].AddParagraph("Status");

            // Add transaction data rows
            foreach (var tx in transactions)
            {
                var row = table.AddRow();
                row.Cells[0].AddParagraph(tx.Id.ToString());
                row.Cells[1].AddParagraph(tx.CreatedAt.ToString("yyyy-MM-dd"));
                row.Cells[2].AddParagraph(tx.FromAddress);
                row.Cells[3].AddParagraph(tx.ToAddress);
                row.Cells[4].AddParagraph(tx.Amount.ToString("N2"));
                row.Cells[5].AddParagraph(tx.Status.ToString());
            }

          
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            var memoryStream = new MemoryStream();
            renderer.PdfDocument.Save(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        });
    }
}