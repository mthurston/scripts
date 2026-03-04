-- Posts table
-- Body is nvarchar(max) — deliberately wide to illustrate over-fetching cost
-- in Scenario 5 (Over-fetching).
-- Status values: Published | Draft | Archived
CREATE TABLE [dbo].[Posts]
(
    [Id]          INT            NOT NULL IDENTITY(1, 1),
    [Title]       NVARCHAR (MAX) NOT NULL,
    [Body]        NVARCHAR (MAX) NOT NULL,
    [AuthorId]    INT            NOT NULL,
    [PublishedAt] DATETIME2 (7)  NOT NULL,
    [Status]      NVARCHAR (MAX) NOT NULL CONSTRAINT [DF_Posts_Status] DEFAULT ('Published'),
    [ViewCount]   INT            NOT NULL CONSTRAINT [DF_Posts_ViewCount] DEFAULT (0),
    CONSTRAINT [PK_Posts] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Posts_Authors_AuthorId]
        FOREIGN KEY ([AuthorId]) REFERENCES [dbo].[Authors] ([Id]) ON DELETE CASCADE
);
