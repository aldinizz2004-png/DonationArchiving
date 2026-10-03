CREATE TABLE IF NOT EXISTS archive_history
(
    archive_id UUID PRIMARY KEY,
    sequence_number BIGSERIAL UNIQUE NOT NULL,

    previous_hash TEXT NOT NULL,
    current_hash TEXT NULL,

    -- Kept as s3_key for compatibility with existing databases; it now stores
    -- a path relative to LocalArchive, not an S3 object key.
    s3_key TEXT UNIQUE NOT NULL,

    record_count INT NOT NULL CHECK (record_count >= 0),

    status VARCHAR(20) NOT NULL DEFAULT 'Pending'
        CHECK (status IN ('Pending', 'Completed')),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,

    last_error TEXT NULL
);

CREATE TABLE IF NOT EXISTS donations
(
    id BIGSERIAL PRIMARY KEY,

    username VARCHAR(100) NOT NULL
        CHECK (LENGTH(TRIM(username)) > 0),

    donation_money NUMERIC(12,2) NOT NULL
        CHECK (donation_money > 0),

    donation_date TIMESTAMPTZ NOT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    archive_batch_id UUID NULL,

    CONSTRAINT fk_archive_batch
        FOREIGN KEY (archive_batch_id)
        REFERENCES archive_history(archive_id)
);

CREATE INDEX IF NOT EXISTS idx_donations_archive_candidates
ON donations(donation_date)
WHERE archive_batch_id IS NULL;


/*
Only one unfinished archive is allowed at a time.

This prevents two Cron executions from creating
two archive branches at the same time.
*/
CREATE UNIQUE INDEX IF NOT EXISTS ux_single_pending_archive
ON archive_history(status)
WHERE status = 'Pending';
