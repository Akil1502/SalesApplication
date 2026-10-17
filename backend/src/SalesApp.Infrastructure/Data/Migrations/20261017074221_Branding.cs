using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Branding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandColour",
                table: "CompanySettings",
                type: "varchar(7)",
                unicode: false,
                maxLength: 7,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyLogo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    ImageData = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyLogo", x => x.Id);
                    table.CheckConstraint("CK_CompanyLogo_SingleRow", "[Id] = 1");
                });

            migrationBuilder.UpdateData(
                table: "CompanySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "BrandColour",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyLogo");

            migrationBuilder.DropColumn(
                name: "BrandColour",
                table: "CompanySettings");
        }
    }
}
