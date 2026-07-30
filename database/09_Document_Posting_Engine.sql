/*
  R3 ERP - Atomic invoice and finance posting engine.
  Converts draft documents into immutable stock, account and cash/bank ledgers.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE EngineeringERP;
GO

IF COL_LENGTH(N'fin.CashMovement',N'FinancialTransactionId') IS NULL
 ALTER TABLE fin.CashMovement ADD FinancialTransactionId bigint NULL;
IF COL_LENGTH(N'fin.BankMovement',N'FinancialTransactionId') IS NULL
 ALTER TABLE fin.BankMovement ADD FinancialTransactionId bigint NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_CashMovement_FinancialTransaction')
 ALTER TABLE fin.CashMovement ADD CONSTRAINT FK_CashMovement_FinancialTransaction FOREIGN KEY(FinancialTransactionId) REFERENCES fin.FinancialTransaction(FinancialTransactionId);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_BankMovement_FinancialTransaction')
 ALTER TABLE fin.BankMovement ADD CONSTRAINT FK_BankMovement_FinancialTransaction FOREIGN KEY(FinancialTransactionId) REFERENCES fin.FinancialTransaction(FinancialTransactionId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'fin.CashMovement') AND name=N'UX_CashMovement_FinancialTransaction')
 CREATE UNIQUE INDEX UX_CashMovement_FinancialTransaction ON fin.CashMovement(FinancialTransactionId) WHERE FinancialTransactionId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'fin.BankMovement') AND name=N'UX_BankMovement_FinancialTransaction')
 CREATE UNIQUE INDEX UX_BankMovement_FinancialTransaction ON fin.BankMovement(FinancialTransactionId) WHERE FinancialTransactionId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'crm.AccountTransaction') AND name=N'UX_AccountTransaction_Invoice')
 CREATE UNIQUE INDEX UX_AccountTransaction_Invoice ON crm.AccountTransaction(InvoiceId) WHERE InvoiceId IS NOT NULL;
GO

CREATE OR ALTER PROCEDURE doc.usp_PostInvoice
 @InvoiceId bigint,@UserId int
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @CompanyId int,@BranchId int,@PeriodId int,@Type tinyint,@Status tinyint,@Number nvarchar(50),
  @Date date,@Due date,@AccountId bigint,@Currency char(3),@Rate decimal(19,8),@Grand decimal(19,4),@Local decimal(19,4);
 SELECT @CompanyId=CompanyId,@BranchId=BranchId,@PeriodId=FiscalPeriodId,@Type=InvoiceType,@Status=InvoiceStatus,
  @Number=InvoiceNumber,@Date=InvoiceDate,@Due=DueDate,@AccountId=AccountId,@Currency=CurrencyCode,@Rate=ExchangeRate,
  @Grand=GrandTotal,@Local=LocalGrandTotal
 FROM doc.Invoice WITH(UPDLOCK,HOLDLOCK) WHERE InvoiceId=@InvoiceId;
 IF @CompanyId IS NULL THROW 50100,N'Fatura bulunamadı.',1;
 IF @Status<>1 THROW 50101,N'Yalnızca taslak fatura onaylanabilir.',1;
 IF NOT EXISTS(SELECT 1 FROM doc.InvoiceLine WHERE InvoiceId=@InvoiceId) THROW 50102,N'Faturada satır bulunmuyor.',1;
 IF EXISTS(SELECT 1 FROM doc.InvoiceLine WHERE InvoiceId=@InvoiceId AND WarehouseId IS NULL)
  THROW 50103,N'Stok faturalarının bütün satırlarında depo zorunludur.',1;

 IF @Type IN(1,4) AND EXISTS
 (
  SELECT 1 FROM doc.InvoiceLine l JOIN inv.Warehouse w ON w.WarehouseId=l.WarehouseId
  LEFT JOIN inv.StockBalance b ON b.CompanyId=@CompanyId AND b.WarehouseId=l.WarehouseId AND b.ProductId=l.ProductId
  WHERE l.InvoiceId=@InvoiceId AND w.AllowNegativeStock=0 AND COALESCE(b.QuantityOnHand,0)<l.BaseQuantity
 )
  THROW 50104,N'Yetersiz stok nedeniyle fatura onaylanamadı.',1;

 INSERT inv.StockTransaction(CompanyId,BranchId,WarehouseId,ProductId,TransactionDate,TransactionType,
  QuantityIn,QuantityOut,UnitCost,LocalAmount,SourceType,SourceId,SourceLineId,LotNumber,SerialNumber,
  Description,CreatedByUserId,ProductVariantId,AccountId,DocumentType,DocumentNumber)
 SELECT @CompanyId,@BranchId,l.WarehouseId,l.ProductId,CAST(@Date AS datetime2),
  CASE @Type WHEN 1 THEN 2 WHEN 2 THEN 1 WHEN 3 THEN 3 ELSE 4 END,
  CASE WHEN @Type IN(2,3) THEN l.BaseQuantity ELSE 0 END,
  CASE WHEN @Type IN(1,4) THEN l.BaseQuantity ELSE 0 END,
  CASE WHEN l.BaseQuantity=0 THEN 0 ELSE l.LineNet/l.BaseQuantity END,l.LineNet*@Rate,1,@InvoiceId,l.InvoiceLineId,
  l.LotNumber,l.SerialNumber,l.Description,@UserId,l.ProductVariantId,@AccountId,N'Fatura',@Number
 FROM doc.InvoiceLine l WHERE l.InvoiceId=@InvoiceId;

 MERGE inv.StockBalance WITH(HOLDLOCK) AS target
 USING(SELECT @CompanyId CompanyId,l.WarehouseId,l.ProductId,
       SUM(CASE WHEN @Type IN(2,3) THEN l.BaseQuantity ELSE -l.BaseQuantity END) Delta,
       SUM(CASE WHEN @Type IN(2,3) THEN l.LineNet*@Rate ELSE 0 END) IncomingValue,
       SUM(CASE WHEN @Type IN(2,3) THEN l.BaseQuantity ELSE 0 END) IncomingQty
       FROM doc.InvoiceLine l WHERE l.InvoiceId=@InvoiceId GROUP BY l.WarehouseId,l.ProductId) source
 ON target.CompanyId=source.CompanyId AND target.WarehouseId=source.WarehouseId AND target.ProductId=source.ProductId
 WHEN MATCHED THEN UPDATE SET
  AverageUnitCost=CASE WHEN source.IncomingQty>0 AND target.QuantityOnHand+source.IncomingQty>0
   THEN ((target.QuantityOnHand*target.AverageUnitCost)+source.IncomingValue)/(target.QuantityOnHand+source.IncomingQty)
   ELSE target.AverageUnitCost END,
  QuantityOnHand=target.QuantityOnHand+source.Delta,LastMovementAtUtc=SYSUTCDATETIME()
 WHEN NOT MATCHED THEN INSERT(CompanyId,WarehouseId,ProductId,QuantityOnHand,ReservedQuantity,AverageUnitCost,LastMovementAtUtc)
  VALUES(source.CompanyId,source.WarehouseId,source.ProductId,source.Delta,0,
   CASE WHEN source.IncomingQty>0 THEN source.IncomingValue/source.IncomingQty ELSE 0 END,SYSUTCDATETIME());

 MERGE inv.VariantStockBalance WITH(HOLDLOCK) AS target
 USING(SELECT @CompanyId CompanyId,l.WarehouseId,l.ProductId,l.ProductVariantId,
       SUM(CASE WHEN @Type IN(2,3) THEN l.BaseQuantity ELSE -l.BaseQuantity END) Delta,
       SUM(CASE WHEN @Type IN(2,3) THEN l.LineNet*@Rate ELSE 0 END) IncomingValue,
       SUM(CASE WHEN @Type IN(2,3) THEN l.BaseQuantity ELSE 0 END) IncomingQty
       FROM doc.InvoiceLine l WHERE l.InvoiceId=@InvoiceId AND l.ProductVariantId IS NOT NULL
       GROUP BY l.WarehouseId,l.ProductId,l.ProductVariantId) source
 ON target.CompanyId=source.CompanyId AND target.WarehouseId=source.WarehouseId AND target.ProductVariantId=source.ProductVariantId
 WHEN MATCHED THEN UPDATE SET
  AverageUnitCost=CASE WHEN source.IncomingQty>0 AND target.QuantityOnHand+source.IncomingQty>0
   THEN ((target.QuantityOnHand*target.AverageUnitCost)+source.IncomingValue)/(target.QuantityOnHand+source.IncomingQty)
   ELSE target.AverageUnitCost END,
  QuantityOnHand=target.QuantityOnHand+source.Delta,LastMovementAtUtc=SYSUTCDATETIME()
 WHEN NOT MATCHED THEN INSERT(CompanyId,WarehouseId,ProductId,ProductVariantId,QuantityOnHand,ReservedQuantity,AverageUnitCost,LastMovementAtUtc)
  VALUES(source.CompanyId,source.WarehouseId,source.ProductId,source.ProductVariantId,source.Delta,0,
   CASE WHEN source.IncomingQty>0 THEN source.IncomingValue/source.IncomingQty ELSE 0 END,SYSUTCDATETIME());

 INSERT fin.AccountLedger(CompanyId,BranchId,AccountId,TransactionDate,DueDate,Debit,Credit,CurrencyCode,
  ForeignDebit,ForeignCredit,ExchangeRate,SourceType,SourceId,DocumentNumber,Description)
 VALUES(@CompanyId,@BranchId,@AccountId,@Date,@Due,
  CASE WHEN @Type IN(1,4) THEN @Local ELSE 0 END,CASE WHEN @Type IN(2,3) THEN @Local ELSE 0 END,@Currency,
  CASE WHEN @Type IN(1,4) THEN @Grand ELSE 0 END,CASE WHEN @Type IN(2,3) THEN @Grand ELSE 0 END,@Rate,1,@InvoiceId,@Number,N'Fatura onayı');

 INSERT crm.AccountTransaction(CompanyId,BranchId,FiscalPeriodId,AccountId,TransactionType,TransactionDate,DueDate,
  DocumentType,DocumentNumber,InvoiceId,CurrencyCode,ExchangeRate,Debit,Credit,LocalDebit,LocalCredit,Description,CreatedByUserId)
 VALUES(@CompanyId,@BranchId,@PeriodId,@AccountId,CASE @Type WHEN 1 THEN 1 WHEN 2 THEN 2 WHEN 3 THEN 3 ELSE 4 END,
  @Date,@Due,N'Fatura',@Number,@InvoiceId,@Currency,@Rate,
  CASE WHEN @Type IN(1,4) THEN @Grand ELSE 0 END,CASE WHEN @Type IN(2,3) THEN @Grand ELSE 0 END,
  CASE WHEN @Type IN(1,4) THEN @Local ELSE 0 END,CASE WHEN @Type IN(2,3) THEN @Local ELSE 0 END,N'Fatura onayı',@UserId);

 UPDATE doc.Invoice SET InvoiceStatus=2,PostedAtUtc=SYSUTCDATETIME(),PostedByUserId=@UserId WHERE InvoiceId=@InvoiceId;
 INSERT audit.AuditLog(CompanyId,UserId,ActionType,SchemaName,TableName,RecordKey,NewValuesJson,MachineName)
 VALUES(@CompanyId,@UserId,'POST','doc','Invoice',CONVERT(nvarchar(30),@InvoiceId),
  CONCAT(N'{"status":"Posted","document":"',STRING_ESCAPE(@Number,'json'),N'"}'),HOST_NAME());
 COMMIT;
END;
GO

CREATE OR ALTER PROCEDURE fin.usp_PostFinancialTransaction
 @FinancialTransactionId bigint,@UserId int
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;
 DECLARE @CompanyId int,@BranchId int,@PeriodId int,@Type tinyint,@Status tinyint,@Number nvarchar(50),@Date datetime2,
  @AccountId bigint,@CashId int,@BankId int,@Currency char(3),@Rate decimal(19,8),@Amount decimal(19,4),@Local decimal(19,4),@Description nvarchar(500);
 SELECT @CompanyId=CompanyId,@BranchId=BranchId,@PeriodId=FiscalPeriodId,@Type=TransactionType,@Status=Status,
  @Number=DocumentNumber,@Date=TransactionDate,@AccountId=AccountId,@CashId=CashAccountId,@BankId=BankAccountId,
  @Currency=CurrencyCode,@Rate=ExchangeRate,@Amount=Amount,@Local=LocalAmount,@Description=Description
 FROM fin.FinancialTransaction WITH(UPDLOCK,HOLDLOCK) WHERE FinancialTransactionId=@FinancialTransactionId;
 IF @CompanyId IS NULL THROW 50110,N'Finans işlemi bulunamadı.',1;
 IF @Status<>1 THROW 50111,N'Yalnızca taslak finans işlemi onaylanabilir.',1;

 IF @AccountId IS NOT NULL
 BEGIN
  INSERT fin.AccountLedger(CompanyId,BranchId,AccountId,TransactionDate,Debit,Credit,CurrencyCode,ForeignDebit,ForeignCredit,
   ExchangeRate,SourceType,SourceId,DocumentNumber,Description)
  VALUES(@CompanyId,@BranchId,@AccountId,@Date,
   CASE WHEN @Type IN(2,4,6) THEN @Local ELSE 0 END,CASE WHEN @Type IN(1,3,5) THEN @Local ELSE 0 END,@Currency,
   CASE WHEN @Type IN(2,4,6) THEN @Amount ELSE 0 END,CASE WHEN @Type IN(1,3,5) THEN @Amount ELSE 0 END,
   @Rate,2,@FinancialTransactionId,@Number,@Description);
  INSERT crm.AccountTransaction(CompanyId,BranchId,FiscalPeriodId,AccountId,TransactionType,TransactionDate,
   DocumentType,DocumentNumber,FinancialTransactionId,CurrencyCode,ExchangeRate,Debit,Credit,LocalDebit,LocalCredit,Description,CreatedByUserId)
  VALUES(@CompanyId,@BranchId,@PeriodId,@AccountId,CASE WHEN @Type=1 THEN 5 WHEN @Type=2 THEN 6 ELSE 11 END,@Date,
   N'Finans',@Number,@FinancialTransactionId,@Currency,@Rate,
   CASE WHEN @Type IN(2,4,6) THEN @Amount ELSE 0 END,CASE WHEN @Type IN(1,3,5) THEN @Amount ELSE 0 END,
   CASE WHEN @Type IN(2,4,6) THEN @Local ELSE 0 END,CASE WHEN @Type IN(1,3,5) THEN @Local ELSE 0 END,@Description,@UserId);
 END;
 IF @CashId IS NOT NULL
  INSERT fin.CashMovement(CompanyId,BranchId,CashAccountId,MovementType,DocumentNumber,MovementDate,AccountId,Direction,
   CurrencyCode,Amount,Description,CreatedByUserId,FinancialTransactionId)
  VALUES(@CompanyId,@BranchId,@CashId,CASE WHEN @Type=1 THEN 2 WHEN @Type=2 THEN 3 ELSE @Type END,@Number,@Date,@AccountId,
   CASE WHEN @Type IN(1,3,5) THEN 1 ELSE 2 END,@Currency,@Amount,@Description,@UserId,@FinancialTransactionId);
 IF @BankId IS NOT NULL
  INSERT fin.BankMovement(CompanyId,BranchId,BankAccountId,MovementType,DocumentNumber,MovementDate,AccountId,Direction,
   CurrencyCode,ExchangeRate,Amount,LocalAmount,ReferenceNumber,Description,CreatedByUserId,FinancialTransactionId)
  SELECT @CompanyId,@BranchId,@BankId,CASE WHEN @Type IN(1,5) THEN 1 ELSE 2 END,@Number,@Date,@AccountId,
   CASE WHEN @Type IN(1,3,5) THEN 1 ELSE 2 END,@Currency,@Rate,@Amount,@Local,ReferenceNumber,@Description,@UserId,@FinancialTransactionId
  FROM fin.FinancialTransaction WHERE FinancialTransactionId=@FinancialTransactionId;
 UPDATE fin.FinancialTransaction SET Status=2,PostedAtUtc=SYSUTCDATETIME(),PostedByUserId=@UserId WHERE FinancialTransactionId=@FinancialTransactionId;
 INSERT audit.AuditLog(CompanyId,UserId,ActionType,SchemaName,TableName,RecordKey,NewValuesJson,MachineName)
 VALUES(@CompanyId,@UserId,'POST','fin','FinancialTransaction',CONVERT(nvarchar(30),@FinancialTransactionId),
  CONCAT(N'{"status":"Posted","document":"',STRING_ESCAPE(@Number,'json'),N'"}'),HOST_NAME());
 COMMIT;
END;
GO

CREATE OR ALTER PROCEDURE doc.usp_CancelDraftInvoice @InvoiceId bigint,@UserId int AS
BEGIN SET NOCOUNT ON;UPDATE doc.Invoice SET InvoiceStatus=3,UpdatedByUserId=@UserId,UpdatedAtUtc=SYSUTCDATETIME()
 WHERE InvoiceId=@InvoiceId AND InvoiceStatus=1;IF @@ROWCOUNT=0 THROW 50120,N'Yalnızca taslak fatura iptal edilebilir.',1;END;
GO
CREATE OR ALTER PROCEDURE fin.usp_CancelDraftFinancialTransaction @FinancialTransactionId bigint,@UserId int AS
BEGIN SET NOCOUNT ON;UPDATE fin.FinancialTransaction SET Status=3 WHERE FinancialTransactionId=@FinancialTransactionId AND Status=1;
 IF @@ROWCOUNT=0 THROW 50121,N'Yalnızca taslak finans işlemi iptal edilebilir.',1;END;
GO
