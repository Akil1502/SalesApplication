using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Returns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplierBills_AmountPaid",
                table: "SupplierBills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReceiptLines_Quantity",
                table: "StockReceiptLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_AmountPaid",
                table: "Invoices");

            migrationBuilder.AddColumn<decimal>(
                name: "AmountCredited",
                table: "SupplierBills",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityRejected",
                table: "StockReceiptLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "StockReceiptLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountCredited",
                table: "Invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    FinancialYear = table.Column<int>(type: "int", nullable: false),
                    CreditNoteDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ReasonNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WithGst = table.Column<bool>(type: "bit", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    SellerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SellerGstin = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    SellerStateCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    SellerAddress = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CustomerGstin = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    BillingLine1 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    BillingLine2 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    BillingCity = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BillingStateCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    BillingStateName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BillingPincode = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: false),
                    PlaceOfSupplyStateCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    PlaceOfSupplyStateName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsInterState = table.Column<bool>(type: "bit", nullable: false),
                    UsesUtgst = table.Column<bool>(type: "bit", nullable: false),
                    TaxableValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RoundOff = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountAdjusted = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountRefunded = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.CheckConstraint("CK_CreditNotes_Total", "[GrandTotal] > 0");
                    table.CheckConstraint("CK_CreditNotes_Used", "[AmountAdjusted] >= 0 AND [AmountRefunded] >= 0 AND [AmountAdjusted] + [AmountRefunded] <= [GrandTotal]");
                    table.ForeignKey(
                        name: "FK_CreditNotes_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    FinancialYear = table.Column<int>(type: "int", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ReasonNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WithGst = table.Column<bool>(type: "bit", nullable: false),
                    SupplierBillId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SupplierGstin = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    SupplierStateCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    IsInterState = table.Column<bool>(type: "bit", nullable: false),
                    SupplierCreditNoteNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SupplierCreditNoteDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TaxableValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RoundOff = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountAdjusted = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountRefunded = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturns", x => x.Id);
                    table.CheckConstraint("CK_SupplierReturns_Total", "[GrandTotal] > 0");
                    table.CheckConstraint("CK_SupplierReturns_Used", "[AmountAdjusted] >= 0 AND [AmountRefunded] >= 0 AND [AmountAdjusted] + [AmountRefunded] <= [GrandTotal]");
                    table.ForeignKey(
                        name: "FK_SupplierReturns_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturns_States_SupplierStateCode",
                        column: x => x.SupplierStateCode,
                        principalTable: "States",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturns_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturns_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreditNoteId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    AdjustmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditAdjustments", x => x.Id);
                    table.CheckConstraint("CK_CreditAdjustments_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CreditAdjustments_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditAdjustments_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditAdjustments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreditNoteId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    InvoiceLineId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductSku = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HsnCode = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Unit = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Disposition = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    TaxableValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNoteLines", x => x.Id);
                    table.CheckConstraint("CK_CreditNoteLines_Goods", "([Quantity] IS NULL AND [Disposition] IS NULL) OR ([Quantity] > 0 AND [Disposition] IS NOT NULL)");
                    table.CheckConstraint("CK_CreditNoteLines_Value", "[TaxableValue] > 0");
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_InvoiceLines_InvoiceLineId",
                        column: x => x.InvoiceLineId,
                        principalTable: "InvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreditNoteId = table.Column<int>(type: "int", nullable: false),
                    RefundDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Mode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditRefunds", x => x.Id);
                    table.CheckConstraint("CK_CreditRefunds_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CreditRefunds_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditRefunds_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCreditAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierReturnId = table.Column<int>(type: "int", nullable: false),
                    SupplierBillId = table.Column<int>(type: "int", nullable: false),
                    AdjustmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditAdjustments", x => x.Id);
                    table.CheckConstraint("CK_SupplierCreditAdjustments_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_SupplierCreditAdjustments_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditAdjustments_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditAdjustments_SupplierReturns_SupplierReturnId",
                        column: x => x.SupplierReturnId,
                        principalTable: "SupplierReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierReturnId = table.Column<int>(type: "int", nullable: false),
                    RefundDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Mode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierRefunds", x => x.Id);
                    table.CheckConstraint("CK_SupplierRefunds_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_SupplierRefunds_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierRefunds_SupplierReturns_SupplierReturnId",
                        column: x => x.SupplierReturnId,
                        principalTable: "SupplierReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierReturnId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    SupplierBillLineId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductSku = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HsnCode = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Unit = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxableValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnLines", x => x.Id);
                    table.CheckConstraint("CK_SupplierReturnLines_Quantity", "[Quantity] > 0 AND [TaxableValue] > 0");
                    table.ForeignKey(
                        name: "FK_SupplierReturnLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnLines_SupplierBillLines_SupplierBillLineId",
                        column: x => x.SupplierBillLineId,
                        principalTable: "SupplierBillLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnLines_SupplierReturns_SupplierReturnId",
                        column: x => x.SupplierReturnId,
                        principalTable: "SupplierReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplierBills_AmountPaid",
                table: "SupplierBills",
                sql: "[AmountPaid] >= 0 AND [AmountCredited] >= 0 AND [AmountPaid] + [AmountCredited] <= [GrandTotal]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReceiptLines_Quantity",
                table: "StockReceiptLines",
                sql: "[Quantity] >= 0 AND [QuantityRejected] >= 0 AND [Quantity] + [QuantityRejected] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_AmountPaid",
                table: "Invoices",
                sql: "[AmountPaid] >= 0 AND [AmountCredited] >= 0 AND [AmountPaid] + [AmountCredited] <= [GrandTotal]");

            migrationBuilder.CreateIndex(
                name: "IX_CreditAdjustments_CreatedByUserId",
                table: "CreditAdjustments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditAdjustments_CreditNoteId",
                table: "CreditAdjustments",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditAdjustments_InvoiceId",
                table: "CreditAdjustments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_CreditNoteId_LineNumber",
                table: "CreditNoteLines",
                columns: new[] { "CreditNoteId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_InvoiceLineId",
                table: "CreditNoteLines",
                column: "InvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_ProductId",
                table: "CreditNoteLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CreatedByUserId",
                table: "CreditNotes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CustomerId_Status",
                table: "CreditNotes",
                columns: new[] { "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_FinancialYear_CreditNoteDate",
                table: "CreditNotes",
                columns: new[] { "FinancialYear", "CreditNoteDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_Number",
                table: "CreditNotes",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditRefunds_CreatedByUserId",
                table: "CreditRefunds",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditRefunds_CreditNoteId",
                table: "CreditRefunds",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditRefunds_RefundDate",
                table: "CreditRefunds",
                column: "RefundDate");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditAdjustments_CreatedByUserId",
                table: "SupplierCreditAdjustments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditAdjustments_SupplierBillId",
                table: "SupplierCreditAdjustments",
                column: "SupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditAdjustments_SupplierReturnId",
                table: "SupplierCreditAdjustments",
                column: "SupplierReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierRefunds_CreatedByUserId",
                table: "SupplierRefunds",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierRefunds_RefundDate",
                table: "SupplierRefunds",
                column: "RefundDate");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierRefunds_SupplierReturnId",
                table: "SupplierRefunds",
                column: "SupplierReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnLines_ProductId",
                table: "SupplierReturnLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnLines_SupplierBillLineId",
                table: "SupplierReturnLines",
                column: "SupplierBillLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnLines_SupplierReturnId_LineNumber",
                table: "SupplierReturnLines",
                columns: new[] { "SupplierReturnId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_CreatedByUserId",
                table: "SupplierReturns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_FinancialYear_ReturnDate",
                table: "SupplierReturns",
                columns: new[] { "FinancialYear", "ReturnDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_Number",
                table: "SupplierReturns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_SupplierBillId",
                table: "SupplierReturns",
                column: "SupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_SupplierId_Status",
                table: "SupplierReturns",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_SupplierStateCode",
                table: "SupplierReturns",
                column: "SupplierStateCode");

            // The built-in roles get the new rights as new databases do (DbInitializer): Sales manager issues credit notes
            // and supplier returns, Sales representative can see credit notes.
            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, Permission)
                SELECT r.Id, p.Permission FROM AspNetRoles r
                CROSS JOIN (VALUES ('credit-notes.view'), ('credit-notes.edit'), ('supplier-returns.view'), ('supplier-returns.edit')) p(Permission)
                WHERE r.Name = 'Sales manager'
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions x WHERE x.RoleId = r.Id AND x.Permission = p.Permission);

                INSERT INTO RolePermissions (RoleId, Permission)
                SELECT r.Id, 'credit-notes.view' FROM AspNetRoles r
                WHERE r.Name = 'Sales representative'
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions x WHERE x.RoleId = r.Id AND x.Permission = 'credit-notes.view');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM RolePermissions WHERE Permission IN ('credit-notes.view', 'credit-notes.edit', 'supplier-returns.view', 'supplier-returns.edit');
                DELETE FROM UserPermissions WHERE Permission IN ('credit-notes.view', 'credit-notes.edit', 'supplier-returns.view', 'supplier-returns.edit');
                """);

            migrationBuilder.DropTable(
                name: "CreditAdjustments");

            migrationBuilder.DropTable(
                name: "CreditNoteLines");

            migrationBuilder.DropTable(
                name: "CreditRefunds");

            migrationBuilder.DropTable(
                name: "SupplierCreditAdjustments");

            migrationBuilder.DropTable(
                name: "SupplierRefunds");

            migrationBuilder.DropTable(
                name: "SupplierReturnLines");

            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropTable(
                name: "SupplierReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplierBills_AmountPaid",
                table: "SupplierBills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReceiptLines_Quantity",
                table: "StockReceiptLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_AmountPaid",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "AmountCredited",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "QuantityRejected",
                table: "StockReceiptLines");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "StockReceiptLines");

            migrationBuilder.DropColumn(
                name: "AmountCredited",
                table: "Invoices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplierBills_AmountPaid",
                table: "SupplierBills",
                sql: "[AmountPaid] >= 0 AND [AmountPaid] <= [GrandTotal]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReceiptLines_Quantity",
                table: "StockReceiptLines",
                sql: "[Quantity] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_AmountPaid",
                table: "Invoices",
                sql: "[AmountPaid] >= 0 AND [AmountPaid] <= [GrandTotal]");
        }
    }
}
