using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Storage.Relational.Migrations.PostgreSql
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByIssuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedByIssuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ClaimedBySubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
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
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_invitations");
        }
    }
}
