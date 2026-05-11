CREATE EXTENSION IF NOT EXISTS vector;

CREATE EXTENSION IF NOT EXISTS pgcrypto;


CREATE TABLE documents
(
    document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    file_name TEXT NOT NULL,

    file_type VARCHAR(50),

    raw_content TEXT,

    cleaned_content TEXT,

    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE document_chunks
(
    chunk_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    document_id UUID
        REFERENCES documents(document_id)
        ON DELETE CASCADE,

    chunk_index INT NOT NULL,

    chunk_content TEXT NOT NULL,

    embedding VECTOR(768),

    token_count INT,

    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_document_chunks_embedding
ON document_chunks
USING ivfflat (embedding vector_cosine_ops)
WITH (lists = 100);

CREATE INDEX idx_document_chunks_content
ON document_chunks
USING gin (to_tsvector('english', chunk_content));

CREATE TABLE entities
(
    entity_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    document_id UUID
        REFERENCES documents(document_id)
        ON DELETE CASCADE,

    entity_name TEXT NOT NULL,

    entity_type VARCHAR(100),

    normalized_name TEXT,

    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_entities_normalized_name
ON entities(normalized_name);

CREATE INDEX idx_entities_entity_type
ON entities(entity_type);



CREATE TABLE entity_relationships
(
    relationship_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    source_entity_id UUID
        REFERENCES entities(entity_id)
        ON DELETE CASCADE,

    relationship_type VARCHAR(100),

    target_entity_id UUID
        REFERENCES entities(entity_id)
        ON DELETE CASCADE,

    confidence_score NUMERIC(5,2),

    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


CREATE INDEX idx_relationship_source
ON entity_relationships(source_entity_id);

CREATE INDEX idx_relationship_target
ON entity_relationships(target_entity_id);

CREATE INDEX idx_relationship_type
ON entity_relationships(relationship_type);



CREATE TABLE ingestion_logs
(
    log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    document_id UUID
        REFERENCES documents(document_id)
        ON DELETE CASCADE,

    status VARCHAR(50),

    message TEXT,

    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);



CREATE TABLE query_logs
(
    query_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    user_query TEXT,

    retrieved_chunks JSONB,

    generated_answer TEXT,

    response_time_ms INT,

    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);



CREATE TABLE document_tags
(
    tag_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    document_id UUID
        REFERENCES documents(document_id)
        ON DELETE CASCADE,

    tag_key VARCHAR(100),

    tag_value TEXT
);


TRUNCATE TABLE entity_relationships CASCADE;

TRUNCATE TABLE entities CASCADE;

TRUNCATE TABLE document_chunks CASCADE;

TRUNCATE TABLE ingestion_logs CASCADE;

TRUNCATE TABLE query_logs CASCADE;

TRUNCATE TABLE document_tags CASCADE;

TRUNCATE TABLE documents CASCADE;


SELECT COUNT(*) FROM documents;

SELECT COUNT(*) FROM document_chunks;

SELECT COUNT(*) FROM entities;

SELECT COUNT(*) FROM entity_relationships;