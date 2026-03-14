/*
 R3 ERP - Reversal and invoice payment allocation.
*/
SET NOCOUNT ON;SET XACT_ABORT ON;SET ANSI_NULLS ON;SET QUOTED_IDENTIFIER ON;
USE EngineeringERP;
GO
CREATE OR ALTER PROCEDURE doc.usp_ReverseInvoice @InvoiceId bigint,@UserId int,@Reason nvarchar(300)
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;
 DECLARE @C int,@B int,@Type tinyint,@Status tinyint,@No nvarchar(50),@Account bigint,@Currency char(3),@Rate decimal(19,8),@Grand decimal(19,4),@Local decimal(19,4);
 SELECT @C=CompanyId,@B=BranchId,@Type=InvoiceType,@Status=InvoiceStatus,@No=InvoiceNumber,@Account=AccountId,@Currency=CurrencyCode,@Rate=ExchangeRate,@Grand=GrandTotal,@Local=LocalGrandTotal
 FROM doc.Invoice WITH(UPDLOCK,HOLDLOCK) WHERE InvoiceId=@InvoiceId;
 IF @Status<>2 THROW 50200,N'Yalnızca onaylı fatura ters kayıtla iptal edilebilir.',1;
 IF EXISTS(SELECT 1 FROM fin.PaymentAllocation WHERE InvoiceId=@InvoiceId) THROW 50201,N'Ödeme tahsisi bulunan fatura önce tahsisleri kapatılmadan ters çevrilemez.',1;
 INSERT inv.StockTransaction(CompanyId,BranchId,WarehouseId,ProductId,TransactionDate,TransactionType,QuantityIn,QuantityOut,UnitCost,LocalAmount,
  SourceType,SourceId,SourceLineId,LotNumber,SerialNumber,Description,CreatedByUserId,ProductVariantId,AccountId,DocumentType,DocumentNumber)
 SELECT @C,@B,l.WarehouseId,l.ProductId,SYSUTCDATETIME(),CASE @Type WHEN 1 THEN 3 WHEN 2 THEN 4 WHEN 3 THEN 2 ELSE 1 END,
  CASE WHEN @Type IN(1,4) THEN l.BaseQuantity ELSE 0 END,CASE WHEN @Type IN(2,3) THEN l.BaseQuantity ELSE 0 END,
  CASE WHEN l.BaseQuantity=0 THEN 0 ELSE l.LineNet/l.BaseQuantity END,l.LineNet*@Rate,1,@InvoiceId,l.InvoiceLineId,l.LotNumber,l.SerialNumber,
  CONCAT(N'Ters kayıt: ',@Reason),@UserId,l.ProductVariantId,@Account,N'Fatura Ters Kayıt',CONCAT(N'REV-',@No)
 FROM doc.InvoiceLine l WHERE l.InvoiceId=@InvoiceId;
 UPDATE b SET QuantityOnHand=b.QuantityOnHand+x.Delta,LastMovementAtUtc=SYSUTCDATETIME()
 FROM inv.StockBalance b JOIN(SELECT l.WarehouseId,l.ProductId,SUM(CASE WHEN @Type IN(1,4) THEN l.BaseQuantity ELSE -l.BaseQuantity END) Delta FROM doc.InvoiceLine l WHERE l.InvoiceId=@InvoiceId GROUP BY l.WarehouseId,l.ProductId)x
 ON x.WarehouseId=b.WarehouseId AND x.ProductId=b.ProductId WHERE b.CompanyId=@C;
 UPDATE b SET QuantityOnHand=b.QuantityOnHand+x.Delta,LastMovementAtUtc=SYSUTCDATETIME()
 FROM inv.VariantStockBalance b JOIN(SELECT l.WarehouseId,l.ProductVariantId,SUM(CASE WHEN @Type IN(1,4) THEN l.BaseQuantity ELSE -l.BaseQuantity END) Delta FROM doc.InvoiceLine l WHERE l.InvoiceId=@InvoiceId AND l.ProductVariantId IS NOT NULL GROUP BY l.WarehouseId,l.ProductVariantId)x
 ON x.WarehouseId=b.WarehouseId AND x.ProductVariantId=b.ProductVariantId WHERE b.CompanyId=@C;
 INSERT fin.AccountLedger(CompanyId,BranchId,AccountId,TransactionDate,Debit,Credit,CurrencyCode,ForeignDebit,ForeignCredit,ExchangeRate,SourceType,SourceId,DocumentNumber,Description)
 VALUES(@C,@B,@Account,SYSUTCDATETIME(),CASE WHEN @Type IN(2,3) THEN @Local ELSE 0 END,CASE WHEN @Type IN(1,4) THEN @Local ELSE 0 END,@Currency,
  CASE WHEN @Type IN(2,3) THEN @Grand ELSE 0 END,CASE WHEN @Type IN(1,4) THEN @Grand ELSE 0 END,@Rate,4,@InvoiceId,CONCAT(N'REV-',@No),@Reason);
 UPDATE doc.Invoice SET InvoiceStatus=4,ReturnReason=@Reason,UpdatedByUserId=@UserId,UpdatedAtUtc=SYSUTCDATETIME() WHERE InvoiceId=@InvoiceId;
 INSERT audit.AuditLog(CompanyId,UserId,ActionType,SchemaName,TableName,RecordKey,NewValuesJson,MachineName)
 VALUES(@C,@UserId,'REVERSE','doc','Invoice',CONVERT(nvarchar(30),@InvoiceId),CONCAT(N'{"reason":"',STRING_ESCAPE(@Reason,'json'),N'"}'),HOST_NAME());
 COMMIT;
END;
GO
CREATE OR ALTER PROCEDURE fin.usp_ReverseFinancialTransaction @FinancialTransactionId bigint,@UserId int,@Reason nvarchar(300)
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;
 DECLARE @C int,@B int,@Type tinyint,@Status tinyint,@No nvarchar(50),@A bigint,@Cash int,@Bank int,@Cur char(3),@Rate decimal(19,8),@Amount decimal(19,4),@Local decimal(19,4);
 SELECT @C=CompanyId,@B=BranchId,@Type=TransactionType,@Status=Status,@No=DocumentNumber,@A=AccountId,@Cash=CashAccountId,@Bank=BankAccountId,@Cur=CurrencyCode,@Rate=ExchangeRate,@Amount=Amount,@Local=LocalAmount
 FROM fin.FinancialTransaction WITH(UPDLOCK,HOLDLOCK) WHERE FinancialTransactionId=@FinancialTransactionId;
 IF @Status<>2 THROW 50210,N'Yalnızca onaylı finans işlemi ters kayıtla iptal edilebilir.',1;
 IF EXISTS(SELECT 1 FROM fin.PaymentAllocation WHERE FinancialTransactionId=@FinancialTransactionId) THROW 50211,N'Faturaya tahsis edilmiş işlem ters çevrilemez.',1;
 IF @A IS NOT NULL INSERT fin.AccountLedger(CompanyId,BranchId,AccountId,TransactionDate,Debit,Credit,CurrencyCode,ForeignDebit,ForeignCredit,ExchangeRate,SourceType,SourceId,DocumentNumber,Description)
 VALUES(@C,@B,@A,SYSUTCDATETIME(),CASE WHEN @Type IN(1,3,5) THEN @Local ELSE 0 END,CASE WHEN @Type IN(2,4,6) THEN @Local ELSE 0 END,@Cur,
  CASE WHEN @Type IN(1,3,5) THEN @Amount ELSE 0 END,CASE WHEN @Type IN(2,4,6) THEN @Amount ELSE 0 END,@Rate,4,@FinancialTransactionId,CONCAT(N'REV-',@No),@Reason);
 IF @Cash IS NOT NULL INSERT fin.CashMovement(CompanyId,BranchId,CashAccountId,MovementType,DocumentNumber,MovementDate,AccountId,Direction,CurrencyCode,Amount,Description,CreatedByUserId)
 VALUES(@C,@B,@Cash,8,CONCAT(N'REV-',@No),SYSUTCDATETIME(),@A,CASE WHEN @Type IN(1,3,5) THEN 2 ELSE 1 END,@Cur,@Amount,@Reason,@UserId);
 IF @Bank IS NOT NULL INSERT fin.BankMovement(CompanyId,BranchId,BankAccountId,MovementType,DocumentNumber,MovementDate,AccountId,Direction,CurrencyCode,ExchangeRate,Amount,LocalAmount,Description,CreatedByUserId)
 VALUES(@C,@B,@Bank,5,CONCAT(N'REV-',@No),SYSUTCDATETIME(),@A,CASE WHEN @Type IN(1,3,5) THEN 2 ELSE 1 END,@Cur,@Rate,@Amount,@Local,@Reason,@UserId);
 UPDATE fin.FinancialTransaction SET Status=3 WHERE FinancialTransactionId=@FinancialTransactionId;
 INSERT audit.AuditLog(CompanyId,UserId,ActionType,SchemaName,TableName,RecordKey,NewValuesJson,MachineName)
 VALUES(@C,@UserId,'REVERSE','fin','FinancialTransaction',CONVERT(nvarchar(30),@FinancialTransactionId),CONCAT(N'{"reason":"',STRING_ESCAPE(@Reason,'json'),N'"}'),HOST_NAME());
 COMMIT;
END;
GO
CREATE OR ALTER PROCEDURE fin.usp_AllocatePayment @FinancialTransactionId bigint,@InvoiceId bigint,@Amount decimal(19,4),@UserId int
AS
BEGIN
 SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRAN;
 DECLARE @FinStatus tinyint,@FinType tinyint,@FinAccount bigint,@FinAmount decimal(19,4),@InvStatus tinyint,@InvType tinyint,@InvAccount bigint,@Grand decimal(19,4),@Paid decimal(19,4);
 SELECT @FinStatus=Status,@FinType=TransactionType,@FinAccount=AccountId,@FinAmount=Amount FROM fin.FinancialTransaction WITH(UPDLOCK,HOLDLOCK) WHERE FinancialTransactionId=@FinancialTransactionId;
 SELECT @InvStatus=InvoiceStatus,@InvType=InvoiceType,@InvAccount=AccountId,@Grand=GrandTotal,@Paid=PaidTotal FROM doc.Invoice WITH(UPDLOCK,HOLDLOCK) WHERE InvoiceId=@InvoiceId;
 IF @FinStatus<>2 OR @InvStatus<>2 THROW 50220,N'Yalnızca onaylı finans işlemi ve onaylı faturaya tahsis yapılabilir.',1;
 IF @FinAccount<>@InvAccount THROW 50221,N'Finans işlemi ile fatura aynı cari hesaba ait olmalıdır.',1;
 IF NOT((@InvType IN(1,4) AND @FinType=1) OR (@InvType IN(2,3) AND @FinType=2)) THROW 50222,N'Tahsilat/ödeme yönü fatura türüyle uyumlu değil.',1;
 IF @Amount<=0 OR @Amount>@Grand-@Paid THROW 50223,N'Tahsis tutarı fatura kalan bakiyesini aşamaz.',1;
 IF @Amount>@FinAmount-COALESCE((SELECT SUM(AllocatedAmount) FROM fin.PaymentAllocation WHERE FinancialTransactionId=@FinancialTransactionId),0)
  THROW 50224,N'Tahsis tutarı finans işleminin kullanılabilir tutarını aşamaz.',1;
 INSERT fin.PaymentAllocation(FinancialTransactionId,InvoiceId,AllocatedAmount,CreatedByUserId) VALUES(@FinancialTransactionId,@InvoiceId,@Amount,@UserId);
 UPDATE doc.Invoice SET PaidTotal=PaidTotal+@Amount WHERE InvoiceId=@InvoiceId;
 COMMIT;
END;
GO
