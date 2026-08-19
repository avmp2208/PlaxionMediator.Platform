using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Storage.Relational.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aggregate_rollups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Environment = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BucketStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BucketEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Min = table.Column<double>(type: "double precision", nullable: false),
                    Max = table.Column<double>(type: "double precision", nullable: false),
                    Avg = table.Column<double>(type: "double precision", nullable: false),
                    P50 = table.Column<double>(type: "double precision", nullable: false),
                    P95 = table.Column<double>(type: "double precision", nullable: false),
                    P99 = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aggregate_rollups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HashedKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "metric_samples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Environment = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metric_samples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Environment = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_services", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aggregate_rollups_TenantId_ServiceId_BucketStartUtc_Request~",
                table: "aggregate_rollups",
                columns: new[] { "TenantId", "ServiceId", "BucketStartUtc", "RequestType" });

            migrationBuilder.CreateIndex(
                name: "IX_aggregate_rollups_TenantId_ServiceId_MetricName_RequestType~",
                table: "aggregate_rollups",
                columns: new[] { "TenantId", "ServiceId", "MetricName", "RequestType", "BucketStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_HashedKey",
                table: "api_keys",
                column: "HashedKey");

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_TenantId",
                table: "api_keys",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_metric_samples_TenantId_ServiceName_MetricName_RequestType",
                table: "metric_samples",
                columns: new[] { "TenantId", "ServiceName", "MetricName", "RequestType" });

            migrationBuilder.CreateIndex(
                name: "IX_metric_samples_TenantId_TimestampUtc",
                table: "metric_samples",
                columns: new[] { "TenantId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_services_TenantId_Name_Environment",
                table: "services",
                columns: new[] { "TenantId", "Name", "Environment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aggregate_rollups");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "metric_samples");

            migrationBuilder.DropTable(
                name: "services");
        }
    }
}
