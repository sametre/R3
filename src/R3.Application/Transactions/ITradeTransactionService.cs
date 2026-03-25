namespace R3.Application.Transactions;

public interface ITradeTransactionService
{
    Task<IReadOnlyList<InvoiceListItem>> GetInvoicesAsync(int companyId, CancellationToken cancellationToken = default);
    Task<InvoiceDetail?> GetInvoiceDetailAsync(long invoiceId, int companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinanceListItem>> GetFinanceTransactionsAsync(int companyId, CancellationToken cancellationToken = default);
    Task<TradeLookups> GetLookupsAsync(int companyId, int branchId, CancellationToken cancellationToken = default);
    Task<long> SaveInvoiceAsync(InvoiceSaveRequest request, CancellationToken cancellationToken = default);
    Task<long> SaveFinanceAsync(FinanceSaveRequest request, CancellationToken cancellationToken = default);
    Task PostInvoiceAsync(long invoiceId, int userId, CancellationToken cancellationToken = default);
    Task CancelInvoiceAsync(long invoiceId, int userId, CancellationToken cancellationToken = default);
    Task ReverseInvoiceAsync(long invoiceId, int userId, string reason, CancellationToken cancellationToken = default);
    Task PostFinanceAsync(long financialTransactionId, int userId, CancellationToken cancellationToken = default);
    Task CancelFinanceAsync(long financialTransactionId, int userId, CancellationToken cancellationToken = default);
    Task ReverseFinanceAsync(long financialTransactionId, int userId, string reason, CancellationToken cancellationToken = default);
    Task AllocatePaymentAsync(long financialTransactionId, long invoiceId, decimal amount, int userId, CancellationToken cancellationToken = default);
}
public sealed record TradeLookup(long Id,string Code,string Name,string? Detail=null){public string Display=>$"{Code} • {Name}";}
public sealed record TradeLookups(IReadOnlyList<TradeLookup> Accounts,IReadOnlyList<TradeLookup> Products,IReadOnlyList<TradeLookup> Warehouses,IReadOnlyList<TradeLookup> CashAccounts,IReadOnlyList<TradeLookup> BankAccounts,int FiscalPeriodId);
public sealed record InvoiceListItem(long InvoiceId,string InvoiceNumber,string? DocumentNumber,string? DispatchNumber,DateTime InvoiceDate,string InvoiceType,string AccountCode,string AccountName,string? TaxNumber,decimal GrandTotal,string CurrencyCode,DateTime DueDate,decimal PaidTotal,string Status)
{
    public decimal RemainingTotal => GrandTotal - PaidTotal;
}
public sealed record InvoiceLineDetail(long ProductId,long? VariantId,int UnitId,int? WarehouseId,decimal Quantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,string? Description);
public sealed record InvoiceDetail(long InvoiceId,byte InvoiceType,string InvoiceNumber,string? DocumentNumber,string? DispatchNumber,DateTime? DispatchDate,DateTime InvoiceDate,DateTime DueDate,long AccountId,string CurrencyCode,decimal ExchangeRate,string? Description,byte Status,IReadOnlyList<InvoiceLineDetail> Lines);
public sealed record InvoiceLineSaveRequest(long ProductId,long? VariantId,int UnitId,int? WarehouseId,decimal Quantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,string? Description);
public sealed record InvoiceSaveRequest(long? InvoiceId,int CompanyId,int BranchId,int FiscalPeriodId,int UserId,byte InvoiceType,string InvoiceNumber,string? DocumentNumber,string? DispatchNumber,DateTime? DispatchDate,DateTime InvoiceDate,DateTime DueDate,long AccountId,string CurrencyCode,decimal ExchangeRate,string? Description,IReadOnlyList<InvoiceLineSaveRequest> Lines);
public sealed record FinanceListItem(long FinancialTransactionId,string DocumentNumber,DateTime TransactionDate,string TransactionType,string? AccountCode,string? AccountName,string FinancialAccount,decimal Amount,string CurrencyCode,string? ReferenceNumber,string Status);
public sealed record FinanceSaveRequest(long? FinancialTransactionId,int CompanyId,int BranchId,int FiscalPeriodId,int UserId,byte TransactionType,string DocumentNumber,DateTime TransactionDate,long? AccountId,int? CashAccountId,int? BankAccountId,string CurrencyCode,decimal ExchangeRate,decimal Amount,string? ReferenceNumber,string? Description);
