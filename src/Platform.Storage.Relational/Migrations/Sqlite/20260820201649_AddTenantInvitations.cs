using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Storage.Relational.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddTenantInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedByIssuer = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CreatedBySubject = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClaimedByIssuer = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ClaimedBySubject = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_invitations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_invitations_NormalizedEmail",
                table: "tenant_invitations",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_invitations_TenantId",
                table: "tenant_invitations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_invitations_TenantId_NormalizedEmail",
                table: "tenant_invitations",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true,
                filter: "Status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_invitations");
        }
    }
}
