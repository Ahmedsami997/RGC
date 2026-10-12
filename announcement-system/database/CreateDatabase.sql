-- RGC Announcements – SQL Server schema
-- The server creates this automatically on first start (EnsureCreated). Use this script
-- only if your DBA prefers to create the database up front. Generated from the EF Core model.

IF DB_ID(N'RGC_Announcements') IS NULL
    CREATE DATABASE [RGC_Announcements];
GO
USE [RGC_Announcements];
GO

CREATE TABLE [AdminUsers] (
    [Id] int NOT NULL IDENTITY,
    [Username] nvarchar(100) NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [PasswordHash] nvarchar(500) NOT NULL,
    [IsActive] bit NOT NULL,
    [FailedLoginCount] int NOT NULL,
    [LockoutEndUtc] datetime2 NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [LastLoginUtc] datetime2 NULL,
    CONSTRAINT [PK_AdminUsers] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Announcements] (
    [Id] uniqueidentifier NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [Priority] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] nvarchar(100) NOT NULL,
    [LastResentAtUtc] datetime2 NULL,
    [Audience] nvarchar(100) NULL,
    CONSTRAINT [PK_Announcements] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Clients] (
    [Id] uniqueidentifier NOT NULL,
    [MachineName] nvarchar(100) NOT NULL,
    [UserName] nvarchar(200) NOT NULL,
    [UserDisplayName] nvarchar(200) NULL,
    [WindowsUser] nvarchar(200) NULL,
    [IpAddress] nvarchar(100) NULL,
    [PublicIp] nvarchar(100) NULL,
    [OsVersion] nvarchar(200) NULL,
    [AgentVersion] nvarchar(50) NOT NULL,
    [IsOnline] bit NOT NULL,
    [FirstSeenUtc] datetime2 NOT NULL,
    [LastSeenUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Clients] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [AnnouncementRecipients] (
    [AnnouncementId] uniqueidentifier NOT NULL,
    [ClientId] uniqueidentifier NOT NULL,
    [DeliveredAtUtc] datetime2 NULL,
    [DisplayedAtUtc] datetime2 NULL,
    [AcknowledgedAtUtc] datetime2 NULL,
    [AckReceivedAtUtc] datetime2 NULL,
    [AcknowledgedBy] nvarchar(200) NULL,
    CONSTRAINT [PK_AnnouncementRecipients] PRIMARY KEY ([AnnouncementId], [ClientId]),
    CONSTRAINT [FK_AnnouncementRecipients_Announcements_AnnouncementId] FOREIGN KEY ([AnnouncementId]) REFERENCES [Announcements] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AnnouncementRecipients_Clients_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Clients] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [ClientActivity] (
    [Day] date NOT NULL,
    [ClientId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ClientActivity] PRIMARY KEY ([Day], [ClientId])
);
GO


CREATE TABLE [ChatMessages] (
    [Id] bigint NOT NULL IDENTITY,
    [ClientId] uniqueidentifier NOT NULL,
    [FromAdmin] bit NOT NULL,
    [Author] nvarchar(200) NOT NULL,
    [Text] nvarchar(2000) NOT NULL,
    [SentAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_ChatMessages] PRIMARY KEY ([Id])
);
GO


CREATE INDEX [IX_ChatMessages_ClientId_SentAtUtc] ON [ChatMessages] ([ClientId], [SentAtUtc]);
GO


CREATE UNIQUE INDEX [IX_AdminUsers_Username] ON [AdminUsers] ([Username]);
GO


CREATE INDEX [IX_AnnouncementRecipients_ClientId_AcknowledgedAtUtc] ON [AnnouncementRecipients] ([ClientId], [AcknowledgedAtUtc]);
GO


CREATE INDEX [IX_Announcements_CreatedAtUtc] ON [Announcements] ([CreatedAtUtc]);
GO


CREATE INDEX [IX_Clients_MachineName] ON [Clients] ([MachineName]);
GO


