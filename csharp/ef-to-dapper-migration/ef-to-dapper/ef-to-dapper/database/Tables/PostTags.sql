-- PostTags join table (many-to-many between Posts and Tags)
-- Composite primary key matches the EF Core HasKey(pt => new { pt.PostId, pt.TagId }) config.
-- The "Cartesian Explosion Demo" post is assigned all 8 tags here — used in Scenario 2.
CREATE TABLE [dbo].[PostTags]
(
    [PostId] INT NOT NULL,
    [TagId]  INT NOT NULL,
    CONSTRAINT [PK_PostTags] PRIMARY KEY CLUSTERED ([PostId] ASC, [TagId] ASC),
    CONSTRAINT [FK_PostTags_Posts_PostId]
        FOREIGN KEY ([PostId]) REFERENCES [dbo].[Posts] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PostTags_Tags_TagId]
        FOREIGN KEY ([TagId])  REFERENCES [dbo].[Tags]  ([Id]) ON DELETE CASCADE
);
