-- Comments table
-- FK to Authors uses NO ACTION to avoid a multiple-cascade-path error.
-- (Author → Post → Comment already forms one cascade path;
--  a second Author → Comment CASCADE would be rejected by SQL Server.)
CREATE TABLE [dbo].[Comments]
(
    [Id]       INT            NOT NULL IDENTITY(1, 1),
    [PostId]   INT            NOT NULL,
    [AuthorId] INT            NOT NULL,
    [Body]     NVARCHAR (MAX) NOT NULL,
    [PostedAt] DATETIME2 (7)  NOT NULL,
    CONSTRAINT [PK_Comments] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Comments_Posts_PostId]
        FOREIGN KEY ([PostId])   REFERENCES [dbo].[Posts]   ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Comments_Authors_AuthorId]
        FOREIGN KEY ([AuthorId]) REFERENCES [dbo].[Authors] ([Id]) ON DELETE NO ACTION
);
