using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesApp.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Link = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DedupKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationStates",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadUpToId = table.Column<long>(type: "bigint", nullable: false),
                    LastOpenedUpToId = table.Column<long>(type: "bigint", nullable: false),
                    MutedKinds = table.Column<string>(type: "varchar(400)", unicode: false, maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationStates", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserNotificationStates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationReads",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationReads", x => new { x.UserId, x.NotificationId });
                    table.ForeignKey(
                        name: "FK_NotificationReads_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationReads_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationReads_NotificationId",
                table: "NotificationReads",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAtUtc",
                table: "Notifications",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DedupKey",
                table: "Notifications",
                column: "DedupKey",
                unique: true,
                filter: "[DedupKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId",
                table: "Notifications",
                column: "RecipientUserId");

            // Sales managers have always seen every customer's figures; give them the new right to hear about every
            // customer too. Built-in roles are only created once, so an existing database needs this explicitly.
            migrationBuilder.Sql("""
                INSERT INTO [RolePermissions] ([RoleId], [Permission])
                SELECT r.[Id], 'customers.all-activity'
                FROM [AspNetRoles] r
                WHERE r.[Name] = N'Sales manager'
                  AND NOT EXISTS (SELECT 1 FROM [RolePermissions] p WHERE p.[RoleId] = r.[Id] AND p.[Permission] = 'customers.all-activity');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [RolePermissions] WHERE [Permission] = 'customers.all-activity';");
            migrationBuilder.Sql("DELETE FROM [UserPermissions] WHERE [Permission] = 'customers.all-activity';");

            migrationBuilder.DropTable(
                name: "NotificationReads");

            migrationBuilder.DropTable(
                name: "UserNotificationStates");

            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
