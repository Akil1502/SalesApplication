using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SalesApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Masters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "CustomerNumbers");

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HsnCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HsnCodes", x => x.Id);
                    table.CheckConstraint("CK_HsnCodes_GstRate", "[GstRate] >= 0 AND [GstRate] <= 40");
                });

            migrationBuilder.CreateTable(
                name: "States",
                columns: table => new
                {
                    Code = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    UsesUtgst = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_States", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sku = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    HsnCodeId = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CostPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReorderLevel = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReorderQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    LeadTimeDays = table.Column<int>(type: "int", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_CostPrice", "[CostPrice] IS NULL OR [CostPrice] >= 0");
                    table.CheckConstraint("CK_Products_ListPrice", "[ListPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_HsnCodes_HsnCodeId",
                        column: x => x.HsnCodeId,
                        principalTable: "HsnCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    TradeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Gstin = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    StateCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    City = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Pincode = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: false),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "varchar(18)", unicode: false, maxLength: 18, nullable: true),
                    BankIfsc = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    BankBranch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvoicePrefix = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    QuotationPrefix = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    InvoiceTerms = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                    table.CheckConstraint("CK_CompanySettings_SingleRow", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_CompanySettings_States_StateCode",
                        column: x => x.StateCode,
                        principalTable: "States",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    TradeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Gstin = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    BillingLine1 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    BillingLine2 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    BillingCity = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BillingStateCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    BillingPincode = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: false),
                    ShippingLine1 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ShippingLine2 = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ShippingCity = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ShippingStateCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: true),
                    ShippingPincode = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: true),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    AssignedRepId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.CheckConstraint("CK_Customers_CreditLimit", "[CreditLimit] IS NULL OR [CreditLimit] >= 0");
                    table.CheckConstraint("CK_Customers_PaymentTerms", "[PaymentTermsDays] BETWEEN 0 AND 365");
                    table.ForeignKey(
                        name: "FK_Customers_AspNetUsers_AssignedRepId",
                        column: x => x.AssignedRepId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_States_BillingStateCode",
                        column: x => x.BillingStateCode,
                        principalTable: "States",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_States_ShippingStateCode",
                        column: x => x.ShippingStateCode,
                        principalTable: "States",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Description", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "Carded and combed cotton yarn, by count", true, "Cotton yarn" },
                    { 2, "Polyester, viscose and blended yarn", true, "Synthetic & blended yarn" },
                    { 3, "Woven and knitted fabrics", true, "Fabrics" },
                    { 4, "Bed, table and kitchen linen", true, "Home textiles" },
                    { 5, "Knitted and woven apparel", true, "Garments" },
                    { 6, "Cones, bags and cartons", true, "Packing material" }
                });

            migrationBuilder.InsertData(
                table: "HsnCodes",
                columns: new[] { "Id", "Code", "Description", "GstRate", "IsActive" },
                values: new object[,]
                {
                    { 1, "5205", "Cotton yarn (not sewing thread), 85% or more cotton, not for retail sale", 5m, true },
                    { 2, "5206", "Cotton yarn (not sewing thread), less than 85% cotton, not for retail sale", 5m, true },
                    { 3, "5509", "Yarn of synthetic staple fibres, not for retail sale", 5m, true },
                    { 4, "5208", "Woven fabrics of cotton, 85% or more cotton, up to 200 g/m2", 5m, true },
                    { 5, "5407", "Woven fabrics of synthetic filament yarn", 5m, true },
                    { 6, "6302", "Bed linen, table linen, toilet and kitchen linen", 5m, true },
                    { 7, "6109", "T-shirts, singlets and vests, knitted or crocheted", 5m, true },
                    { 8, "3923", "Plastic articles for packing goods (cones, bobbins, bags)", 18m, true }
                });

            migrationBuilder.InsertData(
                table: "States",
                columns: new[] { "Code", "Name", "UsesUtgst" },
                values: new object[,]
                {
                    { "01", "Jammu and Kashmir", false },
                    { "02", "Himachal Pradesh", false },
                    { "03", "Punjab", false },
                    { "04", "Chandigarh", true },
                    { "05", "Uttarakhand", false },
                    { "06", "Haryana", false },
                    { "07", "Delhi", false },
                    { "08", "Rajasthan", false },
                    { "09", "Uttar Pradesh", false },
                    { "10", "Bihar", false },
                    { "11", "Sikkim", false },
                    { "12", "Arunachal Pradesh", false },
                    { "13", "Nagaland", false },
                    { "14", "Manipur", false },
                    { "15", "Mizoram", false },
                    { "16", "Tripura", false },
                    { "17", "Meghalaya", false },
                    { "18", "Assam", false },
                    { "19", "West Bengal", false },
                    { "20", "Jharkhand", false },
                    { "21", "Odisha", false },
                    { "22", "Chhattisgarh", false },
                    { "23", "Madhya Pradesh", false },
                    { "24", "Gujarat", false },
                    { "26", "Dadra and Nagar Haveli and Daman and Diu", true },
                    { "27", "Maharashtra", false },
                    { "29", "Karnataka", false },
                    { "30", "Goa", false },
                    { "31", "Lakshadweep", true },
                    { "32", "Kerala", false },
                    { "33", "Tamil Nadu", false },
                    { "34", "Puducherry", false },
                    { "35", "Andaman and Nicobar Islands", true },
                    { "36", "Telangana", false },
                    { "37", "Andhra Pradesh", false },
                    { "38", "Ladakh", true },
                    { "97", "Other Territory", true }
                });

            migrationBuilder.InsertData(
                table: "CompanySettings",
                columns: new[] { "Id", "AddressLine1", "AddressLine2", "BankAccountNumber", "BankBranch", "BankIfsc", "BankName", "City", "Email", "Gstin", "InvoicePrefix", "InvoiceTerms", "LegalName", "Phone", "Pincode", "QuotationPrefix", "StateCode", "TradeName" },
                values: new object[] { 1, "12 Mill Road", null, null, null, null, null, "Coimbatore", "accounts@demotextiles.example", "33ABCDE1234F1Z7", "INV", "Goods once sold will not be taken back. Interest at 18% p.a. is charged on overdue bills. Subject to Coimbatore jurisdiction.", "Demo Textiles Private Limited", null, "641001", "QT", "33", "Demo Textiles" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_StateCode",
                table: "CompanySettings",
                column: "StateCode");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AssignedRepId",
                table: "Customers",
                column: "AssignedRepId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BillingStateCode",
                table: "Customers",
                column: "BillingStateCode");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Code",
                table: "Customers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Gstin",
                table: "Customers",
                column: "Gstin",
                filter: "[Gstin] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ShippingStateCode",
                table: "Customers",
                column: "ShippingStateCode");

            migrationBuilder.CreateIndex(
                name: "IX_HsnCodes_Code",
                table: "HsnCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_HsnCodeId",
                table: "Products",
                column: "HsnCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanySettings");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "States");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "HsnCodes");

            migrationBuilder.DropSequence(
                name: "CustomerNumbers");
        }
    }
}
