-- Knowledge Base Initial Schema
-- Apply with: sqlcmd -S (localdb)\mssqllocaldb -d KnowledgeBaseDb -i 001_InitialSchema.sql
-- This file is idempotent — safe to run multiple times.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users')
CREATE TABLE Users (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    GitLabUserId INT          NOT NULL,
    Name         NVARCHAR(200) NOT NULL,
    Username     NVARCHAR(100) NOT NULL,
    CONSTRAINT UQ_Users_GitLabUserId UNIQUE (GitLabUserId)
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Labels')
CREATE TABLE Labels (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Name        NVARCHAR(100) NOT NULL,
    Color       NVARCHAR(20)  NOT NULL,
    Description NVARCHAR(500) NULL,
    CONSTRAINT UQ_Labels_Name UNIQUE (Name)
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Issues')
CREATE TABLE Issues (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    GitLabId    INT            NOT NULL,
    Title       NVARCHAR(500)  NOT NULL,
    Description NVARCHAR(MAX)  NULL,
    State       NVARCHAR(50)   NOT NULL,
    WebUrl      NVARCHAR(1000) NOT NULL,
    KbCategory  NVARCHAR(200)  NULL,
    KbNotes     NVARCHAR(MAX)  NULL,
    AuthorId    INT            NOT NULL REFERENCES Users(Id),
    CreatedAt   DATETIME2      NOT NULL,
    UpdatedAt   DATETIME2      NOT NULL,
    CONSTRAINT UQ_Issues_GitLabId UNIQUE (GitLabId)
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IssueLabels')
CREATE TABLE IssueLabels (
    IssueId INT NOT NULL REFERENCES Issues(Id) ON DELETE CASCADE,
    LabelId INT NOT NULL REFERENCES Labels(Id) ON DELETE CASCADE,
    CONSTRAINT PK_IssueLabels PRIMARY KEY (IssueId, LabelId)
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Comments')
CREATE TABLE Comments (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    IssueId         INT           NOT NULL REFERENCES Issues(Id)   ON DELETE CASCADE,
    AuthorId        INT           NOT NULL REFERENCES Users(Id),
    Body            NVARCHAR(MAX) NOT NULL,
    ParentCommentId INT           NULL     REFERENCES Comments(Id), -- self-ref, no cascade to avoid loops
    CreatedAt       DATETIME2     NOT NULL,
    UpdatedAt       DATETIME2     NOT NULL
);
