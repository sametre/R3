using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using R3.Application.Dashboard;

namespace R3.Infrastructure.Dashboard;

internal sealed class SqlDashboardService(IConfiguration configuration):IDashboardService
{
 private readonly string _cs=configuration.GetConnectionString("R3Database")??throw new InvalidOperationException("Bağlantı dizesi bulunamadı.");
 public async Task<DashboardSnapshot> GetSnapshotAsync(int companyId,DateTime from,DateTime endExclusive,CancellationToken ct=default)
 {
  await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);
  const string totals="""
  SELECT
  COALESCE(SUM(CASE WHEN InvoiceStatus=2 AND InvoiceType IN(1,4) THEN GrandTotal-PaidTotal ELSE 0 END),0),
  COALESCE(SUM(CASE WHEN InvoiceStatus=2 AND InvoiceType IN(2,3) THEN GrandTotal-PaidTotal ELSE 0 END),0),
  (SELECT COUNT(*) FROM fin.Cheque WHERE CompanyId=@C AND Status NOT IN(7,8)),
  (SELECT COUNT(*) FROM fin.PromissoryNote WHERE CompanyId=@C AND Status NOT IN(6,7)),
  COALESCE(SUM(CASE WHEN InvoiceStatus=1 THEN 1 ELSE 0 END),0)
  FROM doc.Invoice WHERE CompanyId=@C
  """;
  decimal receivable,payable;int cheques,notes,drafts;await using(var q=new SqlCommand(totals,c)){q.Parameters.Add("@C",SqlDbType.Int).Value=companyId;await using var r=await q.ExecuteReaderAsync(ct);await r.ReadAsync(ct);receivable=r.GetDecimal(0);payable=r.GetDecimal(1);cheques=r.GetInt32(2);notes=r.GetInt32(3);drafts=r.GetInt32(4);}
  const string agenda="""
  SELECT DueDate,N'Ödeme',CONCAT(InvoiceNumber,N' • ',a.LegalName),N'Fatura vadesi',GrandTotal-PaidTotal,CurrencyCode,CAST(0 AS bit)
  FROM doc.Invoice i JOIN crm.Account a ON a.AccountId=i.AccountId
  WHERE i.CompanyId=@C AND i.InvoiceStatus=2 AND i.DueDate>=@From AND i.DueDate<@To AND i.GrandTotal>i.PaidTotal
  UNION ALL SELECT DueDate,N'Çek',CONCAT(ChequeNumber,N' • ',DrawerName),Description,Amount,CurrencyCode,CAST(0 AS bit)
  FROM fin.Cheque WHERE CompanyId=@C AND DueDate>=@From AND DueDate<@To AND Status NOT IN(7,8)
  UNION ALL SELECT DueDate,N'Senet',CONCAT(NoteNumber,N' • ',DebtorName),Description,Amount,CurrencyCode,CAST(0 AS bit)
  FROM fin.PromissoryNote WHERE CompanyId=@C AND DueDate>=@From AND DueDate<@To AND Status NOT IN(6,7)
  UNION ALL SELECT EventDate,CASE EventType WHEN 1 THEN N'Not' WHEN 2 THEN N'Görev' WHEN 3 THEN N'Görüşme' ELSE N'Hatırlatma' END,Title,Description,NULL,NULL,IsCompleted
  FROM core.AgendaEvent WHERE CompanyId=@C AND EventDate>=@From AND EventDate<@To ORDER BY 1
  """;
  var items=new List<AgendaItem>();await using(var q=new SqlCommand(agenda,c)){q.Parameters.Add("@C",SqlDbType.Int).Value=companyId;q.Parameters.Add("@From",SqlDbType.DateTime2).Value=from;q.Parameters.Add("@To",SqlDbType.DateTime2).Value=endExclusive;await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))items.Add(new(r.GetDateTime(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetDecimal(4),r.IsDBNull(5)?null:r.GetString(5),r.GetBoolean(6)));}
  return new(receivable,payable,cheques,notes,drafts,items);
 }
 public async Task AddNoteAsync(int companyId,int branchId,int userId,DateTime eventDate,string title,string? description,CancellationToken ct=default)
 {
  const string sql="INSERT core.AgendaEvent(CompanyId,BranchId,UserId,EventType,Title,EventDate,Description,CreatedByUserId) VALUES(@C,@B,@U,1,@Title,@Date,@Description,@U)";
  await using var c=new SqlConnection(_cs);await c.OpenAsync(ct);await using var q=new SqlCommand(sql,c);q.Parameters.AddWithValue("@C",companyId);q.Parameters.AddWithValue("@B",branchId);q.Parameters.AddWithValue("@U",userId);q.Parameters.AddWithValue("@Title",title);q.Parameters.AddWithValue("@Date",eventDate);q.Parameters.AddWithValue("@Description",(object?)description??DBNull.Value);await q.ExecuteNonQueryAsync(ct);
 }
}
