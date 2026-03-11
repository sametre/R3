/*
  R3 ERP - Exchange, dispatch, account, cash shift and bank workflow
  Run after 04_Retail_Variants_And_Trade_Workflow.sql.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE EngineeringERP;
GO

IF OBJECT_ID(N'doc.Exchange', N'U') IS NULL
BEGIN
    CREATE TABLE doc.Exchange
    (
        ExchangeId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Exchange PRIMARY KEY,
        CompanyId int NOT NULL, BranchId int NOT NULL, StoreId int NOT NULL,
        CashAccountId int NULL, SalesTerminalId int NULL, AccountId bigint NULL,
        ExchangeNumber nvarchar(50) NOT NULL, ExchangeDate datetime2(3) NOT NULL,
        SourceSalesInvoiceId bigint NOT NULL, ReturnInvoiceId bigint NOT NULL, NewSalesInvoiceId bigint NOT NULL,
        PriceDifference decimal(19,4) NOT NULL, CollectedAmount decimal(19,4) NOT NULL CONSTRAINT DF_Exchange_Collected DEFAULT(0),
        RefundedAmount decimal(19,4) NOT NULL CONSTRAINT DF_Exchange_Refunded DEFAULT(0),
        RefundMethod tinyint NULL, Reason nvarchar(300) NOT NULL, Status tinyint NOT NULL CONSTRAINT DF_Exchange_Status DEFAULT(1),
        PersonnelUserId int NOT NULL, CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Exchange_Created DEFAULT(SYSUTCDATETIME()),
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Exchange_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_Exchange_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_Exchange_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
        CONSTRAINT FK_Exchange_Cash FOREIGN KEY(CashAccountId) REFERENCES fin.CashAccount(CashAccountId),
        CONSTRAINT FK_Exchange_Terminal FOREIGN KEY(SalesTerminalId) REFERENCES core.SalesTerminal(SalesTerminalId),
        CONSTRAINT FK_Exchange_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
        CONSTRAINT FK_Exchange_SourceInvoice FOREIGN KEY(SourceSalesInvoiceId) REFERENCES doc.Invoice(InvoiceId),
        CONSTRAINT FK_Exchange_ReturnInvoice FOREIGN KEY(ReturnInvoiceId) REFERENCES doc.Invoice(InvoiceId),
        CONSTRAINT FK_Exchange_NewInvoice FOREIGN KEY(NewSalesInvoiceId) REFERENCES doc.Invoice(InvoiceId),
        CONSTRAINT FK_Exchange_Personnel FOREIGN KEY(PersonnelUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_Exchange_Amounts CHECK(CollectedAmount >= 0 AND RefundedAmount >= 0 AND NOT(CollectedAmount > 0 AND RefundedAmount > 0)),
        CONSTRAINT CK_Exchange_RefundMethod CHECK(RefundMethod IS NULL OR RefundMethod BETWEEN 1 AND 4),
        CONSTRAINT CK_Exchange_Status CHECK(Status BETWEEN 1 AND 3),
        CONSTRAINT UQ_Exchange_Number UNIQUE(CompanyId, ExchangeNumber)
    );
    CREATE INDEX IX_Exchange_SourceInvoice ON doc.Exchange(SourceSalesInvoiceId, ExchangeDate DESC);

    CREATE TABLE doc.ExchangeLine
    (
        ExchangeLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExchangeLine PRIMARY KEY,
        ExchangeId bigint NOT NULL, LineNumber int NOT NULL,
        ReturnInvoiceLineId bigint NULL, ReturnedProductId bigint NULL, ReturnedVariantId bigint NULL, ReturnedQuantity decimal(19,6) NOT NULL CONSTRAINT DF_ExchangeLine_ReturnQty DEFAULT(0),
        NewSalesInvoiceLineId bigint NULL, NewProductId bigint NULL, NewVariantId bigint NULL, NewQuantity decimal(19,6) NOT NULL CONSTRAINT DF_ExchangeLine_NewQty DEFAULT(0),
        ReturnAmount decimal(19,4) NOT NULL CONSTRAINT DF_ExchangeLine_ReturnAmount DEFAULT(0),
        NewSaleAmount decimal(19,4) NOT NULL CONSTRAINT DF_ExchangeLine_SaleAmount DEFAULT(0),
        CONSTRAINT FK_ExchangeLine_Header FOREIGN KEY(ExchangeId) REFERENCES doc.Exchange(ExchangeId),
        CONSTRAINT FK_ExchangeLine_ReturnLine FOREIGN KEY(ReturnInvoiceLineId) REFERENCES doc.InvoiceLine(InvoiceLineId),
        CONSTRAINT FK_ExchangeLine_ReturnProduct FOREIGN KEY(ReturnedProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_ExchangeLine_ReturnVariant FOREIGN KEY(ReturnedVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT FK_ExchangeLine_SaleLine FOREIGN KEY(NewSalesInvoiceLineId) REFERENCES doc.InvoiceLine(InvoiceLineId),
        CONSTRAINT FK_ExchangeLine_NewProduct FOREIGN KEY(NewProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_ExchangeLine_NewVariant FOREIGN KEY(NewVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT CK_ExchangeLine_Values CHECK(ReturnedQuantity >= 0 AND NewQuantity >= 0 AND ReturnAmount >= 0 AND NewSaleAmount >= 0 AND (ReturnedQuantity > 0 OR NewQuantity > 0)),
        CONSTRAINT UQ_ExchangeLine_Number UNIQUE(ExchangeId, LineNumber)
    );
END;
GO

IF OBJECT_ID(N'doc.Dispatch', N'U') IS NULL
BEGIN
    CREATE TABLE doc.Dispatch
    (
        DispatchId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Dispatch PRIMARY KEY,
        CompanyId int NOT NULL, BranchId int NOT NULL, StoreId int NULL, FiscalPeriodId int NOT NULL,
        DispatchType tinyint NOT NULL, DispatchNumber nvarchar(50) NOT NULL,
        ShipmentDate date NOT NULL, ShipmentTime time(0) NULL, AccountId bigint NULL,
        SourceWarehouseId int NOT NULL, DestinationWarehouseId int NULL,
        CarrierName nvarchar(150) NULL, VehiclePlate nvarchar(20) NULL, DriverName nvarchar(150) NULL,
        DeliveryAddress nvarchar(500) NULL, EDispatchUuid uniqueidentifier NULL,
        Status tinyint NOT NULL CONSTRAINT DF_Dispatch_Status DEFAULT(1), Description nvarchar(500) NULL,
        CreatedByUserId int NOT NULL, CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Dispatch_Created DEFAULT(SYSUTCDATETIME()),
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Dispatch_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_Dispatch_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_Dispatch_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
        CONSTRAINT FK_Dispatch_Period FOREIGN KEY(FiscalPeriodId) REFERENCES core.FiscalPeriod(FiscalPeriodId),
        CONSTRAINT FK_Dispatch_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
        CONSTRAINT FK_Dispatch_SourceWarehouse FOREIGN KEY(SourceWarehouseId) REFERENCES inv.Warehouse(WarehouseId),
        CONSTRAINT FK_Dispatch_DestinationWarehouse FOREIGN KEY(DestinationWarehouseId) REFERENCES inv.Warehouse(WarehouseId),
        CONSTRAINT FK_Dispatch_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_Dispatch_Type CHECK(DispatchType BETWEEN 1 AND 8),
        CONSTRAINT CK_Dispatch_Status CHECK(Status BETWEEN 1 AND 7),
        CONSTRAINT UQ_Dispatch_Number UNIQUE(CompanyId, DispatchType, DispatchNumber)
    );
    CREATE UNIQUE INDEX UX_Dispatch_UUID ON doc.Dispatch(EDispatchUuid) WHERE EDispatchUuid IS NOT NULL;

    CREATE TABLE doc.DispatchLine
    (
        DispatchLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DispatchLine PRIMARY KEY,
        DispatchId bigint NOT NULL, LineNumber int NOT NULL, ProductId bigint NOT NULL, ProductVariantId bigint NULL,
        Quantity decimal(19,6) NOT NULL, UnitId int NOT NULL, LotNumber nvarchar(100) NULL, SerialNumber nvarchar(100) NULL,
        Description nvarchar(300) NULL, InvoicedQuantity decimal(19,6) NOT NULL CONSTRAINT DF_DispatchLine_Invoiced DEFAULT(0),
        CONSTRAINT FK_DispatchLine_Header FOREIGN KEY(DispatchId) REFERENCES doc.Dispatch(DispatchId),
        CONSTRAINT FK_DispatchLine_Product FOREIGN KEY(ProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_DispatchLine_Variant FOREIGN KEY(ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT FK_DispatchLine_Unit FOREIGN KEY(UnitId) REFERENCES inv.Unit(UnitId),
        CONSTRAINT CK_DispatchLine_Quantity CHECK(Quantity > 0 AND InvoicedQuantity >= 0 AND InvoicedQuantity <= Quantity),
        CONSTRAINT UQ_DispatchLine_Number UNIQUE(DispatchId, LineNumber)
    );

    CREATE TABLE doc.DispatchInvoiceLine
    (
        DispatchInvoiceLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DispatchInvoiceLine PRIMARY KEY,
        DispatchLineId bigint NOT NULL, InvoiceLineId bigint NOT NULL, Quantity decimal(19,6) NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_DispatchInvoiceLine_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_DispatchInvoiceLine_Dispatch FOREIGN KEY(DispatchLineId) REFERENCES doc.DispatchLine(DispatchLineId),
        CONSTRAINT FK_DispatchInvoiceLine_Invoice FOREIGN KEY(InvoiceLineId) REFERENCES doc.InvoiceLine(InvoiceLineId),
        CONSTRAINT CK_DispatchInvoiceLine_Quantity CHECK(Quantity > 0),
        CONSTRAINT UQ_DispatchInvoiceLine UNIQUE(DispatchLineId, InvoiceLineId)
    );
END;
GO

CREATE OR ALTER TRIGGER doc.trg_DispatchInvoiceLine_Quantity
ON doc.DispatchInvoiceLine AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            SELECT DispatchLineId FROM inserted
            UNION
            SELECT DispatchLineId FROM deleted
        ) a
        JOIN doc.DispatchLine l ON l.DispatchLineId=a.DispatchLineId
        CROSS APPLY
        (
            SELECT COALESCE(SUM(x.Quantity),0) TotalQuantity
            FROM doc.DispatchInvoiceLine x WHERE x.DispatchLineId=a.DispatchLineId
        ) t
        WHERE t.TotalQuantity > l.Quantity
    )
        THROW 50030, N'İrsaliye satırında faturalaştırılan miktar sevk miktarını aşamaz.', 1;

    UPDATE l SET InvoicedQuantity=
        (SELECT COALESCE(SUM(x.Quantity),0) FROM doc.DispatchInvoiceLine x WHERE x.DispatchLineId=l.DispatchLineId)
    FROM doc.DispatchLine l
    WHERE l.DispatchLineId IN
    (
        SELECT DispatchLineId FROM inserted
        UNION
        SELECT DispatchLineId FROM deleted
    );
END;
GO

IF COL_LENGTH(N'crm.Account', N'MersisNumber') IS NULL ALTER TABLE crm.Account ADD MersisNumber varchar(20) NULL;
IF COL_LENGTH(N'crm.Account', N'PriceListCode') IS NULL ALTER TABLE crm.Account ADD PriceListCode varchar(30) NULL;
IF COL_LENGTH(N'crm.Account', N'DiscountGroupCode') IS NULL ALTER TABLE crm.Account ADD DiscountGroupCode varchar(30) NULL;
IF COL_LENGTH(N'crm.Account', N'SalesRepresentativeUserId') IS NULL ALTER TABLE crm.Account ADD SalesRepresentativeUserId int NULL;
IF COL_LENGTH(N'crm.Account', N'PaymentPlanId') IS NULL ALTER TABLE crm.Account ADD PaymentPlanId int NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Account_SalesRepresentative')
    ALTER TABLE crm.Account ADD CONSTRAINT FK_Account_SalesRepresentative FOREIGN KEY(SalesRepresentativeUserId) REFERENCES sec.AppUser(UserId);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Account_PaymentPlan')
    ALTER TABLE crm.Account ADD CONSTRAINT FK_Account_PaymentPlan FOREIGN KEY(PaymentPlanId) REFERENCES fin.PaymentPlan(PaymentPlanId);
GO

IF OBJECT_ID(N'crm.AccountTransaction', N'U') IS NULL
BEGIN
    CREATE TABLE crm.AccountTransaction
    (
        AccountTransactionId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountTransaction PRIMARY KEY,
        CompanyId int NOT NULL, BranchId int NOT NULL, FiscalPeriodId int NOT NULL, AccountId bigint NOT NULL,
        TransactionType tinyint NOT NULL, TransactionDate datetime2(3) NOT NULL, DueDate date NULL,
        DocumentType nvarchar(40) NOT NULL, DocumentNumber nvarchar(50) NOT NULL,
        InvoiceId bigint NULL, FinancialTransactionId bigint NULL,
        CurrencyCode char(3) NOT NULL, ExchangeRate decimal(19,8) NOT NULL CONSTRAINT DF_AccountTx_Rate DEFAULT(1),
        Debit decimal(19,4) NOT NULL CONSTRAINT DF_AccountTx_Debit DEFAULT(0),
        Credit decimal(19,4) NOT NULL CONSTRAINT DF_AccountTx_Credit DEFAULT(0),
        LocalDebit decimal(19,4) NOT NULL CONSTRAINT DF_AccountTx_LocalDebit DEFAULT(0),
        LocalCredit decimal(19,4) NOT NULL CONSTRAINT DF_AccountTx_LocalCredit DEFAULT(0),
        Description nvarchar(500) NULL, CreatedByUserId int NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_AccountTx_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_AccountTx_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_AccountTx_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_AccountTx_Period FOREIGN KEY(FiscalPeriodId) REFERENCES core.FiscalPeriod(FiscalPeriodId),
        CONSTRAINT FK_AccountTx_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
        CONSTRAINT FK_AccountTx_Invoice FOREIGN KEY(InvoiceId) REFERENCES doc.Invoice(InvoiceId),
        CONSTRAINT FK_AccountTx_Financial FOREIGN KEY(FinancialTransactionId) REFERENCES fin.FinancialTransaction(FinancialTransactionId),
        CONSTRAINT FK_AccountTx_Currency FOREIGN KEY(CurrencyCode) REFERENCES core.Currency(CurrencyCode),
        CONSTRAINT FK_AccountTx_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_AccountTx_Type CHECK(TransactionType BETWEEN 1 AND 13),
        CONSTRAINT CK_AccountTx_Amount CHECK(ExchangeRate > 0 AND ((Debit > 0 AND Credit=0) OR (Credit > 0 AND Debit=0)))
    );
    CREATE INDEX IX_AccountTx_Statement ON crm.AccountTransaction(AccountId, TransactionDate, AccountTransactionId) INCLUDE(Debit,Credit,DueDate,CurrencyCode);
END;
GO

IF OBJECT_ID(N'fin.CashShift', N'U') IS NULL
BEGIN
    CREATE TABLE fin.CashShift
    (
        CashShiftId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashShift PRIMARY KEY,
        CompanyId int NOT NULL, BranchId int NOT NULL, StoreId int NOT NULL, CashAccountId int NOT NULL,
        SalesTerminalId int NULL, CashierUserId int NOT NULL, ShiftNumber nvarchar(50) NOT NULL,
        OpenedAt datetime2(3) NOT NULL, OpeningBalance decimal(19,4) NOT NULL,
        ClosedAt datetime2(3) NULL, SystemCashBalance decimal(19,4) NULL, PhysicalCashBalance decimal(19,4) NULL,
        CashDifference AS (CASE WHEN PhysicalCashBalance IS NULL OR SystemCashBalance IS NULL THEN NULL ELSE PhysicalCashBalance-SystemCashBalance END),
        CardTotal decimal(19,4) NOT NULL CONSTRAINT DF_CashShift_Card DEFAULT(0),
        ReturnTotal decimal(19,4) NOT NULL CONSTRAINT DF_CashShift_Return DEFAULT(0),
        GiftVoucherTotal decimal(19,4) NOT NULL CONSTRAINT DF_CashShift_Gift DEFAULT(0),
        ExpenseTotal decimal(19,4) NOT NULL CONSTRAINT DF_CashShift_Expense DEFAULT(0),
        Status tinyint NOT NULL CONSTRAINT DF_CashShift_Status DEFAULT(1),
        DifferenceApprovedByUserId int NULL, DifferenceApprovalNote nvarchar(500) NULL, DifferenceApprovedAtUtc datetime2(3) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_CashShift_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_CashShift_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_CashShift_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
        CONSTRAINT FK_CashShift_Cash FOREIGN KEY(CashAccountId) REFERENCES fin.CashAccount(CashAccountId),
        CONSTRAINT FK_CashShift_Terminal FOREIGN KEY(SalesTerminalId) REFERENCES core.SalesTerminal(SalesTerminalId),
        CONSTRAINT FK_CashShift_Cashier FOREIGN KEY(CashierUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT FK_CashShift_Approver FOREIGN KEY(DifferenceApprovedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_CashShift_Status CHECK(Status BETWEEN 1 AND 3),
        CONSTRAINT CK_CashShift_Totals CHECK(OpeningBalance >= 0 AND CardTotal >= 0 AND ReturnTotal >= 0 AND GiftVoucherTotal >= 0 AND ExpenseTotal >= 0),
        CONSTRAINT UQ_CashShift_Number UNIQUE(CompanyId, ShiftNumber)
    );
END;
GO

IF OBJECT_ID(N'fin.CashMovement', N'U') IS NULL
BEGIN
    CREATE TABLE fin.CashMovement
    (
        CashMovementId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashMovement PRIMARY KEY,
        CompanyId int NOT NULL, BranchId int NOT NULL, StoreId int NULL, CashAccountId int NOT NULL, CashShiftId bigint NULL,
        MovementType tinyint NOT NULL, DocumentNumber nvarchar(50) NOT NULL, MovementDate datetime2(3) NOT NULL,
        AccountId bigint NULL, Direction tinyint NOT NULL, CurrencyCode char(3) NOT NULL, Amount decimal(19,4) NOT NULL,
        Description nvarchar(500) NULL, RequiresApproval bit NOT NULL CONSTRAINT DF_CashMovement_Approval DEFAULT(0),
        ApprovedByUserId int NULL, CreatedByUserId int NOT NULL, CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_CashMovement_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_CashMovement_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_CashMovement_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_CashMovement_Store FOREIGN KEY(StoreId) REFERENCES core.Store(StoreId),
        CONSTRAINT FK_CashMovement_Cash FOREIGN KEY(CashAccountId) REFERENCES fin.CashAccount(CashAccountId),
        CONSTRAINT FK_CashMovement_Shift FOREIGN KEY(CashShiftId) REFERENCES fin.CashShift(CashShiftId),
        CONSTRAINT FK_CashMovement_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
        CONSTRAINT FK_CashMovement_Currency FOREIGN KEY(CurrencyCode) REFERENCES core.Currency(CurrencyCode),
        CONSTRAINT FK_CashMovement_Approver FOREIGN KEY(ApprovedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT FK_CashMovement_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_CashMovement_Type CHECK(MovementType BETWEEN 1 AND 11),
        CONSTRAINT CK_CashMovement_Direction CHECK(Direction IN(1,2)),
        CONSTRAINT CK_CashMovement_Amount CHECK(Amount > 0)
    );
    CREATE INDEX IX_CashMovement_Ledger ON fin.CashMovement(CashAccountId, MovementDate, CashMovementId) INCLUDE(Direction,Amount,MovementType);
END;
GO

IF COL_LENGTH(N'fin.BankAccount', N'AccountName') IS NULL ALTER TABLE fin.BankAccount ADD AccountName nvarchar(150) NULL;
IF COL_LENGTH(N'fin.BankAccount', N'AccountingCode') IS NULL ALTER TABLE fin.BankAccount ADD AccountingCode varchar(30) NULL;
IF COL_LENGTH(N'fin.BankAccount', N'CreditLimit') IS NULL ALTER TABLE fin.BankAccount ADD CreditLimit decimal(19,4) NOT NULL CONSTRAINT DF_BankAccount_CreditLimit DEFAULT(0);
IF COL_LENGTH(N'fin.BankAccount', N'AvailableLimit') IS NULL ALTER TABLE fin.BankAccount ADD AvailableLimit decimal(19,4) NOT NULL CONSTRAINT DF_BankAccount_AvailableLimit DEFAULT(0);
GO

IF OBJECT_ID(N'fin.BankMovement', N'U') IS NULL
BEGIN
    CREATE TABLE fin.BankMovement
    (
        BankMovementId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankMovement PRIMARY KEY,
        CompanyId int NOT NULL, BranchId int NOT NULL, BankAccountId int NOT NULL, CounterBankAccountId int NULL,
        MovementType tinyint NOT NULL, DocumentNumber nvarchar(50) NOT NULL, MovementDate datetime2(3) NOT NULL,
        AccountId bigint NULL, Direction tinyint NOT NULL, CurrencyCode char(3) NOT NULL, ExchangeRate decimal(19,8) NOT NULL CONSTRAINT DF_BankMovement_Rate DEFAULT(1),
        Amount decimal(19,4) NOT NULL, LocalAmount decimal(19,4) NOT NULL, ReferenceNumber nvarchar(100) NULL,
        Description nvarchar(500) NULL, ExternalTransactionId nvarchar(100) NULL,
        CreatedByUserId int NOT NULL, CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_BankMovement_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_BankMovement_Company FOREIGN KEY(CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_BankMovement_Branch FOREIGN KEY(BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_BankMovement_Bank FOREIGN KEY(BankAccountId) REFERENCES fin.BankAccount(BankAccountId),
        CONSTRAINT FK_BankMovement_CounterBank FOREIGN KEY(CounterBankAccountId) REFERENCES fin.BankAccount(BankAccountId),
        CONSTRAINT FK_BankMovement_Account FOREIGN KEY(AccountId) REFERENCES crm.Account(AccountId),
        CONSTRAINT FK_BankMovement_Currency FOREIGN KEY(CurrencyCode) REFERENCES core.Currency(CurrencyCode),
        CONSTRAINT FK_BankMovement_User FOREIGN KEY(CreatedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_BankMovement_Type CHECK(MovementType BETWEEN 1 AND 14),
        CONSTRAINT CK_BankMovement_Direction CHECK(Direction IN(1,2)),
        CONSTRAINT CK_BankMovement_Amounts CHECK(ExchangeRate > 0 AND Amount > 0 AND LocalAmount > 0)
    );
    CREATE INDEX IX_BankMovement_Ledger ON fin.BankMovement(BankAccountId, MovementDate, BankMovementId) INCLUDE(Direction,Amount,LocalAmount,MovementType);
    CREATE UNIQUE INDEX UX_BankMovement_External ON fin.BankMovement(BankAccountId,ExternalTransactionId) WHERE ExternalTransactionId IS NOT NULL;
END;
GO

CREATE OR ALTER VIEW crm.vwAccountAging
AS
SELECT a.AccountId, a.CompanyId, a.AccountCode, a.LegalName, t.CurrencyCode,
    SUM(CASE WHEN t.DueDate IS NULL OR t.DueDate >= CAST(GETDATE() AS date) THEN t.Debit-t.Credit ELSE 0 END) AS NotDue,
    SUM(CASE WHEN DATEDIFF(day,t.DueDate,GETDATE()) BETWEEN 1 AND 30 THEN t.Debit-t.Credit ELSE 0 END) AS Days0To30,
    SUM(CASE WHEN DATEDIFF(day,t.DueDate,GETDATE()) BETWEEN 31 AND 60 THEN t.Debit-t.Credit ELSE 0 END) AS Days31To60,
    SUM(CASE WHEN DATEDIFF(day,t.DueDate,GETDATE()) BETWEEN 61 AND 90 THEN t.Debit-t.Credit ELSE 0 END) AS Days61To90,
    SUM(CASE WHEN DATEDIFF(day,t.DueDate,GETDATE()) > 90 THEN t.Debit-t.Credit ELSE 0 END) AS Days90Plus,
    SUM(t.Debit-t.Credit) AS Balance
FROM crm.Account a JOIN crm.AccountTransaction t ON t.AccountId=a.AccountId
GROUP BY a.AccountId,a.CompanyId,a.AccountCode,a.LegalName,t.CurrencyCode;
GO
