CREATE TABLE id_reservations (singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton), upper_bound bigint NOT NULL CHECK (upper_bound >= 0));
INSERT INTO id_reservations VALUES (true, 0);
CREATE TABLE deletion_markers (user_id uuid PRIMARY KEY, job_id uuid UNIQUE NOT NULL, requested_at timestamptz NOT NULL, completed_at timestamptz);
