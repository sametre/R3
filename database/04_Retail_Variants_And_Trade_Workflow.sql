/*
  R3 ERP - Retail variants, inventory ledger and trade workflow
  Run after 03_Organization_And_Product_Upgrade.sql.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE EngineeringERP;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'pur')
    EXEC(N'CREATE SCHEMA pur');
GO

IF OBJECT_ID(N'inv.Color', N'U') IS NULL
BEGIN
    CREATE TABLE inv.Color
    (
        ColorId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Color PRIMARY KEY,
        CompanyId    int NOT NULL,
        ColorCode    varchar(20) NOT NULL,
        ColorName    nvarchar(80) NOT NULL,
        HexCode      char(7) NULL,
        IsActive     bit NOT NULL CONSTRAINT DF_Color_IsActive DEFAULT (1),
        CONSTRAINT FK_Color_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT CK_Color_Hex CHECK (HexCode IS NULL OR HexCode LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'),
        CONSTRAINT UQ_Color_Code UNIQUE (CompanyId, ColorCode)
    );
END;
GO

IF OBJECT_ID(N'inv.Size', N'U') IS NULL
BEGIN
    CREATE TABLE inv.Size
    (
        SizeId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Size PRIMARY KEY,
        CompanyId    int NOT NULL,
        SizeCode     varchar(20) NOT NULL,
        SizeName     nvarchar(80) NOT NULL,
        SortOrder    smallint NOT NULL CONSTRAINT DF_Size_SortOrder DEFAULT (0),
        IsActive     bit NOT NULL CONSTRAINT DF_Size_IsActive DEFAULT (1),
        CONSTRAINT FK_Size_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT UQ_Size_Code UNIQUE (CompanyId, SizeCode)
    );
END;
GO

IF OBJECT_ID(N'inv.ProductVariant', N'U') IS NULL
BEGIN
    CREATE TABLE inv.ProductVariant
    (
        ProductVariantId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductVariant PRIMARY KEY,
        CompanyId        int NOT NULL,
        ProductId        bigint NOT NULL,
        VariantCode      varchar(60) NOT NULL,
        ColorId          int NULL,
        SizeId           int NULL,
        MainBarcode      varchar(50) NULL,
        PurchasePrice    decimal(19,4) NULL,
        SalesPrice       decimal(19,4) NULL,
        WholesalePrice   decimal(19,4) NULL,
        CampaignPrice    decimal(19,4) NULL,
        ShelfCode        varchar(30) NULL,
        AisleCode        varchar(30) NULL,
        TrackLot         bit NOT NULL CONSTRAINT DF_ProductVariant_TrackLot DEFAULT (0),
        TrackSerial      bit NOT NULL CONSTRAINT DF_ProductVariant_TrackSerial DEFAULT (0),
        IsActive         bit NOT NULL CONSTRAINT DF_ProductVariant_IsActive DEFAULT (1),
        CreatedAtUtc     datetime2(3) NOT NULL CONSTRAINT DF_ProductVariant_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     datetime2(3) NULL,
        RowVersion       rowversion NOT NULL,
        CONSTRAINT FK_ProductVariant_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_ProductVariant_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_ProductVariant_Color FOREIGN KEY (ColorId) REFERENCES inv.Color(ColorId),
        CONSTRAINT FK_ProductVariant_Size FOREIGN KEY (SizeId) REFERENCES inv.Size(SizeId),
        CONSTRAINT CK_ProductVariant_Prices CHECK
        (
            (PurchasePrice IS NULL OR PurchasePrice >= 0) AND
            (SalesPrice IS NULL OR SalesPrice >= 0) AND
            (WholesalePrice IS NULL OR WholesalePrice >= 0) AND
            (CampaignPrice IS NULL OR CampaignPrice >= 0)
        ),
        CONSTRAINT UQ_ProductVariant_Code UNIQUE (CompanyId, VariantCode)
    );

    CREATE UNIQUE INDEX UX_ProductVariant_MainBarcode
        ON inv.ProductVariant(CompanyId, MainBarcode)
        WHERE MainBarcode IS NOT NULL;
    CREATE INDEX IX_ProductVariant_Product
        ON inv.ProductVariant(ProductId, IsActive)
        INCLUDE(VariantCode, ColorId, SizeId, MainBarcode, SalesPrice);
END;
GO

IF OBJECT_ID(N'inv.VariantBarcode', N'U') IS NULL
BEGIN
    CREATE TABLE inv.VariantBarcode
    (
        VariantBarcodeId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_VariantBarcode PRIMARY KEY,
        ProductVariantId bigint NOT NULL,
        Barcode          varchar(50) NOT NULL,
        UnitId           int NULL,
        IsPrimary        bit NOT NULL CONSTRAINT DF_VariantBarcode_IsPrimary DEFAULT (0),
        IsActive         bit NOT NULL CONSTRAINT DF_VariantBarcode_IsActive DEFAULT (1),
        CONSTRAINT FK_VariantBarcode_Variant FOREIGN KEY (ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT FK_VariantBarcode_Unit FOREIGN KEY (UnitId) REFERENCES inv.Unit(UnitId),
        CONSTRAINT UQ_VariantBarcode UNIQUE (Barcode)
    );
END;
GO

IF COL_LENGTH(N'inv.StockTransaction', N'ProductVariantId') IS NULL
    ALTER TABLE inv.StockTransaction ADD ProductVariantId bigint NULL;
IF COL_LENGTH(N'inv.StockTransaction', N'CounterWarehouseId') IS NULL
    ALTER TABLE inv.StockTransaction ADD CounterWarehouseId int NULL;
IF COL_LENGTH(N'inv.StockTransaction', N'AccountId') IS NULL
    ALTER TABLE inv.StockTransaction ADD AccountId bigint NULL;
IF COL_LENGTH(N'inv.StockTransaction', N'DocumentType') IS NULL
    ALTER TABLE inv.StockTransaction ADD DocumentType varchar(30) NULL;
IF COL_LENGTH(N'inv.StockTransaction', N'DocumentNumber') IS NULL
    ALTER TABLE inv.StockTransaction ADD DocumentNumber nvarchar(50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockTx_Variant')
    ALTER TABLE inv.StockTransaction ADD CONSTRAINT FK_StockTx_Variant
        FOREIGN KEY (ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockTx_CounterWarehouse')
    ALTER TABLE inv.StockTransaction ADD CONSTRAINT FK_StockTx_CounterWarehouse
        FOREIGN KEY (CounterWarehouseId) REFERENCES inv.Warehouse(WarehouseId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StockTx_Account')
    ALTER TABLE inv.StockTransaction ADD CONSTRAINT FK_StockTx_Account
        FOREIGN KEY (AccountId) REFERENCES crm.Account(AccountId);
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_StockTx_Type')
    ALTER TABLE inv.StockTransaction DROP CONSTRAINT CK_StockTx_Type;
ALTER TABLE inv.StockTransaction ADD CONSTRAINT CK_StockTx_Type CHECK (TransactionType BETWEEN 1 AND 13);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'inv.StockTransaction') AND name = N'IX_StockTransaction_VariantLedger')
    CREATE INDEX IX_StockTransaction_VariantLedger
        ON inv.StockTransaction(CompanyId, WarehouseId, ProductId, ProductVariantId, TransactionDate, StockTransactionId)
        INCLUDE(QuantityIn, QuantityOut, UnitCost, DocumentType, DocumentNumber, AccountId);
GO

IF OBJECT_ID(N'inv.VariantStockBalance', N'U') IS NULL
BEGIN
    CREATE TABLE inv.VariantStockBalance
    (
        CompanyId         int NOT NULL,
        WarehouseId       int NOT NULL,
        ProductId         bigint NOT NULL,
        ProductVariantId  bigint NOT NULL,
        QuantityOnHand    decimal(19,6) NOT NULL CONSTRAINT DF_VariantBalance_OnHand DEFAULT (0),
        ReservedQuantity  decimal(19,6) NOT NULL CONSTRAINT DF_VariantBalance_Reserved DEFAULT (0),
        AvailableQuantity AS (QuantityOnHand - ReservedQuantity) PERSISTED,
        AverageUnitCost   decimal(19,6) NOT NULL CONSTRAINT DF_VariantBalance_Cost DEFAULT (0),
        LastMovementAtUtc datetime2(3) NULL,
        RowVersion        rowversion NOT NULL,
        CONSTRAINT PK_VariantStockBalance PRIMARY KEY (CompanyId, WarehouseId, ProductVariantId),
        CONSTRAINT FK_VariantBalance_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_VariantBalance_Warehouse FOREIGN KEY (WarehouseId) REFERENCES inv.Warehouse(WarehouseId),
        CONSTRAINT FK_VariantBalance_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_VariantBalance_Variant FOREIGN KEY (ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT CK_VariantBalance_Reserved CHECK (ReservedQuantity >= 0),
        CONSTRAINT CK_VariantBalance_Cost CHECK (AverageUnitCost >= 0)
    );
END;
GO

IF OBJECT_ID(N'fin.PaymentPlan', N'U') IS NULL
BEGIN
    CREATE TABLE fin.PaymentPlan
    (
        PaymentPlanId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentPlan PRIMARY KEY,
        CompanyId     int NOT NULL,
        PlanCode      varchar(20) NOT NULL,
        PlanName      nvarchar(100) NOT NULL,
        TermDays      smallint NOT NULL CONSTRAINT DF_PaymentPlan_Term DEFAULT (0),
        IsActive      bit NOT NULL CONSTRAINT DF_PaymentPlan_IsActive DEFAULT (1),
        CONSTRAINT FK_PaymentPlan_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT CK_PaymentPlan_Term CHECK (TermDays >= 0),
        CONSTRAINT UQ_PaymentPlan_Code UNIQUE (CompanyId, PlanCode)
    );
END;
GO

IF OBJECT_ID(N'pur.PurchaseRequest', N'U') IS NULL
BEGIN
    CREATE TABLE pur.PurchaseRequest
    (
        PurchaseRequestId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseRequest PRIMARY KEY,
        CompanyId         int NOT NULL,
        BranchId          int NOT NULL,
        StoreId           int NULL,
        RequestNumber     nvarchar(50) NOT NULL,
        RequestDate       date NOT NULL,
        RequiredDate      date NULL,
        Status            tinyint NOT NULL CONSTRAINT DF_PurchaseRequest_Status DEFAULT (1),
        Description       nvarchar(500) NULL,
        RequestedByUserId int NOT NULL,
        ApprovedByUserId  int NULL,
        CreatedAtUtc      datetime2(3) NOT NULL CONSTRAINT DF_PurchaseRequest_CreatedAt DEFAULT (SYSUTCDATETIME()),
        RowVersion        rowversion NOT NULL,
        CONSTRAINT FK_PurchaseRequest_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_PurchaseRequest_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_PurchaseRequest_Store FOREIGN KEY (StoreId) REFERENCES core.Store(StoreId),
        CONSTRAINT FK_PurchaseRequest_User FOREIGN KEY (RequestedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT FK_PurchaseRequest_Approver FOREIGN KEY (ApprovedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_PurchaseRequest_Status CHECK (Status BETWEEN 1 AND 6),
        CONSTRAINT UQ_PurchaseRequest_Number UNIQUE (CompanyId, RequestNumber)
    );

    CREATE TABLE pur.PurchaseRequestLine
    (
        PurchaseRequestLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseRequestLine PRIMARY KEY,
        PurchaseRequestId     bigint NOT NULL,
        LineNumber            int NOT NULL,
        ProductId             bigint NOT NULL,
        ProductVariantId      bigint NULL,
        UnitId                int NOT NULL,
        Quantity              decimal(19,6) NOT NULL,
        Description           nvarchar(300) NULL,
        CONSTRAINT FK_PurchaseRequestLine_Header FOREIGN KEY (PurchaseRequestId) REFERENCES pur.PurchaseRequest(PurchaseRequestId),
        CONSTRAINT FK_PurchaseRequestLine_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_PurchaseRequestLine_Variant FOREIGN KEY (ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT FK_PurchaseRequestLine_Unit FOREIGN KEY (UnitId) REFERENCES inv.Unit(UnitId),
        CONSTRAINT CK_PurchaseRequestLine_Quantity CHECK (Quantity > 0),
        CONSTRAINT UQ_PurchaseRequestLine_Number UNIQUE (PurchaseRequestId, LineNumber)
    );
END;
GO

IF OBJECT_ID(N'pur.SupplierQuote', N'U') IS NULL
BEGIN
    CREATE TABLE pur.SupplierQuote
    (
        SupplierQuoteId  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierQuote PRIMARY KEY,
        CompanyId        int NOT NULL,
        BranchId         int NOT NULL,
        QuoteNumber      nvarchar(50) NOT NULL,
        SupplierAccountId bigint NOT NULL,
        QuoteDate        date NOT NULL,
        ValidUntil       date NULL,
        CurrencyCode     char(3) NOT NULL,
        ExchangeRate     decimal(19,8) NOT NULL CONSTRAINT DF_SupplierQuote_Rate DEFAULT (1),
        Status           tinyint NOT NULL CONSTRAINT DF_SupplierQuote_Status DEFAULT (1),
        GrandTotal       decimal(19,4) NOT NULL CONSTRAINT DF_SupplierQuote_Total DEFAULT (0),
        Description      nvarchar(500) NULL,
        CreatedByUserId  int NOT NULL,
        CreatedAtUtc     datetime2(3) NOT NULL CONSTRAINT DF_SupplierQuote_CreatedAt DEFAULT (SYSUTCDATETIME()),
        RowVersion       rowversion NOT NULL,
        CONSTRAINT FK_SupplierQuote_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_SupplierQuote_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_SupplierQuote_Supplier FOREIGN KEY (SupplierAccountId) REFERENCES crm.Account(AccountId),
        CONSTRAINT FK_SupplierQuote_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
        CONSTRAINT FK_SupplierQuote_User FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_SupplierQuote_Rate CHECK (ExchangeRate > 0),
        CONSTRAINT CK_SupplierQuote_Total CHECK (GrandTotal >= 0),
        CONSTRAINT UQ_SupplierQuote_Number UNIQUE (CompanyId, QuoteNumber)
    );

    CREATE TABLE pur.SupplierQuoteLine
    (
        SupplierQuoteLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierQuoteLine PRIMARY KEY,
        SupplierQuoteId     bigint NOT NULL,
        LineNumber          int NOT NULL,
        ProductId           bigint NOT NULL,
        ProductVariantId    bigint NULL,
        UnitId              int NOT NULL,
        Quantity            decimal(19,6) NOT NULL,
        UnitPrice           decimal(19,6) NOT NULL,
        DiscountRate        decimal(9,6) NOT NULL CONSTRAINT DF_SupplierQuoteLine_Discount DEFAULT (0),
        VatRate             decimal(5,2) NOT NULL,
        LineTotal           decimal(19,4) NOT NULL,
        CONSTRAINT FK_SupplierQuoteLine_Header FOREIGN KEY (SupplierQuoteId) REFERENCES pur.SupplierQuote(SupplierQuoteId),
        CONSTRAINT FK_SupplierQuoteLine_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_SupplierQuoteLine_Variant FOREIGN KEY (ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT FK_SupplierQuoteLine_Unit FOREIGN KEY (UnitId) REFERENCES inv.Unit(UnitId),
        CONSTRAINT CK_SupplierQuoteLine_Values CHECK (Quantity > 0 AND UnitPrice >= 0 AND DiscountRate BETWEEN 0 AND 100 AND VatRate BETWEEN 0 AND 100 AND LineTotal >= 0),
        CONSTRAINT UQ_SupplierQuoteLine_Number UNIQUE (SupplierQuoteId, LineNumber)
    );
END;
GO

IF OBJECT_ID(N'pur.PurchaseOrder', N'U') IS NULL
BEGIN
    CREATE TABLE pur.PurchaseOrder
    (
        PurchaseOrderId  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseOrder PRIMARY KEY,
        CompanyId        int NOT NULL,
        BranchId         int NOT NULL,
        StoreId          int NULL,
        OrderNumber      nvarchar(50) NOT NULL,
        SupplierAccountId bigint NOT NULL,
        OrderDate        date NOT NULL,
        DeliveryDate     date NULL,
        DeliveryWarehouseId int NOT NULL,
        CurrencyCode     char(3) NOT NULL,
        ExchangeRate     decimal(19,8) NOT NULL CONSTRAINT DF_PurchaseOrder_Rate DEFAULT (1),
        PaymentPlanId    int NULL,
        Status           tinyint NOT NULL CONSTRAINT DF_PurchaseOrder_Status DEFAULT (1),
        Subtotal         decimal(19,4) NOT NULL CONSTRAINT DF_PurchaseOrder_Subtotal DEFAULT (0),
        DiscountTotal    decimal(19,4) NOT NULL CONSTRAINT DF_PurchaseOrder_Discount DEFAULT (0),
        TaxTotal         decimal(19,4) NOT NULL CONSTRAINT DF_PurchaseOrder_Tax DEFAULT (0),
        ExpenseTotal     decimal(19,4) NOT NULL CONSTRAINT DF_PurchaseOrder_Expense DEFAULT (0),
        GrandTotal       decimal(19,4) NOT NULL CONSTRAINT DF_PurchaseOrder_Grand DEFAULT (0),
        Description      nvarchar(500) NULL,
        CreatedByUserId  int NOT NULL,
        ApprovedByUserId int NULL,
        CreatedAtUtc     datetime2(3) NOT NULL CONSTRAINT DF_PurchaseOrder_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     datetime2(3) NULL,
        RowVersion       rowversion NOT NULL,
        CONSTRAINT FK_PurchaseOrder_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_PurchaseOrder_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_PurchaseOrder_Store FOREIGN KEY (StoreId) REFERENCES core.Store(StoreId),
        CONSTRAINT FK_PurchaseOrder_Supplier FOREIGN KEY (SupplierAccountId) REFERENCES crm.Account(AccountId),
        CONSTRAINT FK_PurchaseOrder_Warehouse FOREIGN KEY (DeliveryWarehouseId) REFERENCES inv.Warehouse(WarehouseId),
        CONSTRAINT FK_PurchaseOrder_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
        CONSTRAINT FK_PurchaseOrder_PaymentPlan FOREIGN KEY (PaymentPlanId) REFERENCES fin.PaymentPlan(PaymentPlanId),
        CONSTRAINT FK_PurchaseOrder_User FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT FK_PurchaseOrder_Approver FOREIGN KEY (ApprovedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_PurchaseOrder_Status CHECK (Status BETWEEN 1 AND 6),
        CONSTRAINT CK_PurchaseOrder_Rate CHECK (ExchangeRate > 0),
        CONSTRAINT CK_PurchaseOrder_Totals CHECK (Subtotal >= 0 AND DiscountTotal >= 0 AND TaxTotal >= 0 AND ExpenseTotal >= 0 AND GrandTotal >= 0),
        CONSTRAINT UQ_PurchaseOrder_Number UNIQUE (CompanyId, OrderNumber)
    );

    CREATE TABLE pur.PurchaseOrderLine
    (
        PurchaseOrderLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseOrderLine PRIMARY KEY,
        PurchaseOrderId     bigint NOT NULL,
        LineNumber          int NOT NULL,
        ProductId           bigint NOT NULL,
        ProductVariantId    bigint NULL,
        UnitId              int NOT NULL,
        Quantity            decimal(19,6) NOT NULL,
        DeliveredQuantity   decimal(19,6) NOT NULL CONSTRAINT DF_PurchaseOrderLine_Delivered DEFAULT (0),
        UnitPrice           decimal(19,6) NOT NULL,
        DiscountRate        decimal(9,6) NOT NULL CONSTRAINT DF_PurchaseOrderLine_Discount DEFAULT (0),
        VatRate             decimal(5,2) NOT NULL,
        ExpenseAmount       decimal(19,4) NOT NULL CONSTRAINT DF_PurchaseOrderLine_Expense DEFAULT (0),
        LineTotal           decimal(19,4) NOT NULL,
        CONSTRAINT FK_PurchaseOrderLine_Header FOREIGN KEY (PurchaseOrderId) REFERENCES pur.PurchaseOrder(PurchaseOrderId),
        CONSTRAINT FK_PurchaseOrderLine_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_PurchaseOrderLine_Variant FOREIGN KEY (ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT FK_PurchaseOrderLine_Unit FOREIGN KEY (UnitId) REFERENCES inv.Unit(UnitId),
        CONSTRAINT CK_PurchaseOrderLine_Values CHECK
        (
            Quantity > 0 AND DeliveredQuantity >= 0 AND DeliveredQuantity <= Quantity AND
            UnitPrice >= 0 AND DiscountRate BETWEEN 0 AND 100 AND VatRate BETWEEN 0 AND 100 AND
            ExpenseAmount >= 0 AND LineTotal >= 0
        ),
        CONSTRAINT UQ_PurchaseOrderLine_Number UNIQUE (PurchaseOrderId, LineNumber)
    );
END;
GO

IF OBJECT_ID(N'pur.GoodsReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE pur.GoodsReceipt
    (
        GoodsReceiptId   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_GoodsReceipt PRIMARY KEY,
        CompanyId        int NOT NULL,
        BranchId         int NOT NULL,
        StoreId          int NULL,
        ReceiptNumber    nvarchar(50) NOT NULL,
        PurchaseOrderId  bigint NULL,
        SupplierAccountId bigint NOT NULL,
        ReceiptDate      datetime2(3) NOT NULL,
        WarehouseId      int NOT NULL,
        DispatchNumber   nvarchar(50) NULL,
        DispatchDate     date NULL,
        Status           tinyint NOT NULL CONSTRAINT DF_GoodsReceipt_Status DEFAULT (1),
        Description      nvarchar(500) NULL,
        CreatedByUserId  int NOT NULL,
        CreatedAtUtc     datetime2(3) NOT NULL CONSTRAINT DF_GoodsReceipt_CreatedAt DEFAULT (SYSUTCDATETIME()),
        RowVersion       rowversion NOT NULL,
        CONSTRAINT FK_GoodsReceipt_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_GoodsReceipt_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_GoodsReceipt_Store FOREIGN KEY (StoreId) REFERENCES core.Store(StoreId),
        CONSTRAINT FK_GoodsReceipt_Order FOREIGN KEY (PurchaseOrderId) REFERENCES pur.PurchaseOrder(PurchaseOrderId),
        CONSTRAINT FK_GoodsReceipt_Supplier FOREIGN KEY (SupplierAccountId) REFERENCES crm.Account(AccountId),
        CONSTRAINT FK_GoodsReceipt_Warehouse FOREIGN KEY (WarehouseId) REFERENCES inv.Warehouse(WarehouseId),
        CONSTRAINT FK_GoodsReceipt_User FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_GoodsReceipt_Status CHECK (Status BETWEEN 1 AND 3),
        CONSTRAINT UQ_GoodsReceipt_Number UNIQUE (CompanyId, ReceiptNumber)
    );

    CREATE TABLE pur.GoodsReceiptLine
    (
        GoodsReceiptLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_GoodsReceiptLine PRIMARY KEY,
        GoodsReceiptId     bigint NOT NULL,
        PurchaseOrderLineId bigint NULL,
        LineNumber         int NOT NULL,
        ProductId          bigint NOT NULL,
        ProductVariantId   bigint NULL,
        UnitId             int NOT NULL,
        Quantity           decimal(19,6) NOT NULL,
        LotNumber          nvarchar(50) NULL,
        SerialNumber       nvarchar(100) NULL,
        ShelfCode          varchar(30) NULL,
        CONSTRAINT FK_GoodsReceiptLine_Header FOREIGN KEY (GoodsReceiptId) REFERENCES pur.GoodsReceipt(GoodsReceiptId),
        CONSTRAINT FK_GoodsReceiptLine_OrderLine FOREIGN KEY (PurchaseOrderLineId) REFERENCES pur.PurchaseOrderLine(PurchaseOrderLineId),
        CONSTRAINT FK_GoodsReceiptLine_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_GoodsReceiptLine_Variant FOREIGN KEY (ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId),
        CONSTRAINT FK_GoodsReceiptLine_Unit FOREIGN KEY (UnitId) REFERENCES inv.Unit(UnitId),
        CONSTRAINT CK_GoodsReceiptLine_Quantity CHECK (Quantity > 0),
        CONSTRAINT UQ_GoodsReceiptLine_Number UNIQUE (GoodsReceiptId, LineNumber)
    );
END;
GO

IF COL_LENGTH(N'doc.Invoice', N'InvoiceSubType') IS NULL
    ALTER TABLE doc.Invoice ADD InvoiceSubType tinyint NULL;
IF COL_LENGTH(N'doc.Invoice', N'RegistrationDate') IS NULL
    ALTER TABLE doc.Invoice ADD RegistrationDate datetime2(3) NULL;
IF COL_LENGTH(N'doc.Invoice', N'TaxNumberSnapshot') IS NULL
    ALTER TABLE doc.Invoice ADD TaxNumberSnapshot varchar(20) NULL;
IF COL_LENGTH(N'doc.Invoice', N'DispatchNumber') IS NULL
    ALTER TABLE doc.Invoice ADD DispatchNumber nvarchar(50) NULL;
IF COL_LENGTH(N'doc.Invoice', N'DispatchDate') IS NULL
    ALTER TABLE doc.Invoice ADD DispatchDate date NULL;
IF COL_LENGTH(N'doc.Invoice', N'PaymentPlanId') IS NULL
    ALTER TABLE doc.Invoice ADD PaymentPlanId int NULL;
IF COL_LENGTH(N'doc.Invoice', N'EInvoiceUuid') IS NULL
    ALTER TABLE doc.Invoice ADD EInvoiceUuid uniqueidentifier NULL;
IF COL_LENGTH(N'doc.Invoice', N'OriginalInvoiceId') IS NULL
    ALTER TABLE doc.Invoice ADD OriginalInvoiceId bigint NULL;
IF COL_LENGTH(N'doc.Invoice', N'ReturnReason') IS NULL
    ALTER TABLE doc.Invoice ADD ReturnReason nvarchar(300) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Invoice_PaymentPlan')
    ALTER TABLE doc.Invoice ADD CONSTRAINT FK_Invoice_PaymentPlan FOREIGN KEY (PaymentPlanId) REFERENCES fin.PaymentPlan(PaymentPlanId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Invoice_OriginalInvoice')
    ALTER TABLE doc.Invoice ADD CONSTRAINT FK_Invoice_OriginalInvoice FOREIGN KEY (OriginalInvoiceId) REFERENCES doc.Invoice(InvoiceId);
GO

IF COL_LENGTH(N'doc.InvoiceLine', N'ProductVariantId') IS NULL
    ALTER TABLE doc.InvoiceLine ADD ProductVariantId bigint NULL;
IF COL_LENGTH(N'doc.InvoiceLine', N'DiscountRate2') IS NULL
    ALTER TABLE doc.InvoiceLine ADD DiscountRate2 decimal(9,6) NOT NULL CONSTRAINT DF_InvoiceLine_DiscountRate2 DEFAULT (0);
IF COL_LENGTH(N'doc.InvoiceLine', N'DiscountRate3') IS NULL
    ALTER TABLE doc.InvoiceLine ADD DiscountRate3 decimal(9,6) NOT NULL CONSTRAINT DF_InvoiceLine_DiscountRate3 DEFAULT (0);
IF COL_LENGTH(N'doc.InvoiceLine', N'ExciseTaxAmount') IS NULL
    ALTER TABLE doc.InvoiceLine ADD ExciseTaxAmount decimal(19,4) NOT NULL CONSTRAINT DF_InvoiceLine_Excise DEFAULT (0);
IF COL_LENGTH(N'doc.InvoiceLine', N'WithholdingRate') IS NULL
    ALTER TABLE doc.InvoiceLine ADD WithholdingRate decimal(5,2) NOT NULL CONSTRAINT DF_InvoiceLine_Withholding DEFAULT (0);
IF COL_LENGTH(N'doc.InvoiceLine', N'LineExpense') IS NULL
    ALTER TABLE doc.InvoiceLine ADD LineExpense decimal(19,4) NOT NULL CONSTRAINT DF_InvoiceLine_Expense DEFAULT (0);
IF COL_LENGTH(N'doc.InvoiceLine', N'ShelfCode') IS NULL
    ALTER TABLE doc.InvoiceLine ADD ShelfCode varchar(30) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_InvoiceLine_Variant')
    ALTER TABLE doc.InvoiceLine ADD CONSTRAINT FK_InvoiceLine_Variant FOREIGN KEY (ProductVariantId) REFERENCES inv.ProductVariant(ProductVariantId);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_InvoiceLine_ExtendedValues')
    ALTER TABLE doc.InvoiceLine ADD CONSTRAINT CK_InvoiceLine_ExtendedValues CHECK
    (
        DiscountRate2 BETWEEN 0 AND 100 AND DiscountRate3 BETWEEN 0 AND 100 AND
        ExciseTaxAmount >= 0 AND WithholdingRate BETWEEN 0 AND 100 AND LineExpense >= 0
    );
GO

IF OBJECT_ID(N'fin.InvoicePayment', N'U') IS NULL
BEGIN
    CREATE TABLE fin.InvoicePayment
    (
        InvoicePaymentId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoicePayment PRIMARY KEY,
        InvoiceId        bigint NOT NULL,
        PaymentType      tinyint NOT NULL,
        CashAccountId    int NULL,
        BankAccountId    int NULL,
        CurrencyCode     char(3) NOT NULL,
        ExchangeRate     decimal(19,8) NOT NULL CONSTRAINT DF_InvoicePayment_Rate DEFAULT (1),
        Amount           decimal(19,4) NOT NULL,
        LocalAmount      decimal(19,4) NOT NULL,
        ReferenceNumber  nvarchar(100) NULL,
        CreatedByUserId  int NOT NULL,
        CreatedAtUtc     datetime2(3) NOT NULL CONSTRAINT DF_InvoicePayment_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_InvoicePayment_Invoice FOREIGN KEY (InvoiceId) REFERENCES doc.Invoice(InvoiceId),
        CONSTRAINT FK_InvoicePayment_Cash FOREIGN KEY (CashAccountId) REFERENCES fin.CashAccount(CashAccountId),
        CONSTRAINT FK_InvoicePayment_Bank FOREIGN KEY (BankAccountId) REFERENCES fin.BankAccount(BankAccountId),
        CONSTRAINT FK_InvoicePayment_Currency FOREIGN KEY (CurrencyCode) REFERENCES core.Currency(CurrencyCode),
        CONSTRAINT FK_InvoicePayment_User FOREIGN KEY (CreatedByUserId) REFERENCES sec.AppUser(UserId),
        CONSTRAINT CK_InvoicePayment_Type CHECK (PaymentType BETWEEN 1 AND 11),
        CONSTRAINT CK_InvoicePayment_Amounts CHECK (Amount > 0 AND LocalAmount > 0 AND ExchangeRate > 0)
    );
    CREATE INDEX IX_InvoicePayment_Invoice ON fin.InvoicePayment(InvoiceId) INCLUDE(PaymentType, Amount, LocalAmount);
END;
GO

IF OBJECT_ID(N'doc.InvoiceReturnLine', N'U') IS NULL
BEGIN
    CREATE TABLE doc.InvoiceReturnLine
    (
        InvoiceReturnLineId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceReturnLine PRIMARY KEY,
        ReturnInvoiceId     bigint NOT NULL,
        ReturnInvoiceLineId bigint NOT NULL,
        SourceInvoiceId     bigint NOT NULL,
        SourceInvoiceLineId bigint NOT NULL,
        ReturnQuantity      decimal(19,6) NOT NULL,
        OriginalUnitPrice   decimal(19,6) NOT NULL,
        OriginalVatRate     decimal(5,2) NOT NULL,
        ReturnReason        nvarchar(300) NOT NULL,
        ReturnWarehouseId   int NOT NULL,
        CreatedAtUtc        datetime2(3) NOT NULL CONSTRAINT DF_InvoiceReturnLine_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_InvoiceReturnLine_ReturnInvoice FOREIGN KEY (ReturnInvoiceId) REFERENCES doc.Invoice(InvoiceId),
        CONSTRAINT FK_InvoiceReturnLine_ReturnLine FOREIGN KEY (ReturnInvoiceLineId) REFERENCES doc.InvoiceLine(InvoiceLineId),
        CONSTRAINT FK_InvoiceReturnLine_SourceInvoice FOREIGN KEY (SourceInvoiceId) REFERENCES doc.Invoice(InvoiceId),
        CONSTRAINT FK_InvoiceReturnLine_SourceLine FOREIGN KEY (SourceInvoiceLineId) REFERENCES doc.InvoiceLine(InvoiceLineId),
        CONSTRAINT FK_InvoiceReturnLine_Warehouse FOREIGN KEY (ReturnWarehouseId) REFERENCES inv.Warehouse(WarehouseId),
        CONSTRAINT CK_InvoiceReturnLine_Quantity CHECK (ReturnQuantity > 0),
        CONSTRAINT CK_InvoiceReturnLine_Values CHECK (OriginalUnitPrice >= 0 AND OriginalVatRate BETWEEN 0 AND 100),
        CONSTRAINT UQ_InvoiceReturnLine_ReturnLine UNIQUE (ReturnInvoiceLineId)
    );
    CREATE INDEX IX_InvoiceReturnLine_Source ON doc.InvoiceReturnLine(SourceInvoiceLineId) INCLUDE(ReturnQuantity);
END;
GO

CREATE OR ALTER TRIGGER doc.trg_InvoiceReturnLine_ValidateQuantity
ON doc.InvoiceReturnLine
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS i
        INNER JOIN doc.InvoiceLine AS returnLine ON returnLine.InvoiceLineId = i.ReturnInvoiceLineId
        INNER JOIN doc.InvoiceLine AS sourceLine ON sourceLine.InvoiceLineId = i.SourceInvoiceLineId
        WHERE returnLine.InvoiceId <> i.ReturnInvoiceId
           OR sourceLine.InvoiceId <> i.SourceInvoiceId
    )
        THROW 50020, N'İade satırı ile kaynak veya iade faturası arasında geçersiz bağlantı var.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM (SELECT DISTINCT SourceInvoiceLineId FROM inserted) AS changed
        INNER JOIN doc.InvoiceLine AS sourceLine ON sourceLine.InvoiceLineId = changed.SourceInvoiceLineId
        CROSS APPLY
        (
            SELECT SUM(returned.ReturnQuantity) AS TotalReturnedQuantity
            FROM doc.InvoiceReturnLine AS returned
            WHERE returned.SourceInvoiceLineId = changed.SourceInvoiceLineId
        ) AS totals
        WHERE totals.TotalReturnedQuantity > sourceLine.Quantity
    )
        THROW 50021, N'İade miktarı, kaynak faturadaki iade edilebilir miktarı aşamaz.', 1;
END;
GO

CREATE OR ALTER VIEW inv.vwVariantStockStatus
AS
SELECT
    b.CompanyId,
    b.WarehouseId,
    w.WarehouseCode,
    w.WarehouseName,
    b.ProductId,
    p.ProductCode,
    p.ProductName,
    b.ProductVariantId,
    v.VariantCode,
    c.ColorName,
    s.SizeName,
    v.MainBarcode,
    b.QuantityOnHand,
    b.ReservedQuantity,
    b.AvailableQuantity,
    b.AverageUnitCost,
    CAST(b.QuantityOnHand * b.AverageUnitCost AS decimal(19,4)) AS StockValue,
    b.LastMovementAtUtc
FROM inv.VariantStockBalance b
JOIN inv.Warehouse w ON w.WarehouseId = b.WarehouseId
JOIN inv.Product p ON p.ProductId = b.ProductId
JOIN inv.ProductVariant v ON v.ProductVariantId = b.ProductVariantId
LEFT JOIN inv.Color c ON c.ColorId = v.ColorId
LEFT JOIN inv.Size s ON s.SizeId = v.SizeId;
GO
