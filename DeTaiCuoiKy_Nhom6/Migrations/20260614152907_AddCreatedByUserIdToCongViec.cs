using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeTaiCuoiKy_Nhom6.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedByUserIdToCongViec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "CongViecs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "CongViecs");
        }
    }
}
