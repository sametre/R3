/*
  R3 ERP - Required reference definitions for inventory and account cards.
  These are system master definitions, not demo transaction data.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE EngineeringERP;
GO

INSERT inv.Unit(CompanyId,UnitCode,UnitName,DecimalPlaces,IsActive)
SELECT c.CompanyId,v.UnitCode,v.UnitName,v.DecimalPlaces,1
FROM core.Company c
CROSS JOIN (VALUES
 ('ADET',N'Adet',0),('KG',N'Kilogram',3),('GR',N'Gram',3),('LT',N'Litre',3),
 ('MT',N'Metre',3),('PAKET',N'Paket',0),('KOLI',N'Koli',0),('HIZMET',N'Hizmet',2)
) v(UnitCode,UnitName,DecimalPlaces)
WHERE c.IsActive=1
AND NOT EXISTS(SELECT 1 FROM inv.Unit u WHERE u.CompanyId=c.CompanyId AND u.UnitCode=v.UnitCode);
GO

INSERT fin.PaymentPlan(CompanyId,PlanCode,PlanName,TermDays,IsActive)
SELECT c.CompanyId,v.PlanCode,v.PlanName,v.TermDays,1
FROM core.Company c
CROSS JOIN (VALUES
 ('PESIN',N'Peşin',0),('VADE7',N'7 Gün Vadeli',7),('VADE15',N'15 Gün Vadeli',15),
 ('VADE30',N'30 Gün Vadeli',30),('VADE45',N'45 Gün Vadeli',45),
 ('VADE60',N'60 Gün Vadeli',60),('VADE90',N'90 Gün Vadeli',90)
) v(PlanCode,PlanName,TermDays)
WHERE c.IsActive=1
AND NOT EXISTS(SELECT 1 FROM fin.PaymentPlan p WHERE p.CompanyId=c.CompanyId AND p.PlanCode=v.PlanCode);
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'inv.Product') AND name=N'IX_Product_List')
 CREATE INDEX IX_Product_List ON inv.Product(CompanyId,IsDeleted,IsActive,ProductCode)
 INCLUDE(ProductName,Barcode,BaseUnitId,SalesPrice,CriticalStockLevel);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'crm.Account') AND name=N'IX_Account_List')
 CREATE INDEX IX_Account_List ON crm.Account(CompanyId,IsDeleted,AccountType,IsActive,AccountCode)
 INCLUDE(LegalName,TaxNumber,IdentityNumber,Phone,CurrencyCode);
GO
