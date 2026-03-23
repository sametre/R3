namespace R3.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(int companyId,DateTime from,DateTime endExclusive,CancellationToken cancellationToken=default);
    Task AddNoteAsync(int companyId,int branchId,int userId,DateTime eventDate,string title,string? description,CancellationToken cancellationToken=default);
}

public sealed record AgendaItem(DateTime Date,string Type,string Title,string? Detail,decimal? Amount,string? CurrencyCode,bool Completed);
public sealed record DashboardSnapshot(decimal ReceivablesDue,decimal PayablesDue,int OpenChequeCount,int OpenNoteCount,int DraftInvoiceCount,IReadOnlyList<AgendaItem> Agenda);
