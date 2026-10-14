-- The assistant's conversation history (phase P7). Idempotent; run after every migration.

-- Messages are append-only. Claude's saved reasoning (thinking blocks and their signatures) is only accepted while
-- everything before it in the conversation is unchanged, and the prompt cache depends on the same thing, so a
-- stored message is never edited. Deleting a whole conversation (cascade from AiConversations) is allowed.
IF OBJECT_ID('dbo.AiMessages', 'U') IS NOT NULL
    EXEC('CREATE OR ALTER TRIGGER dbo.TR_AiMessages_AppendOnly ON dbo.AiMessages AFTER UPDATE AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''AiMessages is append-only: stored conversation messages cannot be changed.'', 1;
    END');
GO
