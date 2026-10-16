using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ImageData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ThumbnailContentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ThumbnailData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.ProductId);
                    table.CheckConstraint("CK_ProductImages_Size", "DATALENGTH([ImageData]) <= 2097152 AND DATALENGTH([ThumbnailData]) <= 262144");
                    table.CheckConstraint("CK_ProductImages_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductImages");
        }
    }
}
