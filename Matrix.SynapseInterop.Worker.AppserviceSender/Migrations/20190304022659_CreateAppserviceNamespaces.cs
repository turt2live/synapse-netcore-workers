using Microsoft.EntityFrameworkCore.Migrations;

namespace Matrix.SynapseInterop.Worker.AppserviceSender.Migrations
{
    public partial class CreateAppserviceNamespaces : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable("appservice_namespaces",
                                         table => new
                                         {
                                             id = table.Column<string>(nullable: false),
                                             appservice_id = table.Column<string>(nullable: false),
                                             kind = table.Column<string>(nullable: false),
                                             exclusive = table.Column<bool>(nullable: false),
                                             regex = table.Column<string>(nullable: false)
                                         },
                                         constraints: table =>
                                         {
                                             table.PrimaryKey("PK_appservice_namespaces", x => x.id);

                                             table.ForeignKey("FK_appservice_namespaces_appservices_appservice_id",
                                                              x => x.appservice_id,
                                                              "appservices",
                                                              "id",
                                                              onDelete: ReferentialAction.Restrict);
                                         });

            migrationBuilder.CreateIndex("IX_appservice_namespaces_appservice_id",
                                         "appservice_namespaces",
                                         "appservice_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("appservice_namespaces");
        }
    }
}
