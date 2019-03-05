using Microsoft.EntityFrameworkCore.Migrations;

namespace Matrix.SynapseInterop.Worker.AppserviceSender.Migrations
{
    public partial class CreateAppservices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable("appservices", table => new
            {
                id = table.Column<string>(nullable: false),
                enabled = table.Column<bool>(nullable: false),
                as_token = table.Column<string>(nullable: false),
                hs_token = table.Column<string>(nullable: false),
                url = table.Column<string>(nullable: true),
                sender_localpart = table.Column<string>(nullable: false),
                metadata = table.Column<string>(nullable: true)
            });

            migrationBuilder.AddPrimaryKey("PK_appservices_id", "appservices", "id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("appservices");
        }
    }
}
