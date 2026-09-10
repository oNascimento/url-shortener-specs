using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Shortener.Infrastructure;

[DbContext(typeof(AppDbContext))]
[Migration("202609090001_LinkManagement")]
public sealed class LinkManagementMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SEQUENCE link_ids AS bigint START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 2 NO CYCLE CACHE 1;
        -- PostgreSQL requires MINVALUE < MAXVALUE. Park this empty sequence at exhaustion
        -- until the allocator durably authorizes its first range; no ID can be issued yet.
        SELECT setval('link_ids', 2, true);
        CREATE TABLE link_allocation (
            singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
            upper_bound bigint NOT NULL CHECK (upper_bound >= 0),
            suspended boolean NOT NULL DEFAULT false
        );
        INSERT INTO link_allocation(singleton, upper_bound) VALUES (true, 0);
        CREATE TABLE links (
            id bigint PRIMARY KEY DEFAULT nextval('link_ids') CHECK (id > 0),
            code varchar(11) COLLATE "C" NOT NULL UNIQUE CHECK (code ~ '^[0-9a-zA-Z]{1,11}$'),
            owner_id uuid REFERENCES users(id),
            destination_url text,
            created_at timestamptz NOT NULL,
            owner_disabled_at timestamptz,
            admin_blocked_at timestamptz,
            deleted_at timestamptz,
            CHECK ((deleted_at IS NULL AND owner_id IS NOT NULL AND destination_url IS NOT NULL)
                OR (deleted_at IS NOT NULL AND owner_id IS NULL AND destination_url IS NULL))
        );
        CREATE INDEX links_owner_created ON links(owner_id, created_at DESC, id DESC);
        CREATE TABLE creation_requests (
            owner_id uuid NOT NULL REFERENCES users(id),
            idempotency_key uuid NOT NULL,
            url_hash bytea NOT NULL CHECK (octet_length(url_hash) = 32),
            link_id bigint NOT NULL REFERENCES links(id),
            expires_at timestamptz NOT NULL,
            PRIMARY KEY (owner_id, idempotency_key)
        );
        CREATE INDEX creation_requests_expiry ON creation_requests(expires_at);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE creation_requests;
        DROP TABLE links;
        DROP TABLE link_allocation;
        DROP SEQUENCE link_ids;
        """);
}
