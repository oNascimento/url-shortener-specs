using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Shortener.Infrastructure;

[DbContext(typeof(AppDbContext))]
[Migration("202609050001_Foundation")]
public sealed class FoundationMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE schema_metadata (id integer PRIMARY KEY CHECK (id = 1), installed_at timestamptz NOT NULL);
        INSERT INTO schema_metadata VALUES (1, now());
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE schema_metadata;");
}
