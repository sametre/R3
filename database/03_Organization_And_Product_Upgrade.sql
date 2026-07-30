/*
  R3 ERP - Organization and extended product model
  Run after 01_ERP_Database.sql.
  SQL Server 2019+
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE EngineeringERP;
GO

IF OBJECT_ID(N'core.Store', N'U') IS NULL
BEGIN
    CREATE TABLE core.Store
    (
        StoreId          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Store PRIMARY KEY,
        CompanyId        int NOT NULL,
        BranchId         int NOT NULL,
        StoreCode        varchar(20) NOT NULL,
        StoreName        nvarchar(150) NOT NULL,
        AddressText      nvarchar(500) NULL,
        Phone            varchar(30) NULL,
        Email            varchar(254) NULL,
        IsActive         bit NOT NULL CONSTRAINT DF_Store_IsActive DEFAULT (1),
        CreatedAtUtc     datetime2(3) NOT NULL CONSTRAINT DF_Store_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     datetime2(3) NULL,
        RowVersion       rowversion NOT NULL,
        CONSTRAINT FK_Store_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_Store_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT UQ_Store_Code UNIQUE (CompanyId, StoreCode)
    );
END;
GO

IF COL_LENGTH(N'inv.Warehouse', N'StoreId') IS NULL
BEGIN
    ALTER TABLE inv.Warehouse ADD StoreId int NULL;
    EXEC(N'ALTER TABLE inv.Warehouse ADD CONSTRAINT FK_Warehouse_Store
        FOREIGN KEY (StoreId) REFERENCES core.Store(StoreId);');
    EXEC(N'CREATE INDEX IX_Warehouse_Store ON inv.Warehouse(StoreId) WHERE StoreId IS NOT NULL;');
END;
GO

IF COL_LENGTH(N'fin.CashAccount', N'StoreId') IS NULL
BEGIN
    ALTER TABLE fin.CashAccount ADD StoreId int NULL;
    EXEC(N'ALTER TABLE fin.CashAccount ADD CONSTRAINT FK_CashAccount_Store
        FOREIGN KEY (StoreId) REFERENCES core.Store(StoreId);');
    EXEC(N'CREATE INDEX IX_CashAccount_Store ON fin.CashAccount(StoreId) WHERE StoreId IS NOT NULL;');
END;
GO

IF OBJECT_ID(N'core.SalesTerminal', N'U') IS NULL
BEGIN
    CREATE TABLE core.SalesTerminal
    (
        SalesTerminalId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesTerminal PRIMARY KEY,
        CompanyId       int NOT NULL,
        BranchId        int NOT NULL,
        StoreId         int NOT NULL,
        CashAccountId   int NULL,
        TerminalCode    varchar(20) NOT NULL,
        TerminalName    nvarchar(100) NOT NULL,
        DeviceName      nvarchar(128) NULL,
        IpAddress       varchar(45) NULL,
        IsActive        bit NOT NULL CONSTRAINT DF_SalesTerminal_IsActive DEFAULT (1),
        LastSeenAtUtc   datetime2(3) NULL,
        RowVersion      rowversion NOT NULL,
        CONSTRAINT FK_SalesTerminal_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_SalesTerminal_Branch FOREIGN KEY (BranchId) REFERENCES core.Branch(BranchId),
        CONSTRAINT FK_SalesTerminal_Store FOREIGN KEY (StoreId) REFERENCES core.Store(StoreId),
        CONSTRAINT FK_SalesTerminal_Cash FOREIGN KEY (CashAccountId) REFERENCES fin.CashAccount(CashAccountId),
        CONSTRAINT UQ_SalesTerminal_Code UNIQUE (CompanyId, TerminalCode)
    );
END;
GO

IF OBJECT_ID(N'inv.Brand', N'U') IS NULL
BEGIN
    CREATE TABLE inv.Brand
    (
        BrandId      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Brand PRIMARY KEY,
        CompanyId    int NOT NULL,
        BrandCode    varchar(20) NOT NULL,
        BrandName    nvarchar(100) NOT NULL,
        IsActive     bit NOT NULL CONSTRAINT DF_Brand_IsActive DEFAULT (1),
        CONSTRAINT FK_Brand_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT UQ_Brand_Code UNIQUE (CompanyId, BrandCode)
    );
END;
GO

IF OBJECT_ID(N'inv.Category', N'U') IS NULL
BEGIN
    CREATE TABLE inv.Category
    (
        CategoryId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Category PRIMARY KEY,
        CompanyId        int NOT NULL,
        ParentCategoryId int NULL,
        CategoryCode     varchar(20) NOT NULL,
        CategoryName     nvarchar(120) NOT NULL,
        IsActive         bit NOT NULL CONSTRAINT DF_Category_IsActive DEFAULT (1),
        CONSTRAINT FK_Category_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT FK_Category_Parent FOREIGN KEY (ParentCategoryId) REFERENCES inv.Category(CategoryId),
        CONSTRAINT UQ_Category_Code UNIQUE (CompanyId, CategoryCode)
    );
END;
GO

IF OBJECT_ID(N'inv.ProductGroup', N'U') IS NULL
BEGIN
    CREATE TABLE inv.ProductGroup
    (
        ProductGroupId   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductGroup PRIMARY KEY,
        CompanyId        int NOT NULL,
        ProductGroupCode varchar(20) NOT NULL,
        ProductGroupName nvarchar(120) NOT NULL,
        IsActive         bit NOT NULL CONSTRAINT DF_ProductGroup_IsActive DEFAULT (1),
        CONSTRAINT FK_ProductGroup_Company FOREIGN KEY (CompanyId) REFERENCES core.Company(CompanyId),
        CONSTRAINT UQ_ProductGroup_Code UNIQUE (CompanyId, ProductGroupCode)
    );
END;
GO

IF COL_LENGTH(N'inv.Product', N'ShortName') IS NULL
    ALTER TABLE inv.Product ADD ShortName nvarchar(100) NULL;
IF COL_LENGTH(N'inv.Product', N'Description') IS NULL
    ALTER TABLE inv.Product ADD Description nvarchar(1000) NULL;
IF COL_LENGTH(N'inv.Product', N'BrandId') IS NULL
    ALTER TABLE inv.Product ADD BrandId int NULL;
IF COL_LENGTH(N'inv.Product', N'CategoryId') IS NULL
    ALTER TABLE inv.Product ADD CategoryId int NULL;
IF COL_LENGTH(N'inv.Product', N'ProductGroupId') IS NULL
    ALTER TABLE inv.Product ADD ProductGroupId int NULL;
IF COL_LENGTH(N'inv.Product', N'WholesalePrice') IS NULL
    ALTER TABLE inv.Product ADD WholesalePrice decimal(19,4) NOT NULL CONSTRAINT DF_Product_Wholesale DEFAULT (0);
IF COL_LENGTH(N'inv.Product', N'CampaignPrice') IS NULL
    ALTER TABLE inv.Product ADD CampaignPrice decimal(19,4) NOT NULL CONSTRAINT DF_Product_Campaign DEFAULT (0);
IF COL_LENGTH(N'inv.Product', N'CriticalStockLevel') IS NULL
    ALTER TABLE inv.Product ADD CriticalStockLevel decimal(19,6) NOT NULL CONSTRAINT DF_Product_Critical DEFAULT (0);
IF COL_LENGTH(N'inv.Product', N'ShelfCode') IS NULL
    ALTER TABLE inv.Product ADD ShelfCode varchar(30) NULL;
IF COL_LENGTH(N'inv.Product', N'AisleCode') IS NULL
    ALTER TABLE inv.Product ADD AisleCode varchar(30) NULL;
IF COL_LENGTH(N'inv.Product', N'SupplierAccountId') IS NULL
    ALTER TABLE inv.Product ADD SupplierAccountId bigint NULL;
IF COL_LENGTH(N'inv.Product', N'ManufacturerCode') IS NULL
    ALTER TABLE inv.Product ADD ManufacturerCode varchar(50) NULL;
IF COL_LENGTH(N'inv.Product', N'CountryOfOrigin') IS NULL
    ALTER TABLE inv.Product ADD CountryOfOrigin char(2) NULL;
IF COL_LENGTH(N'inv.Product', N'WarrantyMonths') IS NULL
    ALTER TABLE inv.Product ADD WarrantyMonths smallint NOT NULL CONSTRAINT DF_Product_Warranty DEFAULT (0);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Product_Brand')
    ALTER TABLE inv.Product ADD CONSTRAINT FK_Product_Brand FOREIGN KEY (BrandId) REFERENCES inv.Brand(BrandId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Product_Category')
    ALTER TABLE inv.Product ADD CONSTRAINT FK_Product_Category FOREIGN KEY (CategoryId) REFERENCES inv.Category(CategoryId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Product_ProductGroup')
    ALTER TABLE inv.Product ADD CONSTRAINT FK_Product_ProductGroup FOREIGN KEY (ProductGroupId) REFERENCES inv.ProductGroup(ProductGroupId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Product_Supplier')
    ALTER TABLE inv.Product ADD CONSTRAINT FK_Product_Supplier FOREIGN KEY (SupplierAccountId) REFERENCES crm.Account(AccountId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Product_ExtendedPrices')
    ALTER TABLE inv.Product ADD CONSTRAINT CK_Product_ExtendedPrices
        CHECK (WholesalePrice >= 0 AND CampaignPrice >= 0);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Product_CriticalStock')
    ALTER TABLE inv.Product ADD CONSTRAINT CK_Product_CriticalStock CHECK (CriticalStockLevel >= 0);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Product_Warranty')
    ALTER TABLE inv.Product ADD CONSTRAINT CK_Product_Warranty CHECK (WarrantyMonths >= 0);
GO

IF OBJECT_ID(N'inv.ProductBarcode', N'U') IS NULL
BEGIN
    CREATE TABLE inv.ProductBarcode
    (
        ProductBarcodeId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductBarcode PRIMARY KEY,
        ProductId        bigint NOT NULL,
        Barcode          varchar(50) NOT NULL,
        UnitId           int NULL,
        IsPrimary        bit NOT NULL CONSTRAINT DF_ProductBarcode_IsPrimary DEFAULT (0),
        IsActive         bit NOT NULL CONSTRAINT DF_ProductBarcode_IsActive DEFAULT (1),
        CONSTRAINT FK_ProductBarcode_Product FOREIGN KEY (ProductId) REFERENCES inv.Product(ProductId),
        CONSTRAINT FK_ProductBarcode_Unit FOREIGN KEY (UnitId) REFERENCES inv.Unit(UnitId),
        CONSTRAINT UQ_ProductBarcode_Barcode UNIQUE (Barcode)
    );
END;
GO

CREATE OR ALTER VIEW core.vwOrganizationTree
AS
SELECT
    c.CompanyId,
    c.CompanyCode,
    c.LegalName,
    b.BranchId,
    b.BranchCode,
    b.BranchName,
    s.StoreId,
    s.StoreCode,
    s.StoreName,
    w.WarehouseId,
    w.WarehouseCode,
    w.WarehouseName
FROM core.Company c
JOIN core.Branch b ON b.CompanyId = c.CompanyId
LEFT JOIN core.Store s ON s.BranchId = b.BranchId
LEFT JOIN inv.Warehouse w ON w.BranchId = b.BranchId
    AND (w.StoreId = s.StoreId OR (w.StoreId IS NULL AND s.StoreId IS NULL));
GO
