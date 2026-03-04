-- Authors table
-- EF Core maps: string → nvarchar(max), DateTime → datetime2(7), int → int
-- No index on Email column intentionally — Scenario 4 (Key Lookup) demonstrates
-- the cost of scanning this table to find a row by email.
CREATE TABLE [dbo].[Authors]
(
    [Id]       INT            NOT NULL IDENTITY(1, 1),
    [Name]     NVARCHAR (MAX) NOT NULL,
    [Email]    NVARCHAR (MAX) NOT NULL,
    [Bio]      NVARCHAR (MAX) NOT NULL,
    [JoinedAt] DATETIME2 (7)  NOT NULL,
    CONSTRAINT [PK_Authors] PRIMARY KEY CLUSTERED ([Id] ASC)
);
