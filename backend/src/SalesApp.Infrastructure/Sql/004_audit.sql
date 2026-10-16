-- The audit trail. Idempotent; run after every migration.

-- Audit entries are append-only: an entry can't be changed or removed, even by the application, so the trail can
-- be trusted after the fact. (Someone with direct database rights could still drop the trigger; that is what
-- database permissions and backups are for.)
IF OBJECT_ID('dbo.AuditEntries', 'U') IS NOT NULL
    EXEC('CREATE OR ALTER TRIGGER dbo.TR_AuditEntries_AppendOnly ON dbo.AuditEntries AFTER UPDATE, DELETE AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51001, ''AuditEntries is append-only: audit entries cannot be changed or removed.'', 1;
    END');
GO
