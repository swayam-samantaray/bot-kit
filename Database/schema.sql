DROP TABLE IF EXISTS entity_relationships CASCADE;
DROP TABLE IF EXISTS entities CASCADE;
DROP TABLE IF EXISTS document_chunks CASCADE;
DROP TABLE IF EXISTS ingestion_logs CASCADE;
DROP TABLE IF EXISTS documents CASCADE;

CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS documents
(
    document_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    file_path text NOT NULL UNIQUE,
    file_name text NOT NULL,
    file_type text NOT NULL,
    department text NOT NULL DEFAULT '',
    category text NOT NULL DEFAULT '',
    title text NOT NULL DEFAULT '',
    version text NOT NULL DEFAULT '',
    effective_date date NULL,
    tags jsonb NOT NULL DEFAULT '[]'::jsonb,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    raw_content text NOT NULL DEFAULT '',
    cleaned_content text NOT NULL DEFAULT '',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS document_chunks
(
    chunk_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id uuid NOT NULL REFERENCES documents(document_id) ON DELETE CASCADE,
    chunk_index integer NOT NULL,
    chunk_content text NOT NULL,
    department text NOT NULL DEFAULT '',
    category text NOT NULL DEFAULT '',
    title text NOT NULL DEFAULT '',
    section_heading text NOT NULL DEFAULT '',
    tags text[] NOT NULL DEFAULT ARRAY[]::text[],
    entity_names text[] NOT NULL DEFAULT ARRAY[]::text[],
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    embedding vector(768) NOT NULL,
    token_count integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS entities
(
    entity_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id uuid NOT NULL REFERENCES documents(document_id) ON DELETE CASCADE,
    entity_name text NOT NULL,
    entity_type text NOT NULL,
    normalized_name text NOT NULL,
    aliases jsonb NOT NULL DEFAULT '[]'::jsonb,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS entity_relationships
(
    relationship_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    source_entity_id uuid NOT NULL REFERENCES entities(entity_id) ON DELETE CASCADE,
    target_entity_id uuid NOT NULL REFERENCES entities(entity_id) ON DELETE CASCADE,
    relationship_type text NOT NULL,
    confidence_score double precision NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS ingestion_logs
(
    log_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id uuid NULL REFERENCES documents(document_id) ON DELETE SET NULL,
    status text NOT NULL,
    message text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_documents_department
    ON documents (LOWER(department));

CREATE INDEX IF NOT EXISTS idx_documents_title
    ON documents (LOWER(title));

CREATE INDEX IF NOT EXISTS idx_documents_tags
    ON documents USING gin (tags);

CREATE INDEX IF NOT EXISTS idx_document_chunks_document
    ON document_chunks (document_id);

CREATE INDEX IF NOT EXISTS idx_document_chunks_department
    ON document_chunks (LOWER(department));

CREATE INDEX IF NOT EXISTS idx_document_chunks_tags
    ON document_chunks USING gin (tags);

CREATE INDEX IF NOT EXISTS idx_document_chunks_entity_names
    ON document_chunks USING gin (entity_names);

CREATE INDEX IF NOT EXISTS idx_document_chunks_embedding
    ON document_chunks USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

CREATE INDEX IF NOT EXISTS idx_entities_normalized_name
    ON entities (normalized_name);

CREATE INDEX IF NOT EXISTS idx_entities_aliases
    ON entities USING gin (aliases);

CREATE INDEX IF NOT EXISTS idx_relationships_source
    ON entity_relationships (source_entity_id);

CREATE INDEX IF NOT EXISTS idx_relationships_target
    ON entity_relationships (target_entity_id);
