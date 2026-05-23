-- SAFE: drops only tenant_* schemas. NEVER drop "public".
-- Run only after you confirmed leads/configs/events live in public."Leads" etc.
-- Take a DB backup first.

DO $$
DECLARE
    schema_name text;
BEGIN
    FOR schema_name IN
        SELECT nspname
        FROM pg_namespace
        WHERE nspname LIKE 'tenant\_%' ESCAPE '\'
        ORDER BY nspname
    LOOP
        RAISE NOTICE 'Dropping schema %', schema_name;
        EXECUTE format('DROP SCHEMA IF EXISTS %I CASCADE', schema_name);
    END LOOP;
END $$;
