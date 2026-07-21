using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Rbac.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRetryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadLetteredAt",
                table: "outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_PublishedAt_DeadLetteredAt_NextAttemptAt",
                table: "outbox",
                columns: new[] { "PublishedAt", "DeadLetteredAt", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_PublishedAt_DeadLetteredAt_NextAttemptAt",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAt",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "outbox");
        }
    }
}
