-- Device Intelligence AI Knowledge Graph Schema
-- SQLite DDL for entities, edges, properties, and temporal tracking

CREATE TABLE IF NOT EXISTS entities (
    id TEXT PRIMARY KEY,
    type TEXT NOT NULL,
    label TEXT NOT NULL,
    first_seen TEXT NOT NULL,  -- ISO 8601
    last_seen TEXT NOT NULL,   -- ISO 8601
    properties_json TEXT NOT NULL DEFAULT '{}'
);

CREATE INDEX IF NOT EXISTS idx_entities_type ON entities(type);
CREATE INDEX IF NOT EXISTS idx_entities_last_seen ON entities(last_seen);

CREATE TABLE IF NOT EXISTS edges (
    id TEXT PRIMARY KEY,
    source_id TEXT NOT NULL,
    target_id TEXT NOT NULL,
    type TEXT NOT NULL,
    created_at TEXT NOT NULL,  -- ISO 8601
    confidence REAL NOT NULL DEFAULT 1.0,
    properties_json TEXT NOT NULL DEFAULT '{}',
    FOREIGN KEY (source_id) REFERENCES entities(id) ON DELETE CASCADE,
    FOREIGN KEY (target_id) REFERENCES entities(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_edges_source ON edges(source_id);
CREATE INDEX IF NOT EXISTS idx_edges_target ON edges(target_id);
CREATE INDEX IF NOT EXISTS idx_edges_type ON edges(type);
CREATE INDEX IF NOT EXISTS idx_edges_created_at ON edges(created_at);

CREATE TABLE IF NOT EXISTS snapshots (
    id TEXT PRIMARY KEY,
    timestamp TEXT NOT NULL,   -- ISO 8601
    entity_count INTEGER NOT NULL DEFAULT 0,
    edge_count INTEGER NOT NULL DEFAULT 0,
    source_snapshot_id TEXT   -- Device Intelligence MCP snapshot ID that produced this graph state
);

CREATE INDEX IF NOT EXISTS idx_snapshots_timestamp ON snapshots(timestamp);

-- Fact table: serialized natural language facts for semantic indexing
CREATE TABLE IF NOT EXISTS facts (
    id TEXT PRIMARY KEY,
    entity_id TEXT NOT NULL,
    fact_text TEXT NOT NULL,          -- NL sentence for embedding
    observed_at TEXT NOT NULL,        -- ISO 8601
    snapshot_id TEXT,
    indexed INTEGER NOT NULL DEFAULT 0,  -- 1 = indexed in Semantic Search
    FOREIGN KEY (entity_id) REFERENCES entities(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_facts_entity ON facts(entity_id);
CREATE INDEX IF NOT EXISTS idx_facts_observed ON facts(observed_at);
CREATE INDEX IF NOT EXISTS idx_facts_indexed ON facts(indexed);
