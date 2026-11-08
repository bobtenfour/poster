using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntraObjectIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByObjectId",
                table: "PosterRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessedByObjectId",
                table: "PosterProcessings",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubmittedByObjectId",
                table: "PosterRequests");

            migrationBuilder.DropColumn(
                name: "ProcessedByObjectId",
                table: "PosterProcessings");
        }
    }
}
