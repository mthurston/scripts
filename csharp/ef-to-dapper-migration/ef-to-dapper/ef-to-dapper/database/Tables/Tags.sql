-- Tags lookup table (8 rows seeded)
CREATE TABLE [dbo].[Tags]
(
    [Id]    INT            NOT NULL IDENTITY(1, 1),
    [Name]  NVARCHAR (MAX) NOT NULL,
    [Color] NVARCHAR (MAX) NOT NULL,
    CONSTRAINT [PK_Tags] PRIMARY KEY CLUSTERED ([Id] ASC)
);
