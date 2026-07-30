SET NOCOUNT ON; SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
USE EngineeringERP;
GO
IF OBJECT_ID(N'core.AgendaEvent',N'U') IS NULL
BEGIN
 CREATE TABLE core.AgendaEvent(
  AgendaEventId bigint IDENTITY CONSTRAINT PK_AgendaEvent PRIMARY KEY,
  CompanyId int NOT NULL, BranchId int NULL, UserId int NULL,
  EventType tinyint NOT NULL, Title nvarchar(160) NOT NULL, EventDate datetime2(0) NOT NULL,
  Description nvarchar(700) NULL, IsCompleted bit NOT NULL CONSTRAINT DF_AgendaEvent_Completed DEFAULT(0),
  CreatedByUserId int NOT NULL, CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_AgendaEvent_Created DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_AgendaEvent_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_AgendaEvent_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
  CONSTRAINT FK_AgendaEvent_User FOREIGN KEY(UserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT FK_AgendaEvent_CreatedBy FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_AgendaEvent_Type CHECK(EventType BETWEEN 1 AND 4)
 );
 CREATE INDEX IX_AgendaEvent_Date ON core.AgendaEvent(CompanyId,EventDate) INCLUDE(EventType,Title,IsCompleted);
END;
GO
