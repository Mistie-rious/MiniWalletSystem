using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WalletBackend.Migrations
{
    /// <inheritdoc />
    public partial class lolade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "CurrencyConfigs",
                columns: new[] { "Id", "ChainId", "ContractAddress", "CreatedAt", "Currency", "Decimals", "IsActive", "Name", "Network", "NodeUrl", "Symbol", "WebSocketUrl" },
                values: new object[] { new Guid("a1111111-2222-3333-4444-555555555555"), 11155111, null, new DateTime(2025, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 0, 18, true, "Ethereum", "Sepolia", "https://eth-sepolia.g.alchemy.com/v2/OPEVNKgEfgyM3w8EfPeRVzmnglTuYeEA", "ETH", "wss://eth-sepolia.g.alchemy.com/v2/OPEVNKgEfgyM3w8EfPeRVzmnglTuYeEA" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CurrencyConfigs",
                keyColumn: "Id",
                keyValue: new Guid("a1111111-2222-3333-4444-555555555555"));
        }
    }
}
