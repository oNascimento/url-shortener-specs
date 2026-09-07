using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Shortener.Infrastructure;

[DbContext(typeof(AppDbContext))]
[Migration("202609070001_Authentication")]
public sealed class AuthenticationMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
      CREATE TABLE IF NOT EXISTS users (id uuid PRIMARY KEY, user_name text NULL, normalized_user_name text NULL, email text NULL, normalized_email text NOT NULL UNIQUE, email_confirmed boolean NOT NULL DEFAULT false, password_hash text NULL, security_stamp text NULL, concurrency_stamp text NULL, phone_number text NULL, phone_number_confirmed boolean NOT NULL DEFAULT false, two_factor_enabled boolean NOT NULL DEFAULT false, lockout_end timestamptz NULL, lockout_enabled boolean NOT NULL DEFAULT false, access_failed_count integer NOT NULL DEFAULT 0, created_at timestamptz NOT NULL, email_verified_at timestamptz NULL, blocked_at timestamptz NULL, deletion_requested_at timestamptz NULL);
      CREATE TABLE IF NOT EXISTS auth_sessions (id uuid PRIMARY KEY, user_id uuid NOT NULL REFERENCES users(id), created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL, revoked_at timestamptz NULL);
      CREATE INDEX IF NOT EXISTS ix_auth_sessions_user_id ON auth_sessions(user_id);
      CREATE TABLE IF NOT EXISTS refresh_tokens (id uuid PRIMARY KEY, session_id uuid NOT NULL REFERENCES auth_sessions(id), token_hash text NOT NULL UNIQUE, created_at timestamptz NOT NULL, consumed_at timestamptz NULL, replaced_by uuid NULL, expires_at timestamptz NOT NULL);
      CREATE TABLE IF NOT EXISTS action_tokens (hash text PRIMARY KEY, user_id uuid NOT NULL REFERENCES users(id), purpose text NOT NULL, expires_at timestamptz NOT NULL, consumed_at timestamptz NULL);
      """);
    protected override void Down(MigrationBuilder m) => m.Sql("DROP TABLE IF EXISTS action_tokens, refresh_tokens, auth_sessions, users;");
}
