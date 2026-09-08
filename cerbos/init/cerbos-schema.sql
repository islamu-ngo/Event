-- Source: https://docs.cerbos.dev/cerbos/latest/configuration/storage#postgres
--
-- IMPORTANT: This schema MUST match what Cerbos expects. Cerbos stores policies as
-- protobuf (BYTEA), generates policy IDs internally (bigint, not serial), and
-- revision IDs use SERIAL (not BIGSERIAL). Do not customize column types.
--
-- Usage: Run this script against the PostgreSQL instance used by Cerbos PDP.
-- This is NOT the application database — Cerbos uses its own schema/database.
--
-- For docker-compose: mount as /docker-entrypoint-initdb.d/cerbos-schema.sql
-- or reference in .cerbos.yaml storage.postgres.connStr with ?search_path=cerbos

CREATE SCHEMA IF NOT EXISTS cerbos;
SET search_path TO cerbos;

-- ===== Core Policy Storage =====

CREATE TABLE IF NOT EXISTS policy (
    id                  bigint NOT NULL PRIMARY KEY,
    kind                VARCHAR(128)  NOT NULL,
    name                VARCHAR(1024) NOT NULL,
    version             VARCHAR(128)  NOT NULL,
    scope               VARCHAR(512),
    description         TEXT,
    disabled            BOOLEAN DEFAULT FALSE,
    definition          BYTEA
);

-- ===== Policy Dependencies =====
-- Tracks which policies depend on other policies (e.g., derived roles imports)

CREATE TABLE IF NOT EXISTS policy_dependency (
    policy_id           BIGINT NOT NULL REFERENCES policy(id) ON DELETE CASCADE,
    dependency_id       BIGINT NOT NULL REFERENCES policy(id) ON DELETE CASCADE,

    PRIMARY KEY (policy_id, dependency_id)
);

-- ===== Policy Ancestors =====
-- Tracks scope ancestry for hierarchical policy resolution

CREATE TABLE IF NOT EXISTS policy_ancestor (
    policy_id           BIGINT NOT NULL REFERENCES policy(id) ON DELETE CASCADE,
    ancestor_id         BIGINT NOT NULL REFERENCES policy(id) ON DELETE CASCADE,

    PRIMARY KEY (policy_id, ancestor_id)
);

-- ===== Policy Revision History =====
-- Audit trail for policy changes (insert/update/delete)

CREATE TABLE IF NOT EXISTS policy_revision (
    revision_id         SERIAL PRIMARY KEY,
    action              VARCHAR(64),
    id                  BIGINT,
    kind                VARCHAR(128),
    name                VARCHAR(1024),
    version             VARCHAR(128),
    scope               VARCHAR(512),
    description         TEXT,
    disabled            BOOLEAN,
    definition          BYTEA,
    update_timestamp    TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- ===== Attribute Schema Definitions =====
-- Stores JSON schemas for validating principal/resource attributes

CREATE TABLE IF NOT EXISTS attr_schema_defs (
    id                  VARCHAR(255) PRIMARY KEY,
    definition          JSON
);

-- ===== Audit Trigger =====
-- Automatically logs policy changes to the revision table

CREATE OR REPLACE FUNCTION process_policy_audit()
RETURNS TRIGGER AS $policy_audit$
BEGIN
    IF (TG_OP = 'DELETE') THEN
        INSERT INTO policy_revision (action, id, kind, name, version, scope, description, disabled, definition)
        VALUES ('DELETE', OLD.id, OLD.kind, OLD.name, OLD.version, OLD.scope, OLD.description, OLD.disabled, OLD.definition);
    ELSIF (TG_OP = 'UPDATE') THEN
        INSERT INTO policy_revision (action, id, kind, name, version, scope, description, disabled, definition)
        VALUES ('UPDATE', NEW.id, NEW.kind, NEW.name, NEW.version, NEW.scope, NEW.description, NEW.disabled, NEW.definition);
    ELSIF (TG_OP = 'INSERT') THEN
        INSERT INTO policy_revision (action, id, kind, name, version, scope, description, disabled, definition)
        VALUES ('INSERT', NEW.id, NEW.kind, NEW.name, NEW.version, NEW.scope, NEW.description, NEW.disabled, NEW.definition);
    END IF;
    RETURN NULL;
END;
$policy_audit$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS policy_audit ON policy;
CREATE TRIGGER policy_audit
    AFTER INSERT OR UPDATE OR DELETE ON policy
    FOR EACH ROW EXECUTE PROCEDURE process_policy_audit();

-- ===== Cerbos User (optional — for connection pooling) =====
-- Uncomment and customize if running Cerbos with a dedicated database user:
--
-- DO $$
-- BEGIN
--     IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'cerbos_user') THEN
--         CREATE ROLE cerbos_user WITH LOGIN PASSWORD 'cerbos_password';
--     END IF;
-- END
-- $$;
--
-- GRANT CONNECT ON DATABASE islamu_cerbos_db TO cerbos_user;
-- GRANT USAGE ON SCHEMA cerbos TO cerbos_user;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON cerbos.policy, cerbos.policy_dependency, cerbos.policy_ancestor, cerbos.attr_schema_defs TO cerbos_user;
-- GRANT SELECT, INSERT, DELETE ON cerbos.policy_revision TO cerbos_user;
-- GRANT USAGE, SELECT ON cerbos.policy_revision_revision_id_seq TO cerbos_user;
