using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    EntityId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    EntityLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId_Id",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_UserId",
                table: "AuditEntries",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");
        }
    }
}
