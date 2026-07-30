/*
  R3 ERP - POS, cheque, note, retail, campaign, count, transfer,
  e-document, expense and accounting integration.
  Run after 05_Exchange_Dispatch_Account_Cash_Bank.sql.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE EngineeringERP;
GO
IF NOT EXISTS(SELECT 1 FROM sys.schemas WHERE name=N'ret') EXEC(N'CREATE SCHEMA ret');
IF NOT EXISTS(SELECT 1 FROM sys.schemas WHERE name=N'acc') EXEC(N'CREATE SCHEMA acc');
GO

IF OBJECT_ID(N'fin.PosAccount',N'U') IS NULL
BEGIN
 CREATE TABLE fin.PosAccount(
  PosAccountId int IDENTITY CONSTRAINT PK_PosAccount PRIMARY KEY, CompanyId int NOT NULL, BranchId int NOT NULL,
  StoreId int NULL, BankAccountId int NOT NULL, PosCode varchar(20) NOT NULL, PosName nvarchar(100) NOT NULL,
  MerchantNumber varchar(50) NULL, TerminalNumber varchar(50) NULL, DefaultCommissionRate decimal(7,4) NOT NULL DEFAULT(0),
  DefaultBlockingDays smallint NOT NULL DEFAULT(0), IsActive bit NOT NULL DEFAULT(1), RowVersion rowversion,
  CONSTRAINT FK_PosAccount_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_PosAccount_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
  CONSTRAINT FK_PosAccount_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
  CONSTRAINT FK_PosAccount_Bank FOREIGN KEY(BankAccountId) REFERENCES fin.BankAccount(BankAccountId),
  CONSTRAINT CK_PosAccount_Values CHECK(DefaultCommissionRate BETWEEN 0 AND 100 AND DefaultBlockingDays>=0),
  CONSTRAINT UQ_PosAccount_Code UNIQUE(CompanyId,PosCode));
 CREATE TABLE fin.PosTransaction(
  PosTransactionId bigint IDENTITY CONSTRAINT PK_PosTransaction PRIMARY KEY, PosAccountId int NOT NULL,
  InvoiceId bigint NULL, CashShiftId bigint NULL, TransactionDate datetime2(3) NOT NULL,
  TransactionType tinyint NOT NULL, ReferenceNumber nvarchar(100) NOT NULL, InstallmentCount tinyint NOT NULL DEFAULT(1),
  GrossAmount decimal(19,4) NOT NULL, CommissionRate decimal(7,4) NOT NULL, CommissionAmount decimal(19,4) NOT NULL,
  BlockingDays smallint NOT NULL, NetAmount AS (GrossAmount-CommissionAmount) PERSISTED,
  ExpectedPaymentDate date NOT NULL, ActualPaymentDate date NULL, Status tinyint NOT NULL DEFAULT(1),
  CreatedByUserId int NOT NULL, CreatedAtUtc datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_PosTx_Account FOREIGN KEY(PosAccountId) REFERENCES fin.PosAccount(PosAccountId),
  CONSTRAINT FK_PosTx_Invoice FOREIGN KEY(InvoiceId) REFERENCES doc.Invoice(InvoiceId),
  CONSTRAINT FK_PosTx_Shift FOREIGN KEY(CashShiftId) REFERENCES fin.CashShift(CashShiftId),
  CONSTRAINT FK_PosTx_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_PosTx_Type CHECK(TransactionType BETWEEN 1 AND 3),
  CONSTRAINT CK_PosTx_Status CHECK(Status BETWEEN 1 AND 5),
  CONSTRAINT CK_PosTx_Values CHECK(InstallmentCount BETWEEN 1 AND 36 AND GrossAmount>0 AND CommissionRate BETWEEN 0 AND 100 AND CommissionAmount>=0 AND CommissionAmount<=GrossAmount AND BlockingDays>=0),
  CONSTRAINT UQ_PosTx_Reference UNIQUE(PosAccountId,ReferenceNumber));
 CREATE INDEX IX_PosTx_Settlement ON fin.PosTransaction(Status,ExpectedPaymentDate) INCLUDE(PosAccountId,GrossAmount,CommissionAmount,ActualPaymentDate);
END;
GO

IF OBJECT_ID(N'fin.Cheque',N'U') IS NULL
BEGIN
 CREATE TABLE fin.Cheque(
  ChequeId bigint IDENTITY CONSTRAINT PK_Cheque PRIMARY KEY, CompanyId int NOT NULL, BranchId int NOT NULL,
  ChequeType tinyint NOT NULL, ChequeNumber nvarchar(50) NOT NULL, BankName nvarchar(150) NOT NULL,
  BankBranchName nvarchar(150) NULL, AccountNumber varchar(50) NULL, DrawerName nvarchar(200) NOT NULL,
  DueDate date NOT NULL, Amount decimal(19,4) NOT NULL, CurrencyCode char(3) NOT NULL, AccountId bigint NULL,
  PortfolioNumber nvarchar(50) NOT NULL, Status tinyint NOT NULL DEFAULT(1), Description nvarchar(500) NULL,
  CreatedByUserId int NOT NULL, CreatedAtUtc datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()), RowVersion rowversion,
  CONSTRAINT FK_Cheque_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_Cheque_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
  CONSTRAINT FK_Cheque_Currency FOREIGN KEY(CurrencyCode) REFERENCES core.Currency(CurrencyCode),
  CONSTRAINT FK_Cheque_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
  CONSTRAINT FK_Cheque_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_Cheque_Type CHECK(ChequeType BETWEEN 1 AND 5), CONSTRAINT CK_Cheque_Status CHECK(Status BETWEEN 1 AND 8),
  CONSTRAINT CK_Cheque_Amount CHECK(Amount>0), CONSTRAINT UQ_Cheque_Portfolio UNIQUE(CompanyId,PortfolioNumber));
 CREATE TABLE fin.ChequeStatusHistory(
  ChequeStatusHistoryId bigint IDENTITY CONSTRAINT PK_ChequeHistory PRIMARY KEY, ChequeId bigint NOT NULL,
  PreviousStatus tinyint NULL, NewStatus tinyint NOT NULL, ChangedAt datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()),
  BankAccountId int NULL, TransferredAccountId bigint NULL, Description nvarchar(500) NULL, ChangedByUserId int NOT NULL,
  CONSTRAINT FK_ChequeHistory_Cheque FOREIGN KEY(ChequeId) REFERENCES fin.Cheque(ChequeId),
  CONSTRAINT FK_ChequeHistory_Bank FOREIGN KEY(BankAccountId) REFERENCES fin.BankAccount(BankAccountId),
  CONSTRAINT FK_ChequeHistory_Account FOREIGN KEY(TransferredAccountId) REFERENCES crm.Account(AccountId),
  CONSTRAINT FK_ChequeHistory_User FOREIGN KEY(ChangedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_ChequeHistory_Status CHECK(NewStatus BETWEEN 1 AND 8));
 CREATE INDEX IX_Cheque_Due ON fin.Cheque(Status,DueDate) INCLUDE(Amount,CurrencyCode,AccountId);
END;
GO

IF OBJECT_ID(N'fin.PromissoryNote',N'U') IS NULL
BEGIN
 CREATE TABLE fin.PromissoryNote(
  PromissoryNoteId bigint IDENTITY CONSTRAINT PK_Note PRIMARY KEY, CompanyId int NOT NULL, BranchId int NOT NULL,
  NoteType tinyint NOT NULL, NoteNumber nvarchar(50) NOT NULL, DebtorName nvarchar(200) NOT NULL,
  CreditorName nvarchar(200) NOT NULL, IssueDate date NOT NULL, DueDate date NOT NULL, Amount decimal(19,4) NOT NULL,
  CurrencyCode char(3) NOT NULL, AccountId bigint NULL, GuarantorName nvarchar(200) NULL, PaymentPlace nvarchar(200) NULL,
  Status tinyint NOT NULL DEFAULT(1), Description nvarchar(500) NULL, CreatedByUserId int NOT NULL,
  CreatedAtUtc datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()), RowVersion rowversion,
  CONSTRAINT FK_Note_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_Note_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
  CONSTRAINT FK_Note_Currency FOREIGN KEY(CurrencyCode) REFERENCES core.Currency(CurrencyCode),
  CONSTRAINT FK_Note_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
  CONSTRAINT FK_Note_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_Note_Type CHECK(NoteType BETWEEN 1 AND 4), CONSTRAINT CK_Note_Status CHECK(Status BETWEEN 1 AND 7),
  CONSTRAINT CK_Note_Amount CHECK(Amount>0), CONSTRAINT UQ_Note_Number UNIQUE(CompanyId,NoteNumber));
 CREATE TABLE fin.PromissoryNoteStatusHistory(
  PromissoryNoteStatusHistoryId bigint IDENTITY CONSTRAINT PK_NoteHistory PRIMARY KEY, PromissoryNoteId bigint NOT NULL,
  PreviousStatus tinyint NULL, NewStatus tinyint NOT NULL, ChangedAt datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()),
  Description nvarchar(500) NULL, ChangedByUserId int NOT NULL,
  CONSTRAINT FK_NoteHistory_Note FOREIGN KEY(PromissoryNoteId) REFERENCES fin.PromissoryNote(PromissoryNoteId),
  CONSTRAINT FK_NoteHistory_User FOREIGN KEY(ChangedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_NoteHistory_Status CHECK(NewStatus BETWEEN 1 AND 7));
 CREATE INDEX IX_Note_Due ON fin.PromissoryNote(Status,DueDate) INCLUDE(Amount,CurrencyCode,AccountId);
END;
GO

IF OBJECT_ID(N'ret.SuspendedSale',N'U') IS NULL
BEGIN
 CREATE TABLE ret.SuspendedSale(
  SuspendedSaleId bigint IDENTITY CONSTRAINT PK_SuspendedSale PRIMARY KEY, CompanyId int NOT NULL, BranchId int NOT NULL,
  StoreId int NOT NULL, SalesTerminalId int NOT NULL, CashShiftId bigint NOT NULL, SuspendNumber nvarchar(50) NOT NULL,
  AccountId bigint NULL, Subtotal decimal(19,4) NOT NULL, CampaignDiscount decimal(19,4) NOT NULL DEFAULT(0),
  GeneralDiscount decimal(19,4) NOT NULL DEFAULT(0), GrandTotal decimal(19,4) NOT NULL, Notes nvarchar(300) NULL,
  SuspendedByUserId int NOT NULL, SuspendedAt datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()), Status tinyint NOT NULL DEFAULT(1),
  CONSTRAINT FK_SuspendedSale_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_SuspendedSale_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
  CONSTRAINT FK_SuspendedSale_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
  CONSTRAINT FK_SuspendedSale_Terminal FOREIGN KEY(SalesTerminalId) REFERENCES core.SalesTerminal(SalesTerminalId),
  CONSTRAINT FK_SuspendedSale_Shift FOREIGN KEY(CashShiftId) REFERENCES fin.CashShift(CashShiftId),
  CONSTRAINT FK_SuspendedSale_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
  CONSTRAINT FK_SuspendedSale_User FOREIGN KEY(SuspendedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_SuspendedSale_Status CHECK(Status BETWEEN 1 AND 3), CONSTRAINT CK_SuspendedSale_Total CHECK(Subtotal>=0 AND CampaignDiscount>=0 AND GeneralDiscount>=0 AND GrandTotal>=0),
  CONSTRAINT UQ_SuspendedSale_Number UNIQUE(CompanyId,SuspendNumber));
 CREATE TABLE ret.SuspendedSaleLine(
  SuspendedSaleLineId bigint IDENTITY CONSTRAINT PK_SuspendedSaleLine PRIMARY KEY, SuspendedSaleId bigint NOT NULL,
  LineNumber int NOT NULL, ProductId bigint NOT NULL, ProductVariantId bigint NOT NULL, Barcode varchar(50) NULL,
  Quantity decimal(19,6) NOT NULL, UnitPrice decimal(19,4) NOT NULL, LineDiscount decimal(19,4) NOT NULL DEFAULT(0),
  CampaignDiscount decimal(19,4) NOT NULL DEFAULT(0), LineTotal decimal(19,4) NOT NULL,
  CONSTRAINT FK_SuspendedLine_Header FOREIGN KEY(SuspendedSaleId) REFERENCES ret.SuspendedSale(SuspendedSaleId),
  CONSTRAINT FK_SuspendedLine_Product FOREIGN KEY(ProductId) REFERENCES inv.Product(ProductId),
  CONSTRAINT FK_SuspendedLine_Variant FOREIGN KEY(ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
  CONSTRAINT CK_SuspendedLine_Values CHECK(Quantity>0 AND UnitPrice>=0 AND LineDiscount>=0 AND CampaignDiscount>=0 AND LineTotal>=0),
  CONSTRAINT UQ_SuspendedLine_Number UNIQUE(SuspendedSaleId,LineNumber));
END;
GO

IF OBJECT_ID(N'ret.Campaign',N'U') IS NULL
BEGIN
 CREATE TABLE ret.Campaign(
  CampaignId bigint IDENTITY CONSTRAINT PK_Campaign PRIMARY KEY, CompanyId int NOT NULL, CampaignCode varchar(30) NOT NULL,
  CampaignName nvarchar(150) NOT NULL, CampaignType tinyint NOT NULL, StartAt datetime2(3) NOT NULL, EndAt datetime2(3) NOT NULL,
  Priority smallint NOT NULL DEFAULT(0), IsCombinable bit NOT NULL DEFAULT(0), IsActive bit NOT NULL DEFAULT(1),
  CreatedByUserId int NOT NULL, RowVersion rowversion,
  CONSTRAINT FK_Campaign_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_Campaign_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_Campaign_Type CHECK(CampaignType BETWEEN 1 AND 13), CONSTRAINT CK_Campaign_Dates CHECK(EndAt>StartAt),
  CONSTRAINT UQ_Campaign_Code UNIQUE(CompanyId,CampaignCode));
 CREATE TABLE ret.CampaignRule(
  CampaignRuleId bigint IDENTITY CONSTRAINT PK_CampaignRule PRIMARY KEY, CampaignId bigint NOT NULL, RuleOrder smallint NOT NULL,
  ConditionType tinyint NOT NULL, CategoryId int NULL, BrandId int NULL, StoreId int NULL,
  CustomerGroupCode varchar(30) NULL, MinimumQuantity decimal(19,6) NULL, MinimumBasketAmount decimal(19,4) NULL,
  ResultType tinyint NOT NULL, ResultValue decimal(19,4) NOT NULL, ApplyToCheapest bit NOT NULL DEFAULT(0),
  CONSTRAINT FK_CampaignRule_Campaign FOREIGN KEY(CampaignId) REFERENCES ret.Campaign(CampaignId),
  CONSTRAINT FK_CampaignRule_Category FOREIGN KEY(CategoryId) REFERENCES inv.Category(CategoryId),
  CONSTRAINT FK_CampaignRule_Brand FOREIGN KEY(BrandId) REFERENCES inv.Brand(BrandId),
  CONSTRAINT FK_CampaignRule_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
  CONSTRAINT CK_CampaignRule_Types CHECK(ConditionType BETWEEN 1 AND 10 AND ResultType BETWEEN 1 AND 4 AND ResultValue>=0),
  CONSTRAINT UQ_CampaignRule_Order UNIQUE(CampaignId,RuleOrder));
END;
GO

IF OBJECT_ID(N'inv.StockCount',N'U') IS NULL
BEGIN
 CREATE TABLE inv.StockCount(
  StockCountId bigint IDENTITY CONSTRAINT PK_StockCount PRIMARY KEY, CompanyId int NOT NULL, BranchId int NOT NULL,
  StoreId int NULL, WarehouseId int NOT NULL, CountNumber nvarchar(50) NOT NULL, CountType tinyint NOT NULL,
  CountDate date NOT NULL, StartedAt datetime2(3) NULL, FinishedAt datetime2(3) NULL, IsBlindCount bit NOT NULL DEFAULT(0),
  MovementPolicy tinyint NOT NULL DEFAULT(2), Status tinyint NOT NULL DEFAULT(1), ResponsibleUserId int NOT NULL,
  ApprovedByUserId int NULL, ApprovedAtUtc datetime2(3) NULL, RowVersion rowversion,
  CONSTRAINT FK_StockCount_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_StockCount_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
  CONSTRAINT FK_StockCount_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
  CONSTRAINT FK_StockCount_Warehouse FOREIGN KEY(WarehouseId) REFERENCES inv.Warehouse(WarehouseId),
  CONSTRAINT FK_StockCount_User FOREIGN KEY(ResponsibleUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT FK_StockCount_Approver FOREIGN KEY(ApprovedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_StockCount_Type CHECK(CountType BETWEEN 1 AND 9), CONSTRAINT CK_StockCount_Status CHECK(Status BETWEEN 1 AND 6),
  CONSTRAINT CK_StockCount_MovementPolicy CHECK(MovementPolicy IN(1,2)), CONSTRAINT UQ_StockCount_Number UNIQUE(CompanyId,CountNumber));
 CREATE TABLE inv.StockCountLine(
  StockCountLineId bigint IDENTITY CONSTRAINT PK_StockCountLine PRIMARY KEY, StockCountId bigint NOT NULL,
  ProductId bigint NOT NULL, ProductVariantId bigint NOT NULL, Barcode varchar(50) NULL, ShelfCode varchar(30) NULL,
  OpeningSystemQuantity decimal(19,6) NOT NULL, QuantityInDuringCount decimal(19,6) NOT NULL DEFAULT(0),
  QuantityOutDuringCount decimal(19,6) NOT NULL DEFAULT(0), ExpectedQuantity AS (OpeningSystemQuantity+QuantityInDuringCount-QuantityOutDuringCount) PERSISTED,
  FirstCount decimal(19,6) NULL, SecondCount decimal(19,6) NULL, FinalCount decimal(19,6) NULL,
  Difference AS (FinalCount-(OpeningSystemQuantity+QuantityInDuringCount-QuantityOutDuringCount)) PERSISTED,
  UnitCost decimal(19,6) NOT NULL, DifferenceReason nvarchar(300) NULL, RequiresSecondCount bit NOT NULL DEFAULT(0),
  CONSTRAINT FK_StockCountLine_Header FOREIGN KEY(StockCountId) REFERENCES inv.StockCount(StockCountId),
  CONSTRAINT FK_StockCountLine_Product FOREIGN KEY(ProductId) REFERENCES inv.Product(ProductId),
  CONSTRAINT FK_StockCountLine_Variant FOREIGN KEY(ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
  CONSTRAINT CK_StockCountLine_Values CHECK(UnitCost>=0 AND FirstCount>=0 AND SecondCount>=0 AND FinalCount>=0),
  CONSTRAINT UQ_StockCountLine_Variant UNIQUE(StockCountId,ProductVariantId));
END;
GO

IF OBJECT_ID(N'inv.WarehouseTransfer',N'U') IS NULL
BEGIN
 CREATE TABLE inv.WarehouseTransfer(
  WarehouseTransferId bigint IDENTITY CONSTRAINT PK_WarehouseTransfer PRIMARY KEY, CompanyId int NOT NULL,
  TransferNumber nvarchar(50) NOT NULL, RequestDate datetime2(3) NOT NULL, SourceWarehouseId int NOT NULL,
  DestinationWarehouseId int NOT NULL, Status tinyint NOT NULL DEFAULT(1), RequestedByUserId int NOT NULL,
  ApprovedByUserId int NULL, ShippedAt datetime2(3) NULL, ReceivedAt datetime2(3) NULL, Description nvarchar(500) NULL, RowVersion rowversion,
  CONSTRAINT FK_Transfer_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_Transfer_Source FOREIGN KEY(SourceWarehouseId) REFERENCES inv.Warehouse(WarehouseId),
  CONSTRAINT FK_Transfer_Destination FOREIGN KEY(DestinationWarehouseId) REFERENCES inv.Warehouse(WarehouseId),
  CONSTRAINT FK_Transfer_Requester FOREIGN KEY(RequestedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT FK_Transfer_Approver FOREIGN KEY(ApprovedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_Transfer_Status CHECK(Status BETWEEN 1 AND 7), CONSTRAINT CK_Transfer_Warehouses CHECK(SourceWarehouseId<>DestinationWarehouseId),
  CONSTRAINT UQ_Transfer_Number UNIQUE(CompanyId,TransferNumber));
 CREATE TABLE inv.WarehouseTransferLine(
  WarehouseTransferLineId bigint IDENTITY CONSTRAINT PK_TransferLine PRIMARY KEY, WarehouseTransferId bigint NOT NULL,
  LineNumber int NOT NULL, ProductId bigint NOT NULL, ProductVariantId bigint NOT NULL, RequestedQuantity decimal(19,6) NOT NULL,
  ShippedQuantity decimal(19,6) NOT NULL DEFAULT(0), InTransitQuantity decimal(19,6) NOT NULL DEFAULT(0),
  ReceivedQuantity decimal(19,6) NOT NULL DEFAULT(0), MissingQuantity AS (ShippedQuantity-ReceivedQuantity) PERSISTED,
  DifferenceNote nvarchar(300) NULL,
  CONSTRAINT FK_TransferLine_Header FOREIGN KEY(WarehouseTransferId) REFERENCES inv.WarehouseTransfer(WarehouseTransferId),
  CONSTRAINT FK_TransferLine_Product FOREIGN KEY(ProductId) REFERENCES inv.Product(ProductId),
  CONSTRAINT FK_TransferLine_Variant FOREIGN KEY(ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
  CONSTRAINT CK_TransferLine_Values CHECK(RequestedQuantity>0 AND ShippedQuantity>=0 AND InTransitQuantity>=0 AND ReceivedQuantity>=0 AND ReceivedQuantity<=ShippedQuantity),
  CONSTRAINT UQ_TransferLine_Number UNIQUE(WarehouseTransferId,LineNumber));
END;
GO

IF OBJECT_ID(N'doc.EDocument',N'U') IS NULL
BEGIN
 CREATE TABLE doc.EDocument(
  EDocumentId bigint IDENTITY CONSTRAINT PK_EDocument PRIMARY KEY, CompanyId int NOT NULL, DocumentType tinyint NOT NULL,
  InvoiceId bigint NULL, DispatchId bigint NULL, DocumentNumber nvarchar(50) NOT NULL, DocumentUuid uniqueidentifier NOT NULL,
  IntegratorName nvarchar(100) NULL, Status tinyint NOT NULL DEFAULT(1), LastError nvarchar(1000) NULL,
  CreatedAtUtc datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()), UpdatedAtUtc datetime2(3) NULL, RowVersion rowversion,
  CONSTRAINT FK_EDocument_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_EDocument_Invoice FOREIGN KEY(InvoiceId) REFERENCES doc.Invoice(InvoiceId),
  CONSTRAINT FK_EDocument_Dispatch FOREIGN KEY(DispatchId) REFERENCES doc.Dispatch(DispatchId),
  CONSTRAINT CK_EDocument_Type CHECK(DocumentType BETWEEN 1 AND 9), CONSTRAINT CK_EDocument_Status CHECK(Status BETWEEN 1 AND 10),
  CONSTRAINT CK_EDocument_Source CHECK(InvoiceId IS NOT NULL OR DispatchId IS NOT NULL), CONSTRAINT UQ_EDocument_UUID UNIQUE(DocumentUuid));
 CREATE TABLE doc.EDocumentStatusHistory(
  EDocumentStatusHistoryId bigint IDENTITY CONSTRAINT PK_EDocumentHistory PRIMARY KEY, EDocumentId bigint NOT NULL,
  PreviousStatus tinyint NULL, NewStatus tinyint NOT NULL, StatusDate datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()),
  ResponseCode varchar(30) NULL, ResponseMessage nvarchar(1000) NULL,
  CONSTRAINT FK_EDocumentHistory_Document FOREIGN KEY(EDocumentId) REFERENCES doc.EDocument(EDocumentId),
  CONSTRAINT CK_EDocumentHistory_Status CHECK(NewStatus BETWEEN 1 AND 10));
END;
GO

IF OBJECT_ID(N'acc.CostCenter',N'U') IS NULL
BEGIN
 CREATE TABLE acc.CostCenter(
  CostCenterId int IDENTITY CONSTRAINT PK_CostCenter PRIMARY KEY, CompanyId int NOT NULL, CostCenterCode varchar(30) NOT NULL,
  CostCenterName nvarchar(150) NOT NULL, ParentCostCenterId int NULL, IsActive bit NOT NULL DEFAULT(1),
  CONSTRAINT FK_CostCenter_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_CostCenter_Parent FOREIGN KEY(ParentCostCenterId) REFERENCES acc.CostCenter(CostCenterId),
  CONSTRAINT UQ_CostCenter_Code UNIQUE(CompanyId,CostCenterCode));
 CREATE TABLE acc.Expense(
  ExpenseId bigint IDENTITY CONSTRAINT PK_Expense PRIMARY KEY, CompanyId int NOT NULL, BranchId int NULL, StoreId int NULL,
  CostCenterId int NULL, ExpenseType tinyint NOT NULL, DocumentNumber nvarchar(50) NOT NULL, ExpenseDate date NOT NULL,
  AccountId bigint NULL, PersonnelUserId int NULL, CurrencyCode char(3) NOT NULL, Amount decimal(19,4) NOT NULL,
  VatAmount decimal(19,4) NOT NULL DEFAULT(0), Description nvarchar(500) NULL, Status tinyint NOT NULL DEFAULT(1),
  CreatedByUserId int NOT NULL, CreatedAtUtc datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()),
  CONSTRAINT FK_Expense_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_Expense_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
  CONSTRAINT FK_Expense_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
  CONSTRAINT FK_Expense_CostCenter FOREIGN KEY(CostCenterId) REFERENCES acc.CostCenter(CostCenterId),
  CONSTRAINT FK_Expense_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
  CONSTRAINT FK_Expense_Personnel FOREIGN KEY(PersonnelUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT FK_Expense_Currency FOREIGN KEY(CurrencyCode) REFERENCES core.Currency(CurrencyCode),
  CONSTRAINT FK_Expense_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_Expense_Type CHECK(ExpenseType BETWEEN 1 AND 15), CONSTRAINT CK_Expense_Status CHECK(Status BETWEEN 1 AND 4),
  CONSTRAINT CK_Expense_Amounts CHECK(Amount>0 AND VatAmount>=0));
END;
GO

IF OBJECT_ID(N'acc.AccountingConnection',N'U') IS NULL
BEGIN
 CREATE TABLE acc.AccountingConnection(
  AccountingConnectionId bigint IDENTITY CONSTRAINT PK_AccountingConnection PRIMARY KEY, CompanyId int NOT NULL,
  SourceModule varchar(30) NOT NULL, TransactionType varchar(40) NOT NULL, DebitAccountCode varchar(30) NOT NULL,
  CreditAccountCode varchar(30) NOT NULL, VatAccountCode varchar(30) NULL, CostAccountCode varchar(30) NULL,
  InventoryAccountCode varchar(30) NULL, IsActive bit NOT NULL DEFAULT(1),
  CONSTRAINT FK_AccountingConnection_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT UQ_AccountingConnection UNIQUE(CompanyId,SourceModule,TransactionType));
 CREATE TABLE acc.IntegrationPool(
  IntegrationPoolId bigint IDENTITY CONSTRAINT PK_IntegrationPool PRIMARY KEY, CompanyId int NOT NULL,
  SourceModule varchar(30) NOT NULL, SourceRecordId bigint NOT NULL, DocumentNumber nvarchar(50) NOT NULL,
  DocumentDate date NOT NULL, Status tinyint NOT NULL DEFAULT(1), VoucherNumber nvarchar(50) NULL,
  ErrorMessage nvarchar(1000) NULL, CreatedAtUtc datetime2(3) NOT NULL DEFAULT(SYSUTCDATETIME()),
  TransferredAtUtc datetime2(3) NULL, TransferredByUserId int NULL,
  CONSTRAINT FK_IntegrationPool_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
  CONSTRAINT FK_IntegrationPool_User FOREIGN KEY(TransferredByUserId) REFERENCES sec.AppUser(UserId),
  CONSTRAINT CK_IntegrationPool_Status CHECK(Status BETWEEN 1 AND 4),
  CONSTRAINT UQ_IntegrationPool_Source UNIQUE(CompanyId,SourceModule,SourceRecordId));
END;
GO

CREATE OR ALTER VIEW fin.vwPendingPosPayments AS
SELECT p.PosTransactionId,a.PosCode,a.PosName,b.BankName,p.TransactionDate,p.GrossAmount,p.CommissionAmount,
 p.NetAmount,p.InstallmentCount,p.ExpectedPaymentDate,p.ActualPaymentDate,p.Status
FROM fin.PosTransaction p JOIN fin.PosAccount a ON a.PosAccountId=p.PosAccountId
JOIN fin.BankAccount b ON b.BankAccountId=a.BankAccountId WHERE p.Status IN(1,2);
GO
