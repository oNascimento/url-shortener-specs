using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Shortener.Infrastructure;

[DbContext(typeof(AppDbContext))]
[Migration("202610100001_Events")]
public sealed class EventsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE access_events (
            occurred_at timestamptz NOT NULL, event_id uuid NOT NULL,
            link_id bigint NOT NULL CHECK (link_id > 0), source_ip inet NOT NULL,
            PRIMARY KEY (occurred_at, event_id)
        ) PARTITION BY RANGE (occurred_at);
        CREATE INDEX access_events_link_position ON access_events(link_id, occurred_at DESC, event_id DESC);
        DO $$ DECLARE day date; BEGIN
            FOR day IN SELECT generate_series((CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::date - 90,
                (CURRENT_TIMESTAMP AT TIME ZONE 'UTC')::date + 7, '1 day')::date LOOP
                EXECUTE format('CREATE TABLE %I PARTITION OF access_events FOR VALUES FROM (%L) TO (%L)',
                    'access_events_' || to_char(day, 'YYYYMMDD'), day::text || ' 00:00:00+00', (day + 1)::text || ' 00:00:00+00');
            END LOOP;
        END $$;
        CREATE TABLE daily_totals (
            link_id bigint NOT NULL, day_utc date NOT NULL,
            stripe smallint NOT NULL CHECK (stripe BETWEEN 0 AND 15),
            count bigint NOT NULL CHECK (count >= 0), updated_at timestamptz NOT NULL,
            PRIMARY KEY(link_id, day_utc, stripe)
        );
        CREATE TABLE collection_incidents (
            id uuid PRIMARY KEY, started_at timestamptz NOT NULL, ended_at timestamptz,
            kind text NOT NULL, severity text NOT NULL,
            confirmed_failure_count bigint, unconfirmed_count bigint,
            CHECK (ended_at IS NULL OR ended_at >= started_at)
        );
        CREATE INDEX collection_incidents_interval ON collection_incidents(started_at, ended_at);
        CREATE UNIQUE INDEX collection_incidents_open_kind ON collection_incidents(kind) WHERE ended_at IS NULL;
        CREATE TABLE collection_snapshot (
            singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton),
            status text NOT NULL CHECK(status IN ('healthy','delayed','degraded','unknown')),
            observed_at timestamptz NOT NULL, oldest_pending_at timestamptz,
            last_persisted_event_at timestamptz, stable_since timestamptz,
            sources jsonb NOT NULL DEFAULT '{}'
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE collection_snapshot, collection_incidents, daily_totals, access_events;
        """);
}
