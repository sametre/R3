SET NOCOUNT ON; SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
USE EngineeringERP;
GO
IF COL_LENGTH('doc.Invoice','DispatchNumber') IS NULL
    ALTER TABLE doc.Invoice ADD DispatchNumber nvarchar(50) NULL;
IF COL_LENGTH('doc.Invoice','DispatchDate') IS NULL
    ALTER TABLE doc.Invoice ADD DispatchDate date NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('doc.Invoice') AND name='IX_Invoice_DispatchNumber')
    CREATE INDEX IX_Invoice_DispatchNumber ON doc.Invoice(CompanyId,DispatchNumber) WHERE DispatchNumber IS NOT NULL;
GO
