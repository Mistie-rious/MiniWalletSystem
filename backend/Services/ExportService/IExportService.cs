namespace WalletBackend.Services.ExportService;

public interface IExportService
{
    Task<string> ExportTransactionsToCsvAsync(Guid walletId, DateTime? startDate, DateTime? endDate);
    Task<byte[]> ExportTransactionsToPdfAsync(Guid walletId, DateTime? startDate, DateTime? endDate);
}