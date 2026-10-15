using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AspNetRoles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "AspNetRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_RolePermissions_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    IsGranted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => new { x.UserId, x.Permission });
                    table.ForeignKey(
                        name: "FK_UserPermissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing databases: the three roles get their display names, Administrator becomes the system role, and
            // the other two get exactly the access they had before permissions existed. On a new database there are
            // no roles yet; the app creates them with the same defaults at startup (DbInitializer).
            migrationBuilder.Sql("""
                UPDATE AspNetRoles SET Name = 'Administrator', NormalizedName = 'ADMINISTRATOR', IsSystem = 1,
                    Description = 'Everything, including users, roles and company settings.' WHERE Name = 'Admin';
                UPDATE AspNetRoles SET Name = 'Sales manager', NormalizedName = 'SALES MANAGER',
                    Description = 'All sales work, products, credit terms, margins and AI insights. No administration.' WHERE Name = 'SalesManager';
                UPDATE AspNetRoles SET Name = 'Sales representative', NormalizedName = 'SALES REPRESENTATIVE',
                    Description = 'Customers, orders and invoices. No cost prices or credit terms.' WHERE Name = 'SalesRep';

                INSERT INTO RolePermissions (RoleId, Permission)
                SELECT r.Id, p.Permission FROM AspNetRoles r
                CROSS JOIN (VALUES ('dashboard.view'), ('orders.view'), ('orders.edit'), ('smart-entry.use'), ('invoices.view'), ('invoices.edit'), ('payments.view'), ('payments.edit'), ('customers.view'), ('customers.edit'), ('products.view'), ('products.edit'), ('catalog.view'), ('catalog.edit'), ('stock.view'), ('stock.edit'), ('reports.view'), ('ask-data.use'), ('insights.view'), ('assistant.use'), ('cost-prices.view'), ('credit-terms.edit'), ('credit-limit.override'), ('sales-rep.assignable')) p(Permission)
                WHERE r.Name = 'Sales manager';

                INSERT INTO RolePermissions (RoleId, Permission)
                SELECT r.Id, p.Permission FROM AspNetRoles r
                CROSS JOIN (VALUES ('dashboard.view'), ('orders.view'), ('orders.edit'), ('smart-entry.use'), ('invoices.view'), ('payments.view'), ('customers.view'), ('customers.edit'), ('products.view'), ('stock.view'), ('reports.view'), ('ask-data.use'), ('assistant.use'), ('sales-rep.assignable')) p(Permission)
                WHERE r.Name = 'Sales representative';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE AspNetRoles SET Name = 'Admin', NormalizedName = 'ADMIN' WHERE Name = 'Administrator';
                UPDATE AspNetRoles SET Name = 'SalesManager', NormalizedName = 'SALESMANAGER' WHERE Name = 'Sales manager';
                UPDATE AspNetRoles SET Name = 'SalesRep', NormalizedName = 'SALESREP' WHERE Name = 'Sales representative';
                """);

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "AspNetRoles");
        }
    }
}
