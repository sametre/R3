using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using R3.Application.Transactions;

namespace R3.Infrastructure.Transactions;

internal sealed class SqlTradeTransactionService(IConfiguration configuration):ITradeTransactionService
{
 private readonly string _cs=configuration.GetConnectionString("R3Database")??throw new InvalidOperationException("Bağlantı dizesi bulunamadı.");
 public async Task<IReadOnlyList<InvoiceListItem>> GetInvoicesAsync(int companyId,CancellationToken ct=default)
 {
  const string sql="""SELECT i.InvoiceId,i.InvoiceNumber,i.DocumentNumber,i.DispatchNumber,i.InvoiceDate,CASE i.InvoiceType WHEN 1 THEN N'Satış' WHEN 2 THEN N'Satış İade' WHEN 3 THEN N'Alış' ELSE N'Alış İade' END,a.AccountCode,a.LegalName,COALESCE(a.TaxNumber,a.IdentityNumber),i.GrandTotal,i.CurrencyCode,i.DueDate,i.PaidTotal,CASE i.InvoiceStatus WHEN 1 THEN N'Taslak' WHEN 2 THEN N'Onaylı' ELSE N'İptal' END FROM doc.Invoice i JOIN crm.Account a ON a.AccountId=i.AccountId WHERE i.CompanyId=@C ORDER BY i.InvoiceDate DESC,i.InvoiceId DESC""";
  var list=new List<InvoiceListItem>();await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);await using var q=new SqlCommand(sql,c);q.Parameters.Add("@C",SqlDbType.Int).Value=companyId;await using var r=await q.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new(r.GetInt64(0),r.GetString(1),S(r,2),S(r,3),r.GetDateTime(4),r.GetString(5),r.GetString(6),r.GetString(7),S(r,8),r.GetDecimal(9),r.GetString(10),r.GetDateTime(11),r.GetDecimal(12),r.GetString(13)));return list;
 }
 public async Task<IReadOnlyList<FinanceListItem>> GetFinanceTransactionsAsync(int companyId,CancellationToken ct=default)
 {
  const string sql="""SELECT f.FinancialTransactionId,f.DocumentNumber,f.TransactionDate,CASE f.TransactionType WHEN 1 THEN N'Tahsilat' WHEN 2 THEN N'Ödeme' WHEN 3 THEN N'Kasa Giriş' WHEN 4 THEN N'Kasa Çıkış' WHEN 5 THEN N'Banka Giriş' ELSE N'Banka Çıkış' END,a.AccountCode,a.LegalName,COALESCE(ca.CashName,ba.BankName),f.Amount,f.CurrencyCode,f.ReferenceNumber,CASE f.Status WHEN 1 THEN N'Taslak' WHEN 2 THEN N'Onaylı' ELSE N'İptal' END FROM fin.FinancialTransaction f LEFT JOIN crm.Account a ON a.AccountId=f.AccountId LEFT JOIN fin.CashAccount ca ON ca.CashAccountId=f.CashAccountId LEFT JOIN fin.BankAccount ba ON ba.BankAccountId=f.BankAccountId WHERE f.CompanyId=@C ORDER BY f.TransactionDate DESC,f.FinancialTransactionId DESC""";
  var list=new List<FinanceListItem>();await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);await using var q=new SqlCommand(sql,c);q.Parameters.Add("@C",SqlDbType.Int).Value=companyId;await using var r=await q.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new(r.GetInt64(0),r.GetString(1),r.GetDateTime(2),r.GetString(3),S(r,4),S(r,5),r.GetString(6),r.GetDecimal(7),r.GetString(8),S(r,9),r.GetString(10)));return list;
 }
 public async Task<InvoiceDetail?> GetInvoiceDetailAsync(long invoiceId,int companyId,CancellationToken ct=default)
 {
  const string header="""SELECT InvoiceId,InvoiceType,InvoiceNumber,DocumentNumber,DispatchNumber,DispatchDate,InvoiceDate,DueDate,AccountId,CurrencyCode,ExchangeRate,Description,InvoiceStatus FROM doc.Invoice WHERE InvoiceId=@Id AND CompanyId=@C""";
  await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);await using var q=new SqlCommand(header,c);A(q,"@Id",invoiceId);A(q,"@C",companyId);
  long id;byte type,status;string number,currency;DateTime date,due;long account;decimal rate;string? document,dispatch,description;DateTime? dispatchDate;
  await using(var r=await q.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct))return null;id=r.GetInt64(0);type=r.GetByte(1);number=r.GetString(2);document=S(r,3);dispatch=S(r,4);dispatchDate=r.IsDBNull(5)?null:r.GetDateTime(5);date=r.GetDateTime(6);due=r.GetDateTime(7);account=r.GetInt64(8);currency=r.GetString(9);rate=r.GetDecimal(10);description=S(r,11);status=r.GetByte(12);}
  const string lines="""SELECT ProductId,ProductVariantId,UnitId,WarehouseId,Quantity,UnitPrice,DiscountRate,TaxRate,Description FROM doc.InvoiceLine WHERE InvoiceId=@Id ORDER BY LineNumber""";
  var items=new List<InvoiceLineDetail>();await using var lq=new SqlCommand(lines,c);A(lq,"@Id",invoiceId);await using var lr=await lq.ExecuteReaderAsync(ct);
  while(await lr.ReadAsync(ct))items.Add(new(lr.GetInt64(0),lr.IsDBNull(1)?null:lr.GetInt64(1),lr.GetInt32(2),lr.IsDBNull(3)?null:lr.GetInt32(3),lr.GetDecimal(4),lr.GetDecimal(5),lr.GetDecimal(6),lr.GetDecimal(7),S(lr,8)));
  return new(id,type,number,document,dispatch,dispatchDate,date,due,account,currency,rate,description,status,items);
 }
 public async Task<TradeLookups> GetLookupsAsync(int companyId,int branchId,CancellationToken ct=default)
 {
  await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);
  var accounts=await Load(c,"SELECT AccountId,AccountCode,LegalName,COALESCE(TaxNumber,IdentityNumber) FROM crm.Account WHERE CompanyId=@C AND IsDeleted=0 AND IsActive=1 ORDER BY LegalName",companyId,ct);
  var products=await Load(c,"SELECT ProductId,ProductCode,ProductName FROM inv.Product WHERE CompanyId=@C AND IsDeleted=0 AND IsActive=1 ORDER BY ProductName",companyId,ct);
  var warehouses=await Load(c,"SELECT WarehouseId,WarehouseCode,WarehouseName FROM inv.Warehouse WHERE CompanyId=@C AND IsActive=1 ORDER BY WarehouseName",companyId,ct);
  var cash=await Load(c,"SELECT CashAccountId,CashCode,CashName FROM fin.CashAccount WHERE CompanyId=@C AND IsActive=1 ORDER BY CashName",companyId,ct);
  var banks=await Load(c,"SELECT BankAccountId,AccountCode,BankName FROM fin.BankAccount WHERE CompanyId=@C AND IsActive=1 ORDER BY BankName",companyId,ct);
  await using var q=new SqlCommand("SELECT TOP(1) FiscalPeriodId FROM core.FiscalPeriod WHERE CompanyId=@C AND IsClosed=0 ORDER BY StartDate DESC",c);q.Parameters.Add("@C",SqlDbType.Int).Value=companyId;
  int period=Convert.ToInt32(await q.ExecuteScalarAsync(ct),System.Globalization.CultureInfo.InvariantCulture);return new(accounts,products,warehouses,cash,banks,period);
 }
 public async Task<long> SaveInvoiceAsync(InvoiceSaveRequest x,CancellationToken ct=default)
 {
  if(x.InvoiceId is null&&x.Lines.Count==0)throw new InvalidOperationException("Faturada en az bir satır olmalıdır.");
  if(x.InvoiceId is not null&&x.Lines.Count==0)
  {
   const string update="""UPDATE doc.Invoice SET InvoiceType=@T,InvoiceNumber=@N,DocumentNumber=@Doc,DispatchNumber=@Dispatch,DispatchDate=@DispatchDate,InvoiceDate=@D,DueDate=@Due,AccountId=@A,CurrencyCode=@Cur,ExchangeRate=@Rate,Description=@Desc,UpdatedByUserId=@U,UpdatedAtUtc=SYSUTCDATETIME() WHERE InvoiceId=@Id AND CompanyId=@C AND InvoiceStatus=1;IF @@ROWCOUNT=0 THROW 50050,N'Yalnızca taslak fatura güncellenebilir.',1;""";
   await using var uc=new SqlConnection(_cs);await uc.OpenAsync(ct);await using var uq=new SqlCommand(update,uc);
   A(uq,"@T",x.InvoiceType);A(uq,"@N",x.InvoiceNumber);A(uq,"@Doc",x.DocumentNumber);A(uq,"@Dispatch",x.DispatchNumber);A(uq,"@DispatchDate",x.DispatchDate);A(uq,"@D",x.InvoiceDate.Date);A(uq,"@Due",x.DueDate.Date);A(uq,"@A",x.AccountId);A(uq,"@Cur",x.CurrencyCode);A(uq,"@Rate",x.ExchangeRate);A(uq,"@Desc",x.Description);A(uq,"@U",x.UserId);A(uq,"@Id",x.InvoiceId);A(uq,"@C",x.CompanyId);await uq.ExecuteNonQueryAsync(ct);return x.InvoiceId.Value;
  }
  decimal sub=x.Lines.Sum(l=>l.Quantity*l.UnitPrice),disc=x.Lines.Sum(l=>l.Quantity*l.UnitPrice*l.DiscountRate/100),tax=x.Lines.Sum(l=>(l.Quantity*l.UnitPrice*(1-l.DiscountRate/100))*l.VatRate/100),grand=sub-disc+tax;
  await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);await using var t=(SqlTransaction)await c.BeginTransactionAsync(ct);
  try{
   long id;
   if(x.InvoiceId is null){const string h="""INSERT doc.Invoice(CompanyId,BranchId,FiscalPeriodId,InvoiceType,InvoiceStatus,InvoiceNumber,DocumentNumber,DispatchNumber,DispatchDate,InvoiceDate,DueDate,AccountId,CurrencyCode,ExchangeRate,Subtotal,DiscountTotal,TaxTotal,GrandTotal,LocalGrandTotal,PaidTotal,Description,CreatedByUserId) VALUES(@C,@B,@F,@T,1,@N,@Doc,@Dispatch,@DispatchDate,@D,@Due,@A,@Cur,@Rate,@Sub,@Disc,@Tax,@Grand,@Local,0,@Desc,@U);SELECT CAST(SCOPE_IDENTITY() AS bigint)""";await using var q=new SqlCommand(h,c,t);P(q,x,sub,disc,tax,grand);id=(long)(await q.ExecuteScalarAsync(ct))!;}
   else{id=x.InvoiceId.Value;const string h="""UPDATE doc.Invoice SET InvoiceType=@T,InvoiceNumber=@N,DocumentNumber=@Doc,DispatchNumber=@Dispatch,DispatchDate=@DispatchDate,InvoiceDate=@D,DueDate=@Due,AccountId=@A,CurrencyCode=@Cur,ExchangeRate=@Rate,Subtotal=@Sub,DiscountTotal=@Disc,TaxTotal=@Tax,GrandTotal=@Grand,LocalGrandTotal=@Local,Description=@Desc,UpdatedByUserId=@U,UpdatedAtUtc=SYSUTCDATETIME() WHERE InvoiceId=@Id AND CompanyId=@C AND InvoiceStatus=1;IF @@ROWCOUNT=0 THROW 50050,N'Yalnızca taslak fatura güncellenebilir.',1;DELETE doc.InvoiceLine WHERE InvoiceId=@Id""";await using var q=new SqlCommand(h,c,t);P(q,x,sub,disc,tax,grand);q.Parameters.AddWithValue("@Id",id);await q.ExecuteNonQueryAsync(ct);}
   int n=0;foreach(var l in x.Lines){n++;decimal gross=l.Quantity*l.UnitPrice,da=gross*l.DiscountRate/100,net=gross-da,ta=net*l.VatRate/100,total=net+ta;const string line="""INSERT doc.InvoiceLine(InvoiceId,LineNumber,ProductId,ProductVariantId,WarehouseId,UnitId,Description,Quantity,UnitFactor,UnitPrice,DiscountRate,DiscountAmount,TaxRate,TaxAmount,LineNet,LineTotal,DiscountRate2,DiscountRate3,ExciseTaxAmount,WithholdingRate,LineExpense) SELECT @I,@N,@P,@V,@W,BaseUnitId,@Desc,@Q,1,@Price,@DR,@DA,@VR,@TA,@Net,@Total,0,0,0,0,0 FROM inv.Product WHERE ProductId=@P""";await using var q=new SqlCommand(line,c,t);q.Parameters.AddWithValue("@I",id);q.Parameters.AddWithValue("@N",n);q.Parameters.AddWithValue("@P",l.ProductId);q.Parameters.AddWithValue("@V",(object?)l.VariantId??DBNull.Value);q.Parameters.AddWithValue("@W",(object?)l.WarehouseId??DBNull.Value);q.Parameters.AddWithValue("@Desc",(object?)l.Description??DBNull.Value);q.Parameters.AddWithValue("@Q",l.Quantity);q.Parameters.AddWithValue("@Price",l.UnitPrice);q.Parameters.AddWithValue("@DR",l.DiscountRate);q.Parameters.AddWithValue("@DA",da);q.Parameters.AddWithValue("@VR",l.VatRate);q.Parameters.AddWithValue("@TA",ta);q.Parameters.AddWithValue("@Net",net);q.Parameters.AddWithValue("@Total",total);await q.ExecuteNonQueryAsync(ct);}
   await t.CommitAsync(ct);return id;
  }catch{await t.RollbackAsync(ct);throw;}
 }
 public async Task<long> SaveFinanceAsync(FinanceSaveRequest x,CancellationToken ct=default)
 {
  decimal local=x.Amount*x.ExchangeRate;await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);
  string sql=x.FinancialTransactionId is null?"""INSERT fin.FinancialTransaction(CompanyId,BranchId,FiscalPeriodId,TransactionType,Status,DocumentNumber,TransactionDate,AccountId,CashAccountId,BankAccountId,CurrencyCode,ExchangeRate,Amount,LocalAmount,Description,ReferenceNumber,CreatedByUserId) VALUES(@C,@B,@F,@T,1,@N,@D,@A,@Cash,@Bank,@Cur,@Rate,@Amount,@Local,@Desc,@Ref,@U);SELECT CAST(SCOPE_IDENTITY() AS bigint)""":"""UPDATE fin.FinancialTransaction SET TransactionType=@T,DocumentNumber=@N,TransactionDate=@D,AccountId=@A,CashAccountId=@Cash,BankAccountId=@Bank,CurrencyCode=@Cur,ExchangeRate=@Rate,Amount=@Amount,LocalAmount=@Local,Description=@Desc,ReferenceNumber=@Ref WHERE FinancialTransactionId=@Id AND CompanyId=@C AND Status=1;IF @@ROWCOUNT=0 THROW 50051,N'Yalnızca taslak finans işlemi güncellenebilir.',1;SELECT @Id""";
  await using var q=new SqlCommand(sql,c);A(q,"@C",x.CompanyId);A(q,"@B",x.BranchId);A(q,"@F",x.FiscalPeriodId);A(q,"@T",x.TransactionType);A(q,"@N",x.DocumentNumber);A(q,"@D",x.TransactionDate);A(q,"@A",x.AccountId);A(q,"@Cash",x.CashAccountId);A(q,"@Bank",x.BankAccountId);A(q,"@Cur",x.CurrencyCode);A(q,"@Rate",x.ExchangeRate);A(q,"@Amount",x.Amount);A(q,"@Local",local);A(q,"@Desc",x.Description);A(q,"@Ref",x.ReferenceNumber);A(q,"@U",x.UserId);A(q,"@Id",x.FinancialTransactionId);return (long)(await q.ExecuteScalarAsync(ct))!;
 }
 public Task PostInvoiceAsync(long id,int userId,CancellationToken ct=default)=>ExecuteProcedure("doc.usp_PostInvoice","@InvoiceId",id,userId,ct);
 public Task CancelInvoiceAsync(long id,int userId,CancellationToken ct=default)=>ExecuteProcedure("doc.usp_CancelDraftInvoice","@InvoiceId",id,userId,ct);
 public Task ReverseInvoiceAsync(long id,int userId,string reason,CancellationToken ct=default)=>ExecuteReasonProcedure("doc.usp_ReverseInvoice","@InvoiceId",id,userId,reason,ct);
 public Task PostFinanceAsync(long id,int userId,CancellationToken ct=default)=>ExecuteProcedure("fin.usp_PostFinancialTransaction","@FinancialTransactionId",id,userId,ct);
 public Task CancelFinanceAsync(long id,int userId,CancellationToken ct=default)=>ExecuteProcedure("fin.usp_CancelDraftFinancialTransaction","@FinancialTransactionId",id,userId,ct);
 public Task ReverseFinanceAsync(long id,int userId,string reason,CancellationToken ct=default)=>ExecuteReasonProcedure("fin.usp_ReverseFinancialTransaction","@FinancialTransactionId",id,userId,reason,ct);
 public async Task AllocatePaymentAsync(long financialTransactionId,long invoiceId,decimal amount,int userId,CancellationToken ct=default)
 {
  await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);await using var q=new SqlCommand("fin.usp_AllocatePayment",c){CommandType=CommandType.StoredProcedure};
  A(q,"@FinancialTransactionId",financialTransactionId);A(q,"@InvoiceId",invoiceId);A(q,"@Amount",amount);A(q,"@UserId",userId);await q.ExecuteNonQueryAsync(ct);
 }
 private async Task ExecuteProcedure(string procedure,string idName,long id,int userId,CancellationToken ct){await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);await using var q=new SqlCommand(procedure,c){CommandType=CommandType.StoredProcedure};q.Parameters.Add(idName,SqlDbType.BigInt).Value=id;q.Parameters.Add("@UserId",SqlDbType.Int).Value=userId;await q.ExecuteNonQueryAsync(ct);}
 private async Task ExecuteReasonProcedure(string procedure,string idName,long id,int userId,string reason,CancellationToken ct){await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);await using var q=new SqlCommand(procedure,c){CommandType=CommandType.StoredProcedure};A(q,idName,id);A(q,"@UserId",userId);A(q,"@Reason",reason);await q.ExecuteNonQueryAsync(ct);}
 private static async Task<IReadOnlyList<TradeLookup>> Load(SqlConnection c,string sql,int company,CancellationToken ct){var l=new List<TradeLookup>();await using var q=new SqlCommand(sql,c);q.Parameters.Add("@C",SqlDbType.Int).Value=company;await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))l.Add(new(Convert.ToInt64(r.GetValue(0),System.Globalization.CultureInfo.InvariantCulture),r.GetString(1),r.GetString(2),r.FieldCount>3&&!r.IsDBNull(3)?r.GetString(3):null));return l;}
 private static void P(SqlCommand q,InvoiceSaveRequest x,decimal s,decimal d,decimal tax,decimal g){A(q,"@C",x.CompanyId);A(q,"@B",x.BranchId);A(q,"@F",x.FiscalPeriodId);A(q,"@T",x.InvoiceType);A(q,"@N",x.InvoiceNumber);A(q,"@Doc",x.DocumentNumber);A(q,"@Dispatch",x.DispatchNumber);A(q,"@DispatchDate",x.DispatchDate);A(q,"@D",x.InvoiceDate.Date);A(q,"@Due",x.DueDate.Date);A(q,"@A",x.AccountId);A(q,"@Cur",x.CurrencyCode);A(q,"@Rate",x.ExchangeRate);A(q,"@Sub",s);A(q,"@Disc",d);A(q,"@Tax",tax);A(q,"@Grand",g);A(q,"@Local",g*x.ExchangeRate);A(q,"@Desc",x.Description);A(q,"@U",x.UserId);}
 private static string? S(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);private static void A(SqlCommand q,string n,object? v)=>q.Parameters.AddWithValue(n,v??DBNull.Value);
}
