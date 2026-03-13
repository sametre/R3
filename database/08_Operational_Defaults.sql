/*
  R3 ERP - Required operational definitions for a new company.
  Creates no demo documents or transactions.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
USE EngineeringERP;
GO
DECLARE @Year int=YEAR(GETDATE());
INSERT core.FiscalPeriod(CompanyId,PeriodName,StartDate,EndDate,IsClosed)
SELECT c.CompanyId,CONCAT(@Year,N' Mali Dönemi'),
 DATEFROMPARTS(@Year,1,1),DATEFROMPARTS(@Year,12,31),0
FROM core.Company c WHERE c.IsActive=1
AND NOT EXISTS(SELECT 1 FROM core.FiscalPeriod p WHERE p.CompanyId=c.CompanyId AND p.StartDate=DATEFROMPARTS(@Year,1,1) AND p.EndDate=DATEFROMPARTS(@Year,12,31));
GO
INSERT inv.Warehouse(CompanyId,BranchId,WarehouseCode,WarehouseName,AllowNegativeStock,IsActive)
SELECT b.CompanyId,b.BranchId,'SATIS',CONCAT(b.BranchName,N' Satış Deposu'),0,1
FROM core.Branch b WHERE b.IsActive=1
AND NOT EXISTS(SELECT 1 FROM inv.Warehouse w WHERE w.CompanyId=b.CompanyId AND w.WarehouseCode='SATIS');
GO
INSERT fin.CashAccount(CompanyId,BranchId,CashCode,CashName,CurrencyCode,IsActive)
SELECT b.CompanyId,b.BranchId,'KASA1',CONCAT(b.BranchName,N' Kasa 1'),'TRY',1
FROM core.Branch b WHERE b.IsActive=1
AND NOT EXISTS(SELECT 1 FROM fin.CashAccount c WHERE c.CompanyId=b.CompanyId AND c.CashCode='KASA1');
GO
