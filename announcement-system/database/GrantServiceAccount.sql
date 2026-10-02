-- Gives the account the RGC server runs as (e.g. the Windows Service account) access to the database.
-- Replace DOMAIN\svc-rgc with your service account (or NT AUTHORITY\NETWORK SERVICE / the machine account DOMAIN\SERVER$).
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'DOMAIN\svc-rgc')
    CREATE LOGIN [DOMAIN\svc-rgc] FROM WINDOWS;
GO
USE [RGC_Announcements];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'DOMAIN\svc-rgc')
    CREATE USER [DOMAIN\svc-rgc] FOR LOGIN [DOMAIN\svc-rgc];
ALTER ROLE db_datareader ADD MEMBER [DOMAIN\svc-rgc];
ALTER ROLE db_datawriter ADD MEMBER [DOMAIN\svc-rgc];
-- Needed only if the server should create the tables itself on first start:
ALTER ROLE db_ddladmin ADD MEMBER [DOMAIN\svc-rgc];
GO
