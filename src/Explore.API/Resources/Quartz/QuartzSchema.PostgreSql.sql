CREATE TABLE IF NOT EXISTS {prefix}job_details (
    sched_name TEXT NOT NULL,
    job_name TEXT NOT NULL,
    job_group TEXT NOT NULL,
    description TEXT NULL,
    job_class_name TEXT NOT NULL,
    is_durable BOOLEAN NOT NULL,
    is_nonconcurrent BOOLEAN NOT NULL,
    is_update_data BOOLEAN NOT NULL,
    requests_recovery BOOLEAN NOT NULL,
    job_data BYTEA NULL,
    PRIMARY KEY (sched_name, job_name, job_group)
)
GO

CREATE TABLE IF NOT EXISTS {prefix}triggers (
    sched_name TEXT NOT NULL,
    trigger_name TEXT NOT NULL,
    trigger_group TEXT NOT NULL,
    job_name TEXT NOT NULL,
    job_group TEXT NOT NULL,
    description TEXT NULL,
    next_fire_time BIGINT NULL,
    prev_fire_time BIGINT NULL,
    priority INTEGER NULL,
    trigger_state TEXT NOT NULL,
    trigger_type TEXT NOT NULL,
    start_time BIGINT NOT NULL,
    end_time BIGINT NULL,
    calendar_name TEXT NULL,
    misfire_instr SMALLINT NULL,
    misfire_orig_fire_time BIGINT NULL,
    job_data BYTEA NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, job_name, job_group)
        REFERENCES {prefix}job_details (sched_name, job_name, job_group)
)
GO

CREATE TABLE IF NOT EXISTS {prefix}simple_triggers (
    sched_name TEXT NOT NULL,
    trigger_name TEXT NOT NULL,
    trigger_group TEXT NOT NULL,
    repeat_count BIGINT NOT NULL,
    repeat_interval BIGINT NOT NULL,
    times_triggered BIGINT NOT NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, trigger_name, trigger_group)
        REFERENCES {prefix}triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
)
GO

CREATE TABLE IF NOT EXISTS {prefix}cron_triggers (
    sched_name TEXT NOT NULL,
    trigger_name TEXT NOT NULL,
    trigger_group TEXT NOT NULL,
    cron_expression TEXT NOT NULL,
    time_zone_id TEXT NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, trigger_name, trigger_group)
        REFERENCES {prefix}triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
)
GO

CREATE TABLE IF NOT EXISTS {prefix}simprop_triggers (
    sched_name TEXT NOT NULL,
    trigger_name TEXT NOT NULL,
    trigger_group TEXT NOT NULL,
    str_prop_1 TEXT NULL,
    str_prop_2 TEXT NULL,
    str_prop_3 TEXT NULL,
    int_prop_1 INTEGER NULL,
    int_prop_2 INTEGER NULL,
    long_prop_1 BIGINT NULL,
    long_prop_2 BIGINT NULL,
    dec_prop_1 NUMERIC NULL,
    dec_prop_2 NUMERIC NULL,
    bool_prop_1 BOOLEAN NULL,
    bool_prop_2 BOOLEAN NULL,
    time_zone_id TEXT NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, trigger_name, trigger_group)
        REFERENCES {prefix}triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
)
GO

CREATE TABLE IF NOT EXISTS {prefix}blob_triggers (
    sched_name TEXT NOT NULL,
    trigger_name TEXT NOT NULL,
    trigger_group TEXT NOT NULL,
    blob_data BYTEA NULL,
    PRIMARY KEY (sched_name, trigger_name, trigger_group),
    FOREIGN KEY (sched_name, trigger_name, trigger_group)
        REFERENCES {prefix}triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
)
GO

CREATE TABLE IF NOT EXISTS {prefix}calendars (
    sched_name TEXT NOT NULL,
    calendar_name TEXT NOT NULL,
    calendar BYTEA NOT NULL,
    PRIMARY KEY (sched_name, calendar_name)
)
GO

CREATE TABLE IF NOT EXISTS {prefix}paused_trigger_grps (
    sched_name TEXT NOT NULL,
    trigger_group TEXT NOT NULL,
    PRIMARY KEY (sched_name, trigger_group)
)
GO

CREATE TABLE IF NOT EXISTS {prefix}fired_triggers (
    sched_name TEXT NOT NULL,
    entry_id TEXT NOT NULL,
    trigger_name TEXT NOT NULL,
    trigger_group TEXT NOT NULL,
    instance_name TEXT NOT NULL,
    fired_time BIGINT NOT NULL,
    sched_time BIGINT NOT NULL,
    priority INTEGER NOT NULL,
    state TEXT NOT NULL,
    job_name TEXT NULL,
    job_group TEXT NULL,
    is_nonconcurrent BOOLEAN NULL,
    requests_recovery BOOLEAN NULL,
    PRIMARY KEY (sched_name, entry_id)
)
GO

CREATE TABLE IF NOT EXISTS {prefix}scheduler_state (
    sched_name TEXT NOT NULL,
    instance_name TEXT NOT NULL,
    last_checkin_time BIGINT NOT NULL,
    checkin_interval BIGINT NOT NULL,
    PRIMARY KEY (sched_name, instance_name)
)
GO

CREATE TABLE IF NOT EXISTS {prefix}locks (
    sched_name TEXT NOT NULL,
    lock_name TEXT NOT NULL,
    PRIMARY KEY (sched_name, lock_name)
)
GO

CREATE INDEX IF NOT EXISTS ix_{prefix}triggers_job
    ON {prefix}triggers (sched_name, job_name, job_group)
GO

CREATE INDEX IF NOT EXISTS ix_{prefix}triggers_state_next_fire
    ON {prefix}triggers (sched_name, trigger_state, next_fire_time)
GO

CREATE INDEX IF NOT EXISTS ix_{prefix}fired_triggers_instance
    ON {prefix}fired_triggers (sched_name, instance_name)
GO

CREATE INDEX IF NOT EXISTS ix_{prefix}fired_triggers_job
    ON {prefix}fired_triggers (sched_name, job_name, job_group)
GO

CREATE INDEX IF NOT EXISTS ix_{prefix}fired_triggers_trigger
    ON {prefix}fired_triggers (sched_name, trigger_name, trigger_group)
GO
