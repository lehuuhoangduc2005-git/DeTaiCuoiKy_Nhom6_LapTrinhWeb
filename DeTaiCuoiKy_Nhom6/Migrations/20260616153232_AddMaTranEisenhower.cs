using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeTaiCuoiKy_Nhom6.Migrations
{
    /// <inheritdoc />
    public partial class AddMaTranEisenhower : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaTranEisenhower",
                table: "CongViecs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaTranEisenhower",
                table: "CongViecs");
        }
    }
}
