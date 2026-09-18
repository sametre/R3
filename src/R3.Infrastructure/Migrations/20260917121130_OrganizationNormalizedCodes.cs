using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace R3.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationNormalizedCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_warehouses_company_id_code",
                schema: "r3",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "IX_companies_code",
                schema: "r3",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "IX_branches_company_id_code",
                schema: "r3",
                table: "branches");

            migrationBuilder.AddColumn<string>(
                name: "normalized_code",
                schema: "r3",
                table: "warehouses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_code",
                schema: "r3",
                table: "companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_code",
                schema: "r3",
                table: "branches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_company_id_normalized_code",
                schema: "r3",
                table: "warehouses",
                columns: new[] { "company_id", "normalized_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_companies_normalized_code",
                schema: "r3",
                table: "companies",
                column: "normalized_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branches_company_id_normalized_code",
                schema: "r3",
                table: "branches",
                columns: new[] { "company_id", "normalized_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_warehouses_company_id_normalized_code",
                schema: "r3",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "IX_companies_normalized_code",
                schema: "r3",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "IX_branches_company_id_normalized_code",
                schema: "r3",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "normalized_code",
                schema: "r3",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "normalized_code",
                schema: "r3",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "normalized_code",
                schema: "r3",
                table: "branches");

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_company_id_code",
                schema: "r3",
                table: "warehouses",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_companies_code",
                schema: "r3",
                table: "companies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branches_company_id_code",
                schema: "r3",
                table: "branches",
                columns: new[] { "company_id", "code" },
                unique: true);
        }
    }
}
