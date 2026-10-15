-- Settings for the AI "ask your data" query path (phase P6). Idempotent; run after every migration.

-- Snapshot isolation lets AI queries read a consistent picture without taking shared locks, so a slow
-- question never blocks order entry or invoicing, and is never blocked by them. Enabling it needs ALTER
-- permission on the database; if the app's login doesn't have that, the query path falls back to
-- READ COMMITTED (it checks the setting before every query).
BEGIN TRY
    IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND snapshot_isolation_state = 0)
        ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;
END TRY
BEGIN CATCH
    PRINT CONCAT('Snapshot isolation was not enabled: ', ERROR_MESSAGE());
END CATCH
GO

-- Note: SQL Server always lets a user change its OWN default schema (ALTER USER ... WITH DEFAULT_SCHEMA); that
-- can't be denied. It grants nothing (dbo stays denied) and SqlGuard refuses every statement except SELECT, so the
-- AI path can't issue it anyway.
