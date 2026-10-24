#nullable disable

namespace SylvanBot.Infrastructure.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

public partial class NumericTicketNumbers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Validate all conversions while writes are blocked, before changing any stored values.
        // The migration transaction retains every record and its message associations.
        migrationBuilder.Sql("""
            LOCK TABLE tickets IN ACCESS EXCLUSIVE MODE;
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM tickets
                    WHERE "Reference" !~ '^(T-)?[0-9]+$'
                ) THEN
                    RAISE EXCEPTION 'Ticket numbers contain invalid values; no tickets were changed.';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM tickets
                    WHERE regexp_replace("Reference", '^T-', '')::numeric NOT BETWEEN 1 AND 9223372036854775806
                ) THEN
                    RAISE EXCEPTION 'Ticket numbers must be positive integers within the allocation range; no tickets were changed.';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM tickets
                    GROUP BY "GuildId", regexp_replace("Reference", '^T-', '')::numeric
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Ticket number conversion conflicts within a guild; resolve duplicates before retrying. No tickets were changed.';
                END IF;
            END $$;

            ALTER TABLE tickets DROP CONSTRAINT "CK_tickets_reference";
            ALTER TABLE tickets ALTER COLUMN "Reference" TYPE bigint
                USING regexp_replace("Reference", '^T-', '')::bigint;
            ALTER TABLE tickets RENAME COLUMN "Reference" TO "Number";
            ALTER INDEX "IX_tickets_GuildId_Reference" RENAME TO "IX_tickets_GuildId_Number";
            ALTER TABLE tickets ADD CONSTRAINT "CK_tickets_number" CHECK ("Number" > 0);
            ALTER SEQUENCE ticket_reference_numbers RENAME TO ticket_numbers;
            SELECT setval('ticket_numbers',
                GREATEST(COALESCE((SELECT MAX("Number") FROM tickets), 1),
                    (SELECT last_value FROM ticket_numbers)),
                (SELECT is_called FROM ticket_numbers) OR EXISTS (SELECT 1 FROM tickets));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE tickets DROP CONSTRAINT "CK_tickets_number";
            ALTER TABLE tickets ALTER COLUMN "Number" TYPE character varying(32) USING "Number"::text;
            ALTER TABLE tickets RENAME COLUMN "Number" TO "Reference";
            ALTER INDEX "IX_tickets_GuildId_Number" RENAME TO "IX_tickets_GuildId_Reference";
            ALTER TABLE tickets ADD CONSTRAINT "CK_tickets_reference"
                CHECK ("Reference" ~ '^(T-[0-9]{4,}|[0-9]+)$');
            ALTER SEQUENCE ticket_numbers RENAME TO ticket_reference_numbers;
            """);
    }
}