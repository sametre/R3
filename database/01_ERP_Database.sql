/*
  ERP Database - Initial Core Schema
  SQL Server 2019+
  Modules: Organization, Security, Current Accounts, Inventory,
           Sales/Purchase Invoices, Cash/Bank and Finance.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF DB_ID(N'EngineeringERP') IS NULL
BEGIN
    CREATE DATABASE EngineeringERP;
END;
GO

ALTER DATABASE EngineeringERP SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE EngineeringERP SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

USE EngineeringERP;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'core') EXEC(N'CREATE SCHEMA core');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'sec') EXEC(N'CREATE SCHEMA sec');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'crm') EXEC(N'CREATE SCHEMA crm');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'inv') EXEC(N'CREATE SCHEMA inv');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'doc') EXEC(N'CREATE SCHEMA doc');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'fin') EXEC(N'CREATE SCHEMA fin');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'audit') EXEC(N'CREATE SCHEMA audit');
GO

CREATE TABLE core.Company
(
    CompanyId           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Company PRIMARY KEY,
    CompanyCode         varchar(20) NOT NULL,
    LegalName           nvarchar(200) NOT NULL,
    TradeName           nvarchar(200) NULL,
    TaxOffice           nvarchar(100) NULL,
    TaxNumber           varchar(20) NULL,
    RegistrationNumber varchar(30) NULL,
    DefaultCurrencyCode char(3) NOT NULL CONSTRAINT DF_Company_Currency DEFAULT ('TRY'),
    Phone               varchar(30) NULL,
    Email               varchar(254) NULL,
    AddressText         nvarchar(500) NULL,
    IsActive            bit NOT NULL CONSTRAINT DF_Company_IsActive DEFAULT (1),
    CreatedAtUtc        datetime2(3) NOT NULL CONSTRAINT DF_Company_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc        datetime2(3) NULL,
    RowVersion          rowversion NOT NULL,
    CONSTRAINT UQ_Company_Code UNIQUE (CompanyCode)
);
GO

CREATE TABLE core.Branch
(
    BranchId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branch PRIMARY KEY,
    CompanyId      int NOT NULL,
    BranchCode     varchar(20) NOT NULL,
    BranchName     nvarchar(150) NOT NULL,
    Phone          varchar(30) NULL,
    Email          varchar(254) NULL,
    AddressText    nvarchar(500) NULL,
    IsHeadOffice   bit NOT NULL CONSTRAINT DF_Branch_Head DEFAULT (0),
    IsActive       bit NOT NULL CONSTRAINT DF_Branch_Active DEFAULT (1),
    CreatedAtUtc   datetime2(3) NOT NULL CONSTRAINT DF_Branch_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc   datetime2(3) NULL,
    RowVersion     rowversion NOT NULL,
    CONSTRAINT FK_Branch_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT UQ_Branch_CompanyCode UNIQUE (CompanyId, BranchCode)
);
GO

CREATE TABLE core.Currency
(
    CurrencyCode char(3) NOT NULL CONSTRAINT PK_Currency PRIMARY KEY,
    CurrencyName nvarchar(50) NOT NULL,
    Symbol       nvarchar(8) NULL,
    DecimalPlaces tinyint NOT NULL CONSTRAINT DF_Currency_Decimals DEFAULT (2),
    IsActive     bit NOT NULL CONSTRAINT DF_Currency_Active DEFAULT (1),
    CONSTRAINT CK_Currency_DecimalPlaces CHECK (DecimalPlaces BETWEEN 0 AND 6)
);
GO

CREATE TABLE core.ExchangeRate
(
    ExchangeRateId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExchangeRate PRIMARY KEY,
    CompanyId      int NOT NULL,
    RateDate       date NOT NULL,
    CurrencyCode   char(3) NOT NULL,
    RateType       tinyint NOT NULL, -- 1 Buying, 2 Selling, 3 Effective Buying, 4 Effective Selling
    RateValue      decimal(19,8) NOT NULL,
    CreatedAtUtc   datetime2(3) NOT NULL CONSTRAINT DF_ExchangeRate_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_ExchangeRate_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_ExchangeRate_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
    CONSTRAINT CK_ExchangeRate_Type CHECK (RateType BETWEEN 1 AND 4),
    CONSTRAINT CK_ExchangeRate_Value CHECK (RateValue > 0),
    CONSTRAINT UQ_ExchangeRate UNIQUE (CompanyId, RateDate, CurrencyCode, RateType)
);
GO

CREATE TABLE core.FiscalPeriod
(
    FiscalPeriodId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FiscalPeriod PRIMARY KEY,
    CompanyId      int NOT NULL,
    PeriodName     nvarchar(100) NOT NULL,
    StartDate      date NOT NULL,
    EndDate        date NOT NULL,
    IsClosed       bit NOT NULL CONSTRAINT DF_FiscalPeriod_Closed DEFAULT (0),
    ClosedAtUtc    datetime2(3) NULL,
    RowVersion     rowversion NOT NULL,
    CONSTRAINT FK_FiscalPeriod_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT CK_FiscalPeriod_Dates CHECK (EndDate >= StartDate),
    CONSTRAINT UQ_FiscalPeriod UNIQUE (CompanyId, StartDate, EndDate)
);
GO

CREATE TABLE sec.AppUser
(
    UserId             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppUser PRIMARY KEY,
    UserName           nvarchar(100) NOT NULL,
    NormalizedUserName nvarchar(100) NOT NULL,
    DisplayName        nvarchar(150) NOT NULL,
    Email              varchar(254) NULL,
    PasswordHash       nvarchar(500) NOT NULL,
    PasswordSalt       nvarchar(200) NULL,
    IsActive           bit NOT NULL CONSTRAINT DF_AppUser_Active DEFAULT (1),
    IsLocked           bit NOT NULL CONSTRAINT DF_AppUser_Locked DEFAULT (0),
    FailedLoginCount   smallint NOT NULL CONSTRAINT DF_AppUser_Failed DEFAULT (0),
    LastLoginAtUtc     datetime2(3) NULL,
    CreatedAtUtc       datetime2(3) NOT NULL CONSTRAINT DF_AppUser_CreatedAt DEFAULT (SYSUTCDATETIME()),
    RowVersion         rowversion NOT NULL,
    CONSTRAINT UQ_AppUser_Normalized UNIQUE (NormalizedUserName)
);
GO

CREATE TABLE sec.Role
(
    RoleId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Role PRIMARY KEY,
    RoleCode    varchar(50) NOT NULL,
    RoleName    nvarchar(100) NOT NULL,
    Description nvarchar(300) NULL,
    IsSystem    bit NOT NULL CONSTRAINT DF_Role_System DEFAULT (0),
    CONSTRAINT UQ_Role_Code UNIQUE (RoleCode)
);
GO

CREATE TABLE sec.UserCompany
(
    UserId         int NOT NULL,
    CompanyId      int NOT NULL,
    DefaultBranchId int NULL,
    IsDefault      bit NOT NULL CONSTRAINT DF_UserCompany_Default DEFAULT (0),
    CONSTRAINT PK_UserCompany PRIMARY KEY (UserId, CompanyId),
    CONSTRAINT FK_UserCompany_User FOREIGN KEY (UserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT FK_UserCompany_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_UserCompany_Branch FOREIGN KEY (DefaultBranchId) REFERENCES core.Branch(BranchId)
);
GO

CREATE TABLE sec.UserRole
(
    UserId    int NOT NULL,
    CompanyId int NOT NULL,
    RoleId    int NOT NULL,
    CONSTRAINT PK_UserRole PRIMARY KEY (UserId, CompanyId, RoleId),
    CONSTRAINT FK_UserRole_UserCompany FOREIGN KEY (UserId, CompanyId)
        REFERENCES sec.UserCompany(UserId, CompanyId),
    CONSTRAINT FK_UserRole_Role FOREIGN KEY (RoleId) REFERENCES sec.Role(RoleId)
);
GO

CREATE TABLE core.NumberSeries
(
    NumberSeriesId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NumberSeries PRIMARY KEY,
    CompanyId      int NOT NULL,
    BranchId       int NULL,
    DocumentType   tinyint NOT NULL, -- 1 Sales Invoice, 2 Purchase Invoice, 3 Collection, 4 Payment
    SeriesCode     varchar(20) NOT NULL,
    Prefix         nvarchar(20) NULL,
    NextNumber     bigint NOT NULL CONSTRAINT DF_NumberSeries_Next DEFAULT (1),
    NumberLength   tinyint NOT NULL CONSTRAINT DF_NumberSeries_Length DEFAULT (8),
    ResetYearly    bit NOT NULL CONSTRAINT DF_NumberSeries_Reset DEFAULT (1),
    LastResetYear  smallint NULL,
    RowVersion     rowversion NOT NULL,
    CONSTRAINT FK_NumberSeries_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_NumberSeries_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
    CONSTRAINT CK_NumberSeries_DocType CHECK (DocumentType BETWEEN 1 AND 10),
    CONSTRAINT CK_NumberSeries_Next CHECK (NextNumber > 0),
    CONSTRAINT CK_NumberSeries_Length CHECK (NumberLength BETWEEN 1 AND 18)
);
GO

CREATE UNIQUE INDEX UX_NumberSeries_Scope
ON core.NumberSeries(CompanyId, BranchId, DocumentType, SeriesCode)
WHERE BranchId IS NOT NULL;
GO

CREATE TABLE crm.Account
(
    AccountId        bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Account PRIMARY KEY,
    CompanyId        int NOT NULL,
    AccountCode      varchar(30) NOT NULL,
    AccountType      tinyint NOT NULL, -- 1 Customer, 2 Supplier, 3 Both, 4 Personnel, 5 Other
    LegalName        nvarchar(200) NOT NULL,
    TradeName        nvarchar(200) NULL,
    TaxOffice        nvarchar(100) NULL,
    TaxNumber        varchar(20) NULL,
    IdentityNumber   char(11) NULL,
    CurrencyCode     char(3) NOT NULL CONSTRAINT DF_Account_Currency DEFAULT ('TRY'),
    CreditLimit      decimal(19,4) NOT NULL CONSTRAINT DF_Account_Credit DEFAULT (0),
    PaymentTermDays  smallint NOT NULL CONSTRAINT DF_Account_Term DEFAULT (0),
    RiskStatus       tinyint NOT NULL CONSTRAINT DF_Account_Risk DEFAULT (1),
    Phone            varchar(30) NULL,
    Email            varchar(254) NULL,
    EInvoiceAlias    nvarchar(200) NULL,
    IsEInvoiceUser   bit NOT NULL CONSTRAINT DF_Account_EInvoice DEFAULT (0),
    Notes            nvarchar(1000) NULL,
    IsActive         bit NOT NULL CONSTRAINT DF_Account_Active DEFAULT (1),
    IsDeleted        bit NOT NULL CONSTRAINT DF_Account_Deleted DEFAULT (0),
    CreatedByUserId  int NOT NULL,
    CreatedAtUtc     datetime2(3) NOT NULL CONSTRAINT DF_Account_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedByUserId  int NULL,
    UpdatedAtUtc     datetime2(3) NULL,
    RowVersion       rowversion NOT NULL,
    CONSTRAINT FK_Account_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_Account_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
    CONSTRAINT FK_Account_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT FK_Account_UpdatedBy FOREIGN KEY (UpdatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_Account_Type CHECK (AccountType BETWEEN 1 AND 5),
    CONSTRAINT CK_Account_Credit CHECK (CreditLimit >= 0),
    CONSTRAINT CK_Account_Term CHECK (PaymentTermDays >= 0),
    CONSTRAINT CK_Account_Identity CHECK (IdentityNumber IS NULL OR IdentityNumber NOT LIKE '%[^0-9]%'),
    CONSTRAINT UQ_Account_Code UNIQUE (CompanyId, AccountCode)
);
GO

CREATE INDEX IX_Account_Search ON crm.Account(CompanyId, LegalName) INCLUDE(AccountCode, TradeName, TaxNumber, IsActive);
GO

CREATE TABLE crm.AccountAddress
(
    AccountAddressId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountAddress PRIMARY KEY,
    AccountId        bigint NOT NULL,
    AddressType      tinyint NOT NULL, -- 1 Invoice, 2 Delivery, 3 Other
    AddressName      nvarchar(100) NULL,
    CountryCode      char(2) NOT NULL CONSTRAINT DF_Address_Country DEFAULT ('TR'),
    City             nvarchar(100) NULL,
    District         nvarchar(100) NULL,
    PostalCode       varchar(15) NULL,
    AddressLine      nvarchar(500) NOT NULL,
    IsDefault        bit NOT NULL CONSTRAINT DF_Address_Default DEFAULT (0),
    CONSTRAINT FK_AccountAddress_Account FOREIGN KEY (AccountId) REFERENCES crm.Account(AccountId),
    CONSTRAINT CK_AccountAddress_Type CHECK (AddressType BETWEEN 1 AND 3)
);
GO

CREATE TABLE inv.Unit
(
    UnitId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Unit PRIMARY KEY,
    CompanyId    int NOT NULL,
    UnitCode     varchar(15) NOT NULL,
    UnitName     nvarchar(50) NOT NULL,
    DecimalPlaces tinyint NOT NULL CONSTRAINT DF_Unit_Decimals DEFAULT (2),
    IsActive     bit NOT NULL CONSTRAINT DF_Unit_Active DEFAULT (1),
    CONSTRAINT FK_Unit_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT CK_Unit_Decimals CHECK (DecimalPlaces BETWEEN 0 AND 6),
    CONSTRAINT UQ_Unit_Code UNIQUE (CompanyId, UnitCode)
);
GO

CREATE TABLE inv.Warehouse
(
    WarehouseId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouse PRIMARY KEY,
    CompanyId     int NOT NULL,
    BranchId      int NOT NULL,
    WarehouseCode varchar(20) NOT NULL,
    WarehouseName nvarchar(100) NOT NULL,
    AddressText   nvarchar(500) NULL,
    AllowNegativeStock bit NOT NULL CONSTRAINT DF_Warehouse_Negative DEFAULT (0),
    IsActive      bit NOT NULL CONSTRAINT DF_Warehouse_Active DEFAULT (1),
    RowVersion    rowversion NOT NULL,
    CONSTRAINT FK_Warehouse_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_Warehouse_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
    CONSTRAINT UQ_Warehouse_Code UNIQUE (CompanyId, WarehouseCode)
);
GO

CREATE TABLE inv.Product
(
    ProductId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Product PRIMARY KEY,
    CompanyId       int NOT NULL,
    ProductCode     varchar(40) NOT NULL,
    Barcode         varchar(50) NULL,
    ProductName     nvarchar(200) NOT NULL,
    ProductType     tinyint NOT NULL, -- 1 Stock, 2 Service, 3 Raw Material, 4 Semi Finished, 5 Finished
    BaseUnitId      int NOT NULL,
    VatRate         decimal(5,2) NOT NULL CONSTRAINT DF_Product_Vat DEFAULT (20),
    PurchasePrice   decimal(19,4) NOT NULL CONSTRAINT DF_Product_Purchase DEFAULT (0),
    SalesPrice      decimal(19,4) NOT NULL CONSTRAINT DF_Product_Sales DEFAULT (0),
    CurrencyCode    char(3) NOT NULL CONSTRAINT DF_Product_Currency DEFAULT ('TRY'),
    MinStockLevel   decimal(19,6) NOT NULL CONSTRAINT DF_Product_MinStock DEFAULT (0),
    MaxStockLevel   decimal(19,6) NULL,
    TrackLot        bit NOT NULL CONSTRAINT DF_Product_Lot DEFAULT (0),
    TrackSerial     bit NOT NULL CONSTRAINT DF_Product_Serial DEFAULT (0),
    IsActive        bit NOT NULL CONSTRAINT DF_Product_Active DEFAULT (1),
    IsDeleted       bit NOT NULL CONSTRAINT DF_Product_Deleted DEFAULT (0),
    CreatedByUserId int NOT NULL,
    CreatedAtUtc    datetime2(3) NOT NULL CONSTRAINT DF_Product_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedByUserId int NULL,
    UpdatedAtUtc    datetime2(3) NULL,
    RowVersion      rowversion NOT NULL,
    CONSTRAINT FK_Product_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_Product_Unit FOREIGN KEY (BaseUnitId) REFERENCES inv.Unit(UnitId),
    CONSTRAINT FK_Product_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
    CONSTRAINT FK_Product_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT FK_Product_UpdatedBy FOREIGN KEY (UpdatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_Product_Type CHECK (ProductType BETWEEN 1 AND 5),
    CONSTRAINT CK_Product_Vat CHECK (VatRate BETWEEN 0 AND 100),
    CONSTRAINT CK_Product_Prices CHECK (PurchasePrice >= 0 AND SalesPrice >= 0),
    CONSTRAINT CK_Product_StockLevels CHECK (MinStockLevel >= 0 AND (MaxStockLevel IS NULL OR MaxStockLevel >= MinStockLevel)),
    CONSTRAINT UQ_Product_Code UNIQUE (CompanyId, ProductCode)
);
GO

CREATE UNIQUE INDEX UX_Product_Barcode ON inv.Product(CompanyId, Barcode)
WHERE Barcode IS NOT NULL AND IsDeleted = 0;
CREATE INDEX IX_Product_Search ON inv.Product(CompanyId, ProductName) INCLUDE(ProductCode, Barcode, SalesPrice, VatRate, IsActive);
GO

CREATE TABLE inv.ProductUnit
(
    ProductUnitId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductUnit PRIMARY KEY,
    ProductId     bigint NOT NULL,
    UnitId        int NOT NULL,
    ConversionFactor decimal(19,8) NOT NULL,
    Barcode       varchar(50) NULL,
    IsSalesUnit   bit NOT NULL CONSTRAINT DF_ProductUnit_Sales DEFAULT (0),
    IsPurchaseUnit bit NOT NULL CONSTRAINT DF_ProductUnit_Purchase DEFAULT (0),
    CONSTRAINT FK_ProductUnit_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
    CONSTRAINT FK_ProductUnit_Unit FOREIGN KEY (UnitId) REFERENCES inv.Unit(UnitId),
    CONSTRAINT CK_ProductUnit_Factor CHECK (ConversionFactor > 0),
    CONSTRAINT UQ_ProductUnit UNIQUE (ProductId, UnitId)
);
GO

CREATE TABLE doc.Invoice
(
    InvoiceId         bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Invoice PRIMARY KEY,
    CompanyId         int NOT NULL,
    BranchId          int NOT NULL,
    FiscalPeriodId    int NOT NULL,
    InvoiceType       tinyint NOT NULL, -- 1 Sales, 2 Sales Return, 3 Purchase, 4 Purchase Return
    InvoiceStatus     tinyint NOT NULL CONSTRAINT DF_Invoice_Status DEFAULT (1), -- 1 Draft, 2 Approved, 3 Posted, 4 Cancelled
    InvoiceNumber     nvarchar(50) NOT NULL,
    DocumentNumber    nvarchar(50) NULL,
    InvoiceDate       date NOT NULL,
    DueDate           date NOT NULL,
    AccountId         bigint NOT NULL,
    CurrencyCode      char(3) NOT NULL,
    ExchangeRate      decimal(19,8) NOT NULL CONSTRAINT DF_Invoice_Rate DEFAULT (1),
    BillingAddress    nvarchar(500) NULL,
    ShippingAddress   nvarchar(500) NULL,
    Subtotal          decimal(19,4) NOT NULL CONSTRAINT DF_Invoice_Subtotal DEFAULT (0),
    DiscountTotal     decimal(19,4) NOT NULL CONSTRAINT DF_Invoice_Discount DEFAULT (0),
    TaxTotal          decimal(19,4) NOT NULL CONSTRAINT DF_Invoice_Tax DEFAULT (0),
    GrandTotal        decimal(19,4) NOT NULL CONSTRAINT DF_Invoice_Grand DEFAULT (0),
    LocalGrandTotal   decimal(19,4) NOT NULL CONSTRAINT DF_Invoice_LocalGrand DEFAULT (0),
    PaidTotal         decimal(19,4) NOT NULL CONSTRAINT DF_Invoice_Paid DEFAULT (0),
    Description       nvarchar(500) NULL,
    ExternalReference nvarchar(100) NULL,
    PostedAtUtc       datetime2(3) NULL,
    PostedByUserId    int NULL,
    CreatedByUserId   int NOT NULL,
    CreatedAtUtc      datetime2(3) NOT NULL CONSTRAINT DF_Invoice_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedByUserId   int NULL,
    UpdatedAtUtc      datetime2(3) NULL,
    RowVersion        rowversion NOT NULL,
    CONSTRAINT FK_Invoice_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_Invoice_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
    CONSTRAINT FK_Invoice_Period FOREIGN KEY (FiscalPeriodId) REFERENCES core.FiscalPeriod(FiscalPeriodId),
    CONSTRAINT FK_Invoice_Account FOREIGN KEY (AccountId) REFERENCES crm.Account(AccountId),
    CONSTRAINT FK_Invoice_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
    CONSTRAINT FK_Invoice_PostedBy FOREIGN KEY (PostedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT FK_Invoice_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT FK_Invoice_UpdatedBy FOREIGN KEY (UpdatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_Invoice_Type CHECK (InvoiceType BETWEEN 1 AND 4),
    CONSTRAINT CK_Invoice_Status CHECK (InvoiceStatus BETWEEN 1 AND 4),
    CONSTRAINT CK_Invoice_Rate CHECK (ExchangeRate > 0),
    CONSTRAINT CK_Invoice_Dates CHECK (DueDate >= InvoiceDate),
    CONSTRAINT CK_Invoice_Totals CHECK
      (Subtotal >= 0 AND DiscountTotal >= 0 AND TaxTotal >= 0 AND GrandTotal >= 0 AND LocalGrandTotal >= 0 AND PaidTotal >= 0),
    CONSTRAINT UQ_Invoice_Number UNIQUE (CompanyId, InvoiceType, InvoiceNumber)
);
GO

CREATE INDEX IX_Invoice_List ON doc.Invoice(CompanyId, InvoiceDate DESC, InvoiceStatus)
INCLUDE(InvoiceNumber, InvoiceType, AccountId, GrandTotal, CurrencyCode);
CREATE INDEX IX_Invoice_Account ON doc.Invoice(AccountId, InvoiceDate DESC)
INCLUDE(InvoiceNumber, InvoiceStatus, GrandTotal, PaidTotal);
GO

CREATE TABLE doc.InvoiceLine
(
    InvoiceLineId    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceLine PRIMARY KEY,
    InvoiceId        bigint NOT NULL,
    LineNumber       int NOT NULL,
    ProductId        bigint NOT NULL,
    WarehouseId      int NULL,
    UnitId           int NOT NULL,
    Description      nvarchar(300) NULL,
    Quantity         decimal(19,6) NOT NULL,
    UnitFactor       decimal(19,8) NOT NULL CONSTRAINT DF_InvoiceLine_Factor DEFAULT (1),
    BaseQuantity     AS (Quantity * UnitFactor) PERSISTED,
    UnitPrice        decimal(19,6) NOT NULL,
    DiscountRate     decimal(9,6) NOT NULL CONSTRAINT DF_InvoiceLine_DiscountRate DEFAULT (0),
    DiscountAmount   decimal(19,4) NOT NULL CONSTRAINT DF_InvoiceLine_Discount DEFAULT (0),
    TaxRate          decimal(5,2) NOT NULL,
    TaxAmount        decimal(19,4) NOT NULL CONSTRAINT DF_InvoiceLine_Tax DEFAULT (0),
    LineNet          decimal(19,4) NOT NULL,
    LineTotal        decimal(19,4) NOT NULL,
    LotNumber        nvarchar(50) NULL,
    SerialNumber     nvarchar(100) NULL,
    CONSTRAINT FK_InvoiceLine_Invoice FOREIGN KEY (InvoiceId) REFERENCES doc.Invoice(InvoiceId),
    CONSTRAINT FK_InvoiceLine_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
    CONSTRAINT FK_InvoiceLine_Warehouse FOREIGN KEY (WarehouseId) REFERENCES inv.Warehouse(WarehouseId),
    CONSTRAINT FK_InvoiceLine_Unit FOREIGN KEY (UnitId) REFERENCES inv.Unit(UnitId),
    CONSTRAINT CK_InvoiceLine_Values CHECK
      (LineNumber > 0 AND Quantity > 0 AND UnitFactor > 0 AND UnitPrice >= 0
       AND DiscountRate BETWEEN 0 AND 100 AND DiscountAmount >= 0
       AND TaxRate BETWEEN 0 AND 100 AND TaxAmount >= 0 AND LineNet >= 0 AND LineTotal >= 0),
    CONSTRAINT UQ_InvoiceLine_Number UNIQUE (InvoiceId, LineNumber)
);
GO

CREATE INDEX IX_InvoiceLine_Product ON doc.InvoiceLine(ProductId, InvoiceId);
GO

CREATE TABLE inv.StockTransaction
(
    StockTransactionId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockTransaction PRIMARY KEY,
    CompanyId          int NOT NULL,
    BranchId           int NOT NULL,
    WarehouseId        int NOT NULL,
    ProductId          bigint NOT NULL,
    TransactionDate    datetime2(3) NOT NULL,
    TransactionType    tinyint NOT NULL, -- 1 In, 2 Out, 3 Transfer In, 4 Transfer Out, 5 Count +, 6 Count -
    QuantityIn         decimal(19,6) NOT NULL CONSTRAINT DF_StockTx_In DEFAULT (0),
    QuantityOut        decimal(19,6) NOT NULL CONSTRAINT DF_StockTx_Out DEFAULT (0),
    UnitCost           decimal(19,6) NOT NULL CONSTRAINT DF_StockTx_Cost DEFAULT (0),
    LocalAmount        decimal(19,4) NOT NULL CONSTRAINT DF_StockTx_Amount DEFAULT (0),
    SourceType         tinyint NOT NULL, -- 1 Invoice, 2 Dispatch, 3 Transfer, 4 Count, 5 Manual
    SourceId           bigint NOT NULL,
    SourceLineId       bigint NULL,
    LotNumber          nvarchar(50) NULL,
    SerialNumber       nvarchar(100) NULL,
    Description        nvarchar(300) NULL,
    CreatedByUserId    int NOT NULL,
    CreatedAtUtc       datetime2(3) NOT NULL CONSTRAINT DF_StockTx_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_StockTx_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_StockTx_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
    CONSTRAINT FK_StockTx_Warehouse FOREIGN KEY (WarehouseId) REFERENCES inv.Warehouse(WarehouseId),
    CONSTRAINT FK_StockTx_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
    CONSTRAINT FK_StockTx_User FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_StockTx_Type CHECK (TransactionType BETWEEN 1 AND 6),
    CONSTRAINT CK_StockTx_Source CHECK (SourceType BETWEEN 1 AND 5),
    CONSTRAINT CK_StockTx_Quantity CHECK
      ((QuantityIn > 0 AND QuantityOut = 0) OR (QuantityOut > 0 AND QuantityIn = 0)),
    CONSTRAINT CK_StockTx_Amounts CHECK (UnitCost >= 0 AND LocalAmount >= 0)
);
GO

CREATE UNIQUE INDEX UX_StockTransaction_Source
ON inv.StockTransaction(SourceType, SourceLineId, TransactionType)
WHERE SourceLineId IS NOT NULL;
CREATE INDEX IX_StockTransaction_Balance
ON inv.StockTransaction(CompanyId, WarehouseId, ProductId, TransactionDate)
INCLUDE(QuantityIn, QuantityOut, UnitCost, LocalAmount);
GO

CREATE TABLE inv.StockBalance
(
    CompanyId       int NOT NULL,
    WarehouseId     int NOT NULL,
    ProductId       bigint NOT NULL,
    QuantityOnHand  decimal(19,6) NOT NULL CONSTRAINT DF_StockBalance_OnHand DEFAULT (0),
    ReservedQuantity decimal(19,6) NOT NULL CONSTRAINT DF_StockBalance_Reserved DEFAULT (0),
    AvailableQuantity AS (QuantityOnHand - ReservedQuantity) PERSISTED,
    AverageUnitCost decimal(19,6) NOT NULL CONSTRAINT DF_StockBalance_Avg DEFAULT (0),
    LastMovementAtUtc datetime2(3) NULL,
    RowVersion      rowversion NOT NULL,
    CONSTRAINT PK_StockBalance PRIMARY KEY (CompanyId, WarehouseId, ProductId),
    CONSTRAINT FK_StockBalance_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_StockBalance_Warehouse FOREIGN KEY (WarehouseId) REFERENCES inv.Warehouse(WarehouseId),
    CONSTRAINT FK_StockBalance_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
    CONSTRAINT CK_StockBalance_Reserved CHECK (ReservedQuantity >= 0),
    CONSTRAINT CK_StockBalance_Cost CHECK (AverageUnitCost >= 0)
);
GO

CREATE TABLE fin.CashAccount
(
    CashAccountId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashAccount PRIMARY KEY,
    CompanyId     int NOT NULL,
    BranchId      int NOT NULL,
    CashCode      varchar(20) NOT NULL,
    CashName      nvarchar(100) NOT NULL,
    CurrencyCode  char(3) NOT NULL,
    IsActive      bit NOT NULL CONSTRAINT DF_CashAccount_Active DEFAULT (1),
    RowVersion    rowversion NOT NULL,
    CONSTRAINT FK_CashAccount_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_CashAccount_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
    CONSTRAINT FK_CashAccount_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
    CONSTRAINT UQ_CashAccount_Code UNIQUE (CompanyId, CashCode)
);
GO

CREATE TABLE fin.BankAccount
(
    BankAccountId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankAccount PRIMARY KEY,
    CompanyId     int NOT NULL,
    BranchId      int NOT NULL,
    AccountCode   varchar(20) NOT NULL,
    BankName      nvarchar(150) NOT NULL,
    BankBranchName nvarchar(150) NULL,
    Iban           varchar(34) NULL,
    AccountNumber  varchar(50) NULL,
    CurrencyCode   char(3) NOT NULL,
    IsActive       bit NOT NULL CONSTRAINT DF_BankAccount_Active DEFAULT (1),
    RowVersion     rowversion NOT NULL,
    CONSTRAINT FK_BankAccount_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_BankAccount_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
    CONSTRAINT FK_BankAccount_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
    CONSTRAINT UQ_BankAccount_Code UNIQUE (CompanyId, AccountCode)
);
GO

CREATE UNIQUE INDEX UX_BankAccount_Iban ON fin.BankAccount(CompanyId, Iban) WHERE Iban IS NOT NULL;
GO

CREATE TABLE fin.FinancialTransaction
(
    FinancialTransactionId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_FinancialTransaction PRIMARY KEY,
    CompanyId       int NOT NULL,
    BranchId        int NOT NULL,
    FiscalPeriodId  int NOT NULL,
    TransactionType tinyint NOT NULL, -- 1 Collection, 2 Payment, 3 Cash In, 4 Cash Out, 5 Bank In, 6 Bank Out
    Status          tinyint NOT NULL CONSTRAINT DF_FinTx_Status DEFAULT (1), -- 1 Draft, 2 Posted, 3 Cancelled
    DocumentNumber  nvarchar(50) NOT NULL,
    TransactionDate datetime2(3) NOT NULL,
    AccountId       bigint NULL,
    CashAccountId   int NULL,
    BankAccountId   int NULL,
    CurrencyCode    char(3) NOT NULL,
    ExchangeRate    decimal(19,8) NOT NULL CONSTRAINT DF_FinTx_Rate DEFAULT (1),
    Amount          decimal(19,4) NOT NULL,
    LocalAmount     decimal(19,4) NOT NULL,
    Description     nvarchar(500) NULL,
    ReferenceNumber nvarchar(100) NULL,
    PostedAtUtc     datetime2(3) NULL,
    PostedByUserId  int NULL,
    CreatedByUserId int NOT NULL,
    CreatedAtUtc    datetime2(3) NOT NULL CONSTRAINT DF_FinTx_CreatedAt DEFAULT (SYSUTCDATETIME()),
    RowVersion      rowversion NOT NULL,
    CONSTRAINT FK_FinTx_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_FinTx_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
    CONSTRAINT FK_FinTx_Period FOREIGN KEY (FiscalPeriodId) REFERENCES core.FiscalPeriod(FiscalPeriodId),
    CONSTRAINT FK_FinTx_Account FOREIGN KEY (AccountId) REFERENCES crm.Account(AccountId),
    CONSTRAINT FK_FinTx_Cash FOREIGN KEY (CashAccountId) REFERENCES fin.CashAccount(CashAccountId),
    CONSTRAINT FK_FinTx_Bank FOREIGN KEY (BankAccountId) REFERENCES fin.BankAccount(BankAccountId),
    CONSTRAINT FK_FinTx_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
    CONSTRAINT FK_FinTx_PostedBy FOREIGN KEY (PostedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT FK_FinTx_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_FinTx_Type CHECK (TransactionType BETWEEN 1 AND 6),
    CONSTRAINT CK_FinTx_Status CHECK (Status BETWEEN 1 AND 3),
    CONSTRAINT CK_FinTx_Amount CHECK (Amount > 0 AND LocalAmount > 0 AND ExchangeRate > 0),
    CONSTRAINT CK_FinTx_Target CHECK
      ((CASE WHEN CashAccountId IS NULL THEN 0 ELSE 1 END) +
       (CASE WHEN BankAccountId IS NULL THEN 0 ELSE 1 END) = 1),
    CONSTRAINT UQ_FinTx_Number UNIQUE (CompanyId, TransactionType, DocumentNumber)
);
GO

CREATE INDEX IX_FinTx_Account ON fin.FinancialTransaction(AccountId, TransactionDate DESC)
INCLUDE(TransactionType, Status, DocumentNumber, Amount, CurrencyCode);
GO

CREATE TABLE fin.AccountLedger
(
    AccountLedgerId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountLedger PRIMARY KEY,
    CompanyId       int NOT NULL,
    BranchId        int NOT NULL,
    AccountId       bigint NOT NULL,
    TransactionDate datetime2(3) NOT NULL,
    DueDate          date NULL,
    Debit            decimal(19,4) NOT NULL CONSTRAINT DF_AccountLedger_Debit DEFAULT (0),
    Credit           decimal(19,4) NOT NULL CONSTRAINT DF_AccountLedger_Credit DEFAULT (0),
    CurrencyCode     char(3) NOT NULL,
    ForeignDebit     decimal(19,4) NOT NULL CONSTRAINT DF_AccountLedger_FDebit DEFAULT (0),
    ForeignCredit    decimal(19,4) NOT NULL CONSTRAINT DF_AccountLedger_FCredit DEFAULT (0),
    ExchangeRate     decimal(19,8) NOT NULL CONSTRAINT DF_AccountLedger_Rate DEFAULT (1),
    SourceType       tinyint NOT NULL, -- 1 Invoice, 2 Financial Transaction, 3 Opening, 4 Manual
    SourceId         bigint NOT NULL,
    DocumentNumber   nvarchar(50) NOT NULL,
    Description      nvarchar(500) NULL,
    CreatedAtUtc     datetime2(3) NOT NULL CONSTRAINT DF_AccountLedger_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_AccountLedger_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_AccountLedger_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
    CONSTRAINT FK_AccountLedger_Account FOREIGN KEY (AccountId) REFERENCES crm.Account(AccountId),
    CONSTRAINT FK_AccountLedger_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
    CONSTRAINT CK_AccountLedger_Source CHECK (SourceType BETWEEN 1 AND 4),
    CONSTRAINT CK_AccountLedger_Amount CHECK
      ((Debit > 0 AND Credit = 0) OR (Credit > 0 AND Debit = 0)),
    CONSTRAINT CK_AccountLedger_Foreign CHECK
      ((ForeignDebit >= 0 AND ForeignCredit = 0) OR (ForeignCredit >= 0 AND ForeignDebit = 0)),
    CONSTRAINT CK_AccountLedger_Rate CHECK (ExchangeRate > 0)
);
GO

CREATE UNIQUE INDEX UX_AccountLedger_Source ON fin.AccountLedger(SourceType, SourceId, AccountId);
CREATE INDEX IX_AccountLedger_Balance
ON fin.AccountLedger(CompanyId, AccountId, TransactionDate, AccountLedgerId)
INCLUDE(Debit, Credit, ForeignDebit, ForeignCredit, CurrencyCode, DocumentNumber);
GO

CREATE TABLE fin.PaymentAllocation
(
    PaymentAllocationId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentAllocation PRIMARY KEY,
    FinancialTransactionId bigint NOT NULL,
    InvoiceId            bigint NOT NULL,
    AllocatedAmount      decimal(19,4) NOT NULL,
    CreatedByUserId      int NOT NULL,
    CreatedAtUtc         datetime2(3) NOT NULL CONSTRAINT DF_PaymentAllocation_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_PaymentAllocation_FinTx FOREIGN KEY (FinancialTransactionId)
        REFERENCES fin.FinancialTransaction(FinancialTransactionId),
    CONSTRAINT FK_PaymentAllocation_Invoice FOREIGN KEY (InvoiceId) REFERENCES doc.Invoice(InvoiceId),
    CONSTRAINT FK_PaymentAllocation_User FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_PaymentAllocation_Amount CHECK (AllocatedAmount > 0),
    CONSTRAINT UQ_PaymentAllocation UNIQUE (FinancialTransactionId, InvoiceId)
);
GO

CREATE TABLE audit.AuditLog
(
    AuditLogId     bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    CompanyId      int NULL,
    UserId         int NULL,
    EventTimeUtc   datetime2(3) NOT NULL CONSTRAINT DF_AuditLog_Time DEFAULT (SYSUTCDATETIME()),
    ActionType     varchar(30) NOT NULL,
    SchemaName     sysname NOT NULL,
    TableName      sysname NOT NULL,
    RecordKey      nvarchar(200) NULL,
    OldValuesJson  nvarchar(max) NULL,
    NewValuesJson  nvarchar(max) NULL,
    IpAddress      varchar(45) NULL,
    MachineName    nvarchar(128) NULL,
    CorrelationId  uniqueidentifier NULL,
    CONSTRAINT FK_AuditLog_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
    CONSTRAINT FK_AuditLog_User FOREIGN KEY (UserId) REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_AuditLog_OldJson CHECK (OldValuesJson IS NULL OR ISJSON(OldValuesJson) = 1),
    CONSTRAINT CK_AuditLog_NewJson CHECK (NewValuesJson IS NULL OR ISJSON(NewValuesJson) = 1)
);
GO

CREATE INDEX IX_AuditLog_Record ON audit.AuditLog(SchemaName, TableName, RecordKey, EventTimeUtc DESC);
CREATE INDEX IX_AuditLog_UserTime ON audit.AuditLog(UserId, EventTimeUtc DESC);
GO

CREATE OR ALTER VIEW inv.vwStockStatus
AS
SELECT
    sb.CompanyId,
    sb.WarehouseId,
    w.WarehouseCode,
    w.WarehouseName,
    sb.ProductId,
    p.ProductCode,
    p.ProductName,
    u.UnitCode,
    sb.QuantityOnHand,
    sb.ReservedQuantity,
    sb.AvailableQuantity,
    sb.AverageUnitCost,
    CAST(sb.QuantityOnHand * sb.AverageUnitCost AS decimal(19,4)) AS StockValue,
    sb.LastMovementAtUtc
FROM inv.StockBalance sb
JOIN inv.Warehouse w ON w.WarehouseId = sb.WarehouseId
JOIN inv.Product p ON p.ProductId = sb.ProductId
JOIN inv.Unit u ON u.UnitId = p.BaseUnitId;
GO

CREATE OR ALTER VIEW fin.vwAccountBalance
AS
SELECT
    al.CompanyId,
    al.AccountId,
    a.AccountCode,
    a.LegalName,
    SUM(al.Debit) AS TotalDebit,
    SUM(al.Credit) AS TotalCredit,
    SUM(al.Debit - al.Credit) AS Balance
FROM fin.AccountLedger al
JOIN crm.Account a ON a.AccountId = al.AccountId
GROUP BY al.CompanyId, al.AccountId, a.AccountCode, a.LegalName;
GO

CREATE OR ALTER PROCEDURE core.usp_GetNextDocumentNumber
    @NumberSeriesId int,
    @DocumentNumber nvarchar(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @StartedTransaction bit = 0,
            @Prefix nvarchar(20),
            @NextNumber bigint,
            @NumberLength tinyint,
            @ResetYearly bit,
            @LastResetYear smallint,
            @CurrentYear smallint = YEAR(GETDATE());

    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @StartedTransaction = 1;
    END;

    SELECT
        @Prefix = Prefix,
        @NextNumber = NextNumber,
        @NumberLength = NumberLength,
        @ResetYearly = ResetYearly,
        @LastResetYear = LastResetYear
    FROM core.NumberSeries WITH (UPDLOCK, HOLDLOCK)
    WHERE NumberSeriesId = @NumberSeriesId;

    IF @NextNumber IS NULL
        THROW 50001, N'Belge numara serisi bulunamadı.', 1;

    IF @ResetYearly = 1 AND ISNULL(@LastResetYear, 0) <> @CurrentYear
        SET @NextNumber = 1;

    SET @DocumentNumber =
        CONCAT(ISNULL(@Prefix, N''), RIGHT(REPLICATE('0', @NumberLength) + CONVERT(varchar(20), @NextNumber), @NumberLength));

    UPDATE core.NumberSeries
       SET NextNumber = @NextNumber + 1,
           LastResetYear = @CurrentYear
     WHERE NumberSeriesId = @NumberSeriesId;

    IF @StartedTransaction = 1 COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE doc.usp_RecalculateInvoice
    @InvoiceId bigint
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM doc.Invoice WHERE InvoiceId = @InvoiceId AND InvoiceStatus = 1)
        THROW 50002, N'Yalnızca taslak faturalar yeniden hesaplanabilir.', 1;

    UPDATE i
       SET Subtotal = x.Subtotal,
           DiscountTotal = x.DiscountTotal,
           TaxTotal = x.TaxTotal,
           GrandTotal = x.GrandTotal,
           LocalGrandTotal = ROUND(x.GrandTotal * i.ExchangeRate, 4),
           UpdatedAtUtc = SYSUTCDATETIME()
    FROM doc.Invoice i
    CROSS APPLY
    (
        SELECT
            ISNULL(SUM(il.Quantity * il.UnitPrice), 0) AS Subtotal,
            ISNULL(SUM(il.DiscountAmount), 0) AS DiscountTotal,
            ISNULL(SUM(il.TaxAmount), 0) AS TaxTotal,
            ISNULL(SUM(il.LineTotal), 0) AS GrandTotal
        FROM doc.InvoiceLine il
        WHERE il.InvoiceId = i.InvoiceId
    ) x
    WHERE i.InvoiceId = @InvoiceId;
END;
GO

INSERT INTO core.Currency(CurrencyCode, CurrencyName, Symbol, DecimalPlaces)
SELECT v.CurrencyCode, v.CurrencyName, v.Symbol, v.DecimalPlaces
FROM (VALUES
    ('TRY', N'Türk Lirası', N'₺', 2),
    ('USD', N'Amerikan Doları', N'$', 2),
    ('EUR', N'Euro', N'€', 2),
    ('GBP', N'İngiliz Sterlini', N'£', 2)
) v(CurrencyCode, CurrencyName, Symbol, DecimalPlaces)
WHERE NOT EXISTS (SELECT 1 FROM core.Currency c WHERE c.CurrencyCode = v.CurrencyCode);
GO

INSERT INTO sec.Role(RoleCode, RoleName, Description, IsSystem)
SELECT v.RoleCode, v.RoleName, v.Description, 1
FROM (VALUES
    ('SYSTEM_ADMIN', N'Sistem Yöneticisi', N'Tüm sistem yetkileri'),
    ('COMPANY_ADMIN', N'Firma Yöneticisi', N'Firma kapsamındaki yönetim yetkileri'),
    ('SALES_USER', N'Satış Kullanıcısı', N'Satış ve cari görüntüleme yetkileri'),
    ('PURCHASE_USER', N'Satın Alma Kullanıcısı', N'Alış ve tedarikçi işlemleri'),
    ('WAREHOUSE_USER', N'Depo Kullanıcısı', N'Stok ve depo işlemleri'),
    ('FINANCE_USER', N'Finans Kullanıcısı', N'Kasa, banka ve tahsilat işlemleri')
) v(RoleCode, RoleName, Description)
WHERE NOT EXISTS (SELECT 1 FROM sec.Role r WHERE r.RoleCode = v.RoleCode);
GO
