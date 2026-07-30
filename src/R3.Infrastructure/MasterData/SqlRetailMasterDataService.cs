using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using R3.Application.MasterData;

namespace R3.Infrastructure.MasterData;

internal sealed class SqlRetailMasterDataService(IConfiguration configuration) : IRetailMasterDataService
{
    private readonly string _connectionString = configuration.GetConnectionString("R3Database")
        ?? throw new InvalidOperationException("R3Database bağlantı dizesi bulunamadı.");

    public async Task<IReadOnlyList<ProductListItem>> GetProductsAsync(int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT p.ProductId,p.ProductCode,p.ProductName,p.ShortName,
              CASE p.ProductType WHEN 1 THEN N'Stok' WHEN 2 THEN N'Hizmet' WHEN 3 THEN N'Hammadde'
                   WHEN 4 THEN N'Yarı Mamul' ELSE N'Mamul' END,
              u.UnitCode,p.Barcode,p.VatRate,p.PurchasePrice,p.SalesPrice,p.WholesalePrice,p.CampaignPrice,
              p.CriticalStockLevel,p.ShelfCode,p.AisleCode,CASE WHEN p.IsActive=1 THEN N'Aktif' ELSE N'Pasif' END,
              p.MinStockLevel,p.MaxStockLevel,p.Description,p.ManufacturerCode,p.CountryOfOrigin,p.WarrantyMonths,p.TrackLot,p.TrackSerial
            FROM inv.Product p JOIN inv.Unit u ON u.UnitId=p.BaseUnitId
            WHERE p.CompanyId=@CompanyId AND p.IsDeleted=0 ORDER BY p.ProductCode;
            """;
        var result = new List<ProductListItem>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@CompanyId", SqlDbType.Int).Value = companyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),StringValue(reader,3),reader.GetString(4),
                reader.GetString(5),StringValue(reader,6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetDecimal(9),
                reader.GetDecimal(10),reader.GetDecimal(11),reader.GetDecimal(12),StringValue(reader,13),StringValue(reader,14),reader.GetString(15),
                reader.GetDecimal(16),NullableValue<decimal>(reader,17),StringValue(reader,18),StringValue(reader,19),StringValue(reader,20),reader.GetInt16(21),reader.GetBoolean(22),reader.GetBoolean(23)));
        return result;
    }

    public async Task<IReadOnlyList<ProductVariantListItem>> GetVariantsAsync(int companyId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT v.ProductVariantId,p.ProductCode,p.ProductName,v.VariantCode,c.ColorName,s.SizeName,v.MainBarcode,
                   v.PurchasePrice,v.SalesPrice,v.ShelfCode,v.AisleCode,CASE WHEN v.IsActive=1 THEN N'Aktif' ELSE N'Pasif' END
            FROM inv.ProductVariant v JOIN inv.Product p ON p.ProductId=v.ProductId
            LEFT JOIN inv.Color c ON c.ColorId=v.ColorId LEFT JOIN inv.Size s ON s.SizeId=v.SizeId
            WHERE v.CompanyId=@CompanyId ORDER BY p.ProductCode,v.VariantCode;
            """;
        var result = new List<ProductVariantListItem>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@CompanyId", SqlDbType.Int).Value = companyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
                StringValue(reader,4),StringValue(reader,5),StringValue(reader,6),NullableValue<decimal>(reader,7),
                NullableValue<decimal>(reader,8),StringValue(reader,9),StringValue(reader,10),reader.GetString(11)));
        return result;
    }

    public async Task<IReadOnlyList<AccountListItem>> GetAccountsAsync(int companyId, byte? accountType = null, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT a.AccountId,a.AccountCode,a.LegalName,
              CASE a.AccountType WHEN 1 THEN N'Müşteri' WHEN 2 THEN N'Tedarikçi' WHEN 3 THEN N'Müşteri + Tedarikçi'
                   WHEN 4 THEN N'Personel' ELSE N'Diğer' END,
              a.TaxNumber,a.IdentityNumber,ad.City,a.Phone,
              COALESCE((SELECT SUM(t.Debit-t.Credit) FROM crm.AccountTransaction t WHERE t.AccountId=a.AccountId),0),
              a.CurrencyCode,CASE WHEN a.IsActive=1 THEN N'Aktif' ELSE N'Pasif' END,a.AccountType,a.TradeName,a.TaxOffice,a.MersisNumber,
              a.CreditLimit,a.PaymentTermDays,a.Email,a.PriceListCode,a.DiscountGroupCode,a.PaymentPlanId,a.IsEInvoiceUser,a.EInvoiceAlias,a.Notes,
              ad.District,ad.AddressLine
            FROM crm.Account a
            OUTER APPLY(SELECT TOP(1) x.City,x.District,x.AddressLine FROM crm.AccountAddress x WHERE x.AccountId=a.AccountId ORDER BY x.IsDefault DESC,x.AccountAddressId) ad
            WHERE a.CompanyId=@CompanyId AND a.IsDeleted=0 AND (@AccountType IS NULL OR a.AccountType=@AccountType)
            ORDER BY a.AccountCode;
            """;
        var result = new List<AccountListItem>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@CompanyId", SqlDbType.Int).Value = companyId;
        command.Parameters.Add("@AccountType", SqlDbType.TinyInt).Value = accountType is null ? DBNull.Value : accountType.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
                StringValue(reader,4),StringValue(reader,5),StringValue(reader,6),StringValue(reader,7),
                reader.GetDecimal(8),reader.GetString(9),reader.GetString(10),reader.GetByte(11),StringValue(reader,12),StringValue(reader,13),
                StringValue(reader,14),reader.GetDecimal(15),reader.GetInt16(16),StringValue(reader,17),StringValue(reader,18),StringValue(reader,19),
                NullableValue<int>(reader,20),reader.GetBoolean(21),StringValue(reader,22),StringValue(reader,23),StringValue(reader,24),StringValue(reader,25)));
        return result;
    }

    public async Task<MasterDataLookups> GetLookupsAsync(int companyId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return new(
            await LoadLookup(connection,"SELECT UnitId,UnitCode,UnitName FROM inv.Unit WHERE CompanyId=@CompanyId AND IsActive=1 ORDER BY UnitName",companyId,cancellationToken),
            await LoadLookup(connection,"SELECT BrandId,BrandCode,BrandName FROM inv.Brand WHERE CompanyId=@CompanyId AND IsActive=1 ORDER BY BrandName",companyId,cancellationToken),
            await LoadLookup(connection,"SELECT CategoryId,CategoryCode,CategoryName FROM inv.Category WHERE CompanyId=@CompanyId AND IsActive=1 ORDER BY CategoryName",companyId,cancellationToken),
            await LoadLookup(connection,"SELECT ProductGroupId,ProductGroupCode,ProductGroupName FROM inv.ProductGroup WHERE CompanyId=@CompanyId AND IsActive=1 ORDER BY ProductGroupName",companyId,cancellationToken),
            await LoadLookup(connection,"SELECT PaymentPlanId,PlanCode,PlanName FROM fin.PaymentPlan WHERE CompanyId=@CompanyId AND IsActive=1 ORDER BY PlanName",companyId,cancellationToken));
    }

    public async Task<long> SaveProductAsync(ProductSaveRequest r, CancellationToken cancellationToken = default)
    {
        if (r.ProductId is long existingId)
        {
            await UpdateProductAsync(r, existingId, cancellationToken);
            return existingId;
        }
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string sql = """
                INSERT inv.Product(CompanyId,ProductCode,Barcode,ProductName,ProductType,BaseUnitId,VatRate,PurchasePrice,SalesPrice,CurrencyCode,
                 MinStockLevel,MaxStockLevel,TrackLot,TrackSerial,IsActive,IsDeleted,CreatedByUserId,CreatedAtUtc,ShortName,Description,BrandId,
                 CategoryId,ProductGroupId,WholesalePrice,CampaignPrice,CriticalStockLevel,ShelfCode,AisleCode,ManufacturerCode,CountryOfOrigin,WarrantyMonths)
                VALUES(@CompanyId,@ProductCode,@Barcode,@ProductName,@ProductType,@BaseUnitId,@VatRate,@PurchasePrice,@SalesPrice,'TRY',
                 @MinStock,@MaxStock,@TrackLot,@TrackSerial,@IsActive,0,@UserId,SYSUTCDATETIME(),@ShortName,@Description,@BrandId,
                 @CategoryId,@ProductGroupId,@WholesalePrice,@CampaignPrice,@CriticalStock,@ShelfCode,@AisleCode,@ManufacturerCode,@Origin,@Warranty);
                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;
            await using var command = new SqlCommand(sql, connection, transaction);
            Add(command,"@CompanyId",r.CompanyId); Add(command,"@UserId",r.UserId); Add(command,"@ProductCode",r.ProductCode);
            Add(command,"@Barcode",r.MainBarcode); Add(command,"@ProductName",r.ProductName); Add(command,"@ProductType",r.ProductType);
            Add(command,"@BaseUnitId",r.BaseUnitId); Add(command,"@VatRate",r.VatRate); Add(command,"@PurchasePrice",r.PurchasePrice);
            Add(command,"@SalesPrice",r.SalesPrice); Add(command,"@MinStock",r.MinimumStock); Add(command,"@MaxStock",r.MaximumStock);
            Add(command,"@TrackLot",r.TrackLot); Add(command,"@TrackSerial",r.TrackSerial); Add(command,"@IsActive",r.IsActive);
            Add(command,"@ShortName",r.ShortName); Add(command,"@Description",r.Description); Add(command,"@BrandId",r.BrandId);
            Add(command,"@CategoryId",r.CategoryId); Add(command,"@ProductGroupId",r.ProductGroupId); Add(command,"@WholesalePrice",r.WholesalePrice);
            Add(command,"@CampaignPrice",r.CampaignPrice); Add(command,"@CriticalStock",r.CriticalStock); Add(command,"@ShelfCode",r.ShelfCode);
            Add(command,"@AisleCode",r.AisleCode); Add(command,"@ManufacturerCode",r.ManufacturerCode); Add(command,"@Origin",r.CountryOfOrigin);
            Add(command,"@Warranty",r.WarrantyMonths);
            long productId = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Ürün kaydı oluşturulamadı."));

            foreach (ProductVariantSaveRequest variant in r.Variants)
            {
                int? colorId = await EnsureColor(connection,transaction,r.CompanyId,variant.ColorCode,variant.ColorName,cancellationToken);
                int? sizeId = await EnsureSize(connection,transaction,r.CompanyId,variant.SizeCode,variant.SizeName,cancellationToken);
                const string variantSql = """
                    INSERT inv.ProductVariant(CompanyId,ProductId,VariantCode,ColorId,SizeId,MainBarcode,PurchasePrice,SalesPrice,
                     WholesalePrice,CampaignPrice,ShelfCode,AisleCode,TrackLot,TrackSerial,IsActive)
                    VALUES(@CompanyId,@ProductId,@VariantCode,@ColorId,@SizeId,@Barcode,@PurchasePrice,@SalesPrice,
                     @WholesalePrice,@CampaignPrice,@ShelfCode,@AisleCode,@TrackLot,@TrackSerial,1);
                    """;
                await using var variantCommand = new SqlCommand(variantSql,connection,transaction);
                Add(variantCommand,"@CompanyId",r.CompanyId); Add(variantCommand,"@ProductId",productId);
                Add(variantCommand,"@VariantCode",variant.VariantCode); Add(variantCommand,"@ColorId",colorId); Add(variantCommand,"@SizeId",sizeId);
                Add(variantCommand,"@Barcode",variant.Barcode); Add(variantCommand,"@PurchasePrice",variant.PurchasePrice);
                Add(variantCommand,"@SalesPrice",variant.SalesPrice); Add(variantCommand,"@WholesalePrice",variant.WholesalePrice);
                Add(variantCommand,"@CampaignPrice",variant.CampaignPrice); Add(variantCommand,"@ShelfCode",variant.ShelfCode);
                Add(variantCommand,"@AisleCode",variant.AisleCode); Add(variantCommand,"@TrackLot",variant.TrackLot); Add(variantCommand,"@TrackSerial",variant.TrackSerial);
                await variantCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return productId;
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task<long> SaveAccountAsync(AccountSaveRequest r, CancellationToken cancellationToken = default)
    {
        if (r.AccountId is long existingId)
        {
            await UpdateAccountAsync(r, existingId, cancellationToken);
            return existingId;
        }
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string sql = """
                INSERT crm.Account(CompanyId,AccountCode,AccountType,LegalName,TradeName,TaxOffice,TaxNumber,IdentityNumber,CurrencyCode,
                 CreditLimit,PaymentTermDays,RiskStatus,Phone,Email,EInvoiceAlias,IsEInvoiceUser,Notes,IsActive,IsDeleted,CreatedByUserId,
                 CreatedAtUtc,MersisNumber,PriceListCode,DiscountGroupCode,PaymentPlanId)
                VALUES(@CompanyId,@Code,@Type,@LegalName,@TradeName,@TaxOffice,@TaxNumber,@IdentityNumber,@Currency,@CreditLimit,@TermDays,
                 1,@Phone,@Email,@Alias,@EInvoice,@Notes,@IsActive,0,@UserId,SYSUTCDATETIME(),@Mersis,@PriceList,@DiscountGroup,@PaymentPlanId);
                SELECT CAST(SCOPE_IDENTITY() AS bigint);
                """;
            await using var command = new SqlCommand(sql,connection,transaction);
            Add(command,"@CompanyId",r.CompanyId); Add(command,"@Code",r.AccountCode); Add(command,"@Type",r.AccountType);
            Add(command,"@LegalName",r.LegalName); Add(command,"@TradeName",r.TradeName); Add(command,"@TaxOffice",r.TaxOffice);
            Add(command,"@TaxNumber",r.TaxNumber); Add(command,"@IdentityNumber",r.IdentityNumber); Add(command,"@Currency",r.CurrencyCode);
            Add(command,"@CreditLimit",r.CreditLimit); Add(command,"@TermDays",r.PaymentTermDays); Add(command,"@Phone",r.Phone);
            Add(command,"@Email",r.Email); Add(command,"@Alias",r.EInvoiceAlias); Add(command,"@EInvoice",r.IsEInvoiceUser);
            Add(command,"@Notes",r.Notes); Add(command,"@IsActive",r.IsActive); Add(command,"@UserId",r.UserId);
            Add(command,"@Mersis",r.MersisNumber); Add(command,"@PriceList",r.PriceListCode);
            Add(command,"@DiscountGroup",r.DiscountGroupCode); Add(command,"@PaymentPlanId",r.PaymentPlanId);
            long id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Cari kart oluşturulamadı."));
            if (!string.IsNullOrWhiteSpace(r.Address))
            {
                const string addressSql = """
                    INSERT crm.AccountAddress(AccountId,AddressType,AddressName,CountryCode,City,District,AddressLine,IsDefault)
                    VALUES(@AccountId,1,N'Fatura Adresi','TR',@City,@District,@Address,1);
                    """;
                await using var address = new SqlCommand(addressSql,connection,transaction);
                Add(address,"@AccountId",id); Add(address,"@City",r.City); Add(address,"@District",r.District); Add(address,"@Address",r.Address);
                await address.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return id;
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private static async Task<IReadOnlyList<LookupItem>> LoadLookup(SqlConnection connection,string sql,int companyId,CancellationToken token)
    {
        var result = new List<LookupItem>();
        await using var command = new SqlCommand(sql,connection);
        command.Parameters.Add("@CompanyId",SqlDbType.Int).Value=companyId;
        await using var reader = await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)) result.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2)));
        return result;
    }

    private static async Task<int?> EnsureColor(SqlConnection c,SqlTransaction t,int companyId,string? code,string? name,CancellationToken token) =>
        await EnsureDimension(c,t,"inv.Color","ColorId","ColorCode","ColorName",companyId,code,name,token);
    private static async Task<int?> EnsureSize(SqlConnection c,SqlTransaction t,int companyId,string? code,string? name,CancellationToken token) =>
        await EnsureDimension(c,t,"inv.Size","SizeId","SizeCode","SizeName",companyId,code,name,token);
    private static async Task<int?> EnsureDimension(SqlConnection c,SqlTransaction t,string table,string idColumn,string codeColumn,string nameColumn,int companyId,string? code,string? name,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(code)) return null;
        string sql=$"""
            DECLARE @Id int=(SELECT {idColumn} FROM {table} WHERE CompanyId=@CompanyId AND {codeColumn}=@Code);
            IF @Id IS NULL BEGIN
              INSERT {table}(CompanyId,{codeColumn},{nameColumn},IsActive) VALUES(@CompanyId,@Code,@Name,1);
              SET @Id=CAST(SCOPE_IDENTITY() AS int);
            END;
            SELECT @Id;
            """;
        await using var command=new SqlCommand(sql,c,t);
        Add(command,"@CompanyId",companyId); Add(command,"@Code",code.Trim().ToUpperInvariant()); Add(command,"@Name",string.IsNullOrWhiteSpace(name)?code:name);
        return (int?)await command.ExecuteScalarAsync(token);
    }

    private static T? NullableValue<T>(SqlDataReader reader,int ordinal) where T:struct => reader.IsDBNull(ordinal)?null:reader.GetFieldValue<T>(ordinal);
    private static string? StringValue(SqlDataReader reader,int ordinal) => reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
    private static void Add(SqlCommand command,string name,object? value) => command.Parameters.AddWithValue(name,value ?? DBNull.Value);

    private async Task UpdateProductAsync(ProductSaveRequest r,long productId,CancellationToken token)
    {
        const string sql = """
            UPDATE inv.Product SET ProductCode=@ProductCode,Barcode=@Barcode,ProductName=@ProductName,ProductType=@ProductType,
             BaseUnitId=@BaseUnitId,VatRate=@VatRate,PurchasePrice=@PurchasePrice,SalesPrice=@SalesPrice,
             MinStockLevel=@MinStock,MaxStockLevel=@MaxStock,TrackLot=@TrackLot,TrackSerial=@TrackSerial,IsActive=@IsActive,
             ShortName=@ShortName,Description=COALESCE(@Description,Description),BrandId=COALESCE(@BrandId,BrandId),
             CategoryId=COALESCE(@CategoryId,CategoryId),ProductGroupId=COALESCE(@ProductGroupId,ProductGroupId),
             WholesalePrice=@WholesalePrice,CampaignPrice=@CampaignPrice,CriticalStockLevel=@CriticalStock,
             ShelfCode=@ShelfCode,AisleCode=@AisleCode,ManufacturerCode=COALESCE(@ManufacturerCode,ManufacturerCode),
             CountryOfOrigin=COALESCE(@Origin,CountryOfOrigin),WarrantyMonths=@Warranty,UpdatedByUserId=@UserId,UpdatedAtUtc=SYSUTCDATETIME()
            WHERE ProductId=@ProductId AND CompanyId=@CompanyId AND IsDeleted=0;
            IF @@ROWCOUNT=0 THROW 50040,N'Güncellenecek ürün bulunamadı.',1;
            """;
        await using var c=new SqlConnection(_connectionString);await c.OpenAsync(token);
        await using var t=(SqlTransaction)await c.BeginTransactionAsync(token);
        try
        {
            await using var x=new SqlCommand(sql,c,t);
            Add(x,"@ProductId",productId);Add(x,"@CompanyId",r.CompanyId);Add(x,"@UserId",r.UserId);Add(x,"@ProductCode",r.ProductCode);
            Add(x,"@Barcode",r.MainBarcode);Add(x,"@ProductName",r.ProductName);Add(x,"@ProductType",r.ProductType);Add(x,"@BaseUnitId",r.BaseUnitId);
            Add(x,"@VatRate",r.VatRate);Add(x,"@PurchasePrice",r.PurchasePrice);Add(x,"@SalesPrice",r.SalesPrice);Add(x,"@MinStock",r.MinimumStock);
            Add(x,"@MaxStock",r.MaximumStock);Add(x,"@TrackLot",r.TrackLot);Add(x,"@TrackSerial",r.TrackSerial);Add(x,"@IsActive",r.IsActive);
            Add(x,"@ShortName",r.ShortName);Add(x,"@Description",r.Description);Add(x,"@BrandId",r.BrandId);Add(x,"@CategoryId",r.CategoryId);
            Add(x,"@ProductGroupId",r.ProductGroupId);Add(x,"@WholesalePrice",r.WholesalePrice);Add(x,"@CampaignPrice",r.CampaignPrice);
            Add(x,"@CriticalStock",r.CriticalStock);Add(x,"@ShelfCode",r.ShelfCode);Add(x,"@AisleCode",r.AisleCode);
            Add(x,"@ManufacturerCode",r.ManufacturerCode);Add(x,"@Origin",r.CountryOfOrigin);Add(x,"@Warranty",r.WarrantyMonths);
            await x.ExecuteNonQueryAsync(token);
            foreach(var v in r.Variants)
            {
                int? color=await EnsureColor(c,t,r.CompanyId,v.ColorCode,v.ColorName,token),size=await EnsureSize(c,t,r.CompanyId,v.SizeCode,v.SizeName,token);
                const string variantSql="""
                    UPDATE inv.ProductVariant SET ColorId=@ColorId,SizeId=@SizeId,MainBarcode=@Barcode,PurchasePrice=@Purchase,
                     SalesPrice=@Sales,WholesalePrice=@Wholesale,CampaignPrice=@Campaign,ShelfCode=@Shelf,AisleCode=@Aisle,
                     TrackLot=@Lot,TrackSerial=@Serial,UpdatedAtUtc=SYSUTCDATETIME()
                    WHERE CompanyId=@CompanyId AND VariantCode=@Code;
                    IF @@ROWCOUNT=0 INSERT inv.ProductVariant(CompanyId,ProductId,VariantCode,ColorId,SizeId,MainBarcode,PurchasePrice,
                     SalesPrice,WholesalePrice,CampaignPrice,ShelfCode,AisleCode,TrackLot,TrackSerial,IsActive)
                     VALUES(@CompanyId,@ProductId,@Code,@ColorId,@SizeId,@Barcode,@Purchase,@Sales,@Wholesale,@Campaign,@Shelf,@Aisle,@Lot,@Serial,1);
                    """;
                await using var q=new SqlCommand(variantSql,c,t);
                Add(q,"@CompanyId",r.CompanyId);Add(q,"@ProductId",productId);Add(q,"@Code",v.VariantCode);Add(q,"@ColorId",color);Add(q,"@SizeId",size);
                Add(q,"@Barcode",v.Barcode);Add(q,"@Purchase",v.PurchasePrice);Add(q,"@Sales",v.SalesPrice);Add(q,"@Wholesale",v.WholesalePrice);
                Add(q,"@Campaign",v.CampaignPrice);Add(q,"@Shelf",v.ShelfCode);Add(q,"@Aisle",v.AisleCode);Add(q,"@Lot",v.TrackLot);Add(q,"@Serial",v.TrackSerial);
                await q.ExecuteNonQueryAsync(token);
            }
            await t.CommitAsync(token);
        }catch{await t.RollbackAsync(token);throw;}
    }

    private async Task UpdateAccountAsync(AccountSaveRequest r,long accountId,CancellationToken token)
    {
        const string sql="""
            UPDATE crm.Account SET AccountCode=@Code,AccountType=@Type,LegalName=@LegalName,TradeName=@TradeName,
             TaxOffice=COALESCE(@TaxOffice,TaxOffice),TaxNumber=COALESCE(@TaxNumber,TaxNumber),IdentityNumber=COALESCE(@Identity,IdentityNumber),
             CurrencyCode=@Currency,CreditLimit=@Credit,PaymentTermDays=@Term,Phone=@Phone,Email=@Email,
             EInvoiceAlias=@Alias,IsEInvoiceUser=@EInvoice,Notes=@Notes,IsActive=@IsActive,MersisNumber=COALESCE(@Mersis,MersisNumber),
             PriceListCode=@PriceList,DiscountGroupCode=@DiscountGroup,PaymentPlanId=@PaymentPlan,UpdatedByUserId=@UserId,UpdatedAtUtc=SYSUTCDATETIME()
            WHERE AccountId=@AccountId AND CompanyId=@CompanyId AND IsDeleted=0;
            IF @@ROWCOUNT=0 THROW 50041,N'Güncellenecek cari bulunamadı.',1;
            IF @Address IS NOT NULL
            BEGIN
              IF EXISTS(SELECT 1 FROM crm.AccountAddress WHERE AccountId=@AccountId AND IsDefault=1)
               UPDATE crm.AccountAddress SET City=@City,District=@District,AddressLine=@Address WHERE AccountId=@AccountId AND IsDefault=1;
              ELSE INSERT crm.AccountAddress(AccountId,AddressType,AddressName,CountryCode,City,District,AddressLine,IsDefault)
               VALUES(@AccountId,1,N'Fatura Adresi','TR',@City,@District,@Address,1);
            END
            """;
        await using var c=new SqlConnection(_connectionString);await c.OpenAsync(token);await using var x=new SqlCommand(sql,c);
        Add(x,"@AccountId",accountId);Add(x,"@CompanyId",r.CompanyId);Add(x,"@UserId",r.UserId);Add(x,"@Code",r.AccountCode);Add(x,"@Type",r.AccountType);
        Add(x,"@LegalName",r.LegalName);Add(x,"@TradeName",r.TradeName);Add(x,"@TaxOffice",r.TaxOffice);Add(x,"@TaxNumber",r.TaxNumber);
        Add(x,"@Identity",r.IdentityNumber);Add(x,"@Currency",r.CurrencyCode);Add(x,"@Credit",r.CreditLimit);Add(x,"@Term",r.PaymentTermDays);
        Add(x,"@Phone",r.Phone);Add(x,"@Email",r.Email);Add(x,"@Alias",r.EInvoiceAlias);Add(x,"@EInvoice",r.IsEInvoiceUser);Add(x,"@Notes",r.Notes);
        Add(x,"@IsActive",r.IsActive);Add(x,"@Mersis",r.MersisNumber);Add(x,"@PriceList",r.PriceListCode);Add(x,"@DiscountGroup",r.DiscountGroupCode);
        Add(x,"@PaymentPlan",r.PaymentPlanId);Add(x,"@City",r.City);Add(x,"@District",r.District);Add(x,"@Address",r.Address);
        await x.ExecuteNonQueryAsync(token);
    }
}
