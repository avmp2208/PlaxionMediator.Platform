using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Storage.Relational.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddTenantMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdentityIssuer = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IdentitySubject = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_memberships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_memberships_IdentityIssuer_IdentitySubject",
                table: "tenant_memberships",
                columns: new[] { "IdentityIssuer", "IdentitySubject" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_memberships_TenantId",
                table: "tenant_memberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_memberships_TenantId_IdentityIssuer_IdentitySubject",
                table: "tenant_memberships",
                columns: new[] { "TenantId", "IdentityIssuer", "IdentitySubject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_memberships");
        }
    }
}
