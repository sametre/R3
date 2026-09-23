using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

/// <summary>Posts a customer collection and marks the paid portions of its payment-plan lines in one transaction.</summary>
public sealed class LocalInstallmentCollectionService(StoreDatabase database)
{
    public void Collect(string companyId, string branchId, string accountId, string currency, DateTime date,
        IReadOnlyDictionary<string, decimal> amounts, string paymentMethod, string description)
    {
        var selections = amounts.Where(x => x.Value > 0).ToArray();
        if (selections.Length == 0) throw new ArgumentException("Tahsil edilecek en az bir taksit seçin.");
        if (paymentMethod is not ("Cash" or "BankTransfer" or "CreditCard" or "Cheque" or "PromissoryNote")) throw new ArgumentException("Geçersiz ödeme yöntemi.");
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var total = 0m;
        foreach (var (lineId, amount) in selections)
        {
            using var read = connection.CreateCommand(); read.Transaction = transaction;
            read.CommandText = """
                SELECT l.amount,l.paid_amount,p.account_id,p.company_id,p.currency_code
                FROM payment_plan_lines l JOIN payment_plans p ON p.id=l.payment_plan_id
                WHERE l.id=$id AND l.status<>'Cancelled'
                """;
            read.Parameters.AddWithValue("$id", lineId);
            using var reader = read.ExecuteReader();
            if (!reader.Read()) throw new ArgumentException("Seçilen taksit bulunamadı veya iptal edilmiş.");
            if (reader.GetString(2) != accountId || reader.GetString(3) != companyId || reader.GetString(4) != currency)
                throw new ArgumentException("Seçilen taksit bu müşteri veya döviz için geçerli değil.");
            var remaining = reader.GetDecimal(0) - reader.GetDecimal(1);
            if (amount > remaining) throw new ArgumentException("Tahsilat tutarı kalan taksit tutarını aşamaz.");
            using var update = connection.CreateCommand(); update.Transaction = transaction;
            update.CommandText = """
                UPDATE payment_plan_lines SET paid_amount=paid_amount+$amount,
                    status=CASE WHEN paid_amount+$amount>=amount THEN 'Paid' ELSE 'Open' END,
                    paid_at=CASE WHEN paid_amount+$amount>=amount THEN $date ELSE paid_at END
                WHERE id=$id
                """;
            update.Parameters.AddWithValue("$amount", amount); update.Parameters.AddWithValue("$date", date.ToString("O")); update.Parameters.AddWithValue("$id", lineId);
            update.ExecuteNonQuery(); total += amount;
        }
        using (var closePlans = connection.CreateCommand())
        {
            closePlans.Transaction = transaction;
            closePlans.CommandText = """
                UPDATE payment_plans SET status='Closed'
                WHERE company_id=$company AND account_id=$account AND currency_code=$currency AND status='Open'
                  AND NOT EXISTS(SELECT 1 FROM payment_plan_lines l WHERE l.payment_plan_id=payment_plans.id AND l.status<>'Paid')
                """;
            closePlans.Parameters.AddWithValue("$company", companyId); closePlans.Parameters.AddWithValue("$account", accountId); closePlans.Parameters.AddWithValue("$currency", currency); closePlans.ExecuteNonQuery();
        }
        var now = DateTime.UtcNow.ToString("O"); var documentNo = $"TKS-{date:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        using (var ledger = connection.CreateCommand())
        {
            ledger.Transaction = transaction; ledger.CommandText = """
                INSERT INTO account_transactions(id,company_id,branch_id,account_id,transaction_type,debit,credit,currency_code,exchange_rate,document_type,document_no,description,transaction_at,created_at)
                VALUES($id,$company,$branch,$account,'Receipt',0,$amount,$currency,1,'InstallmentCollection',$number,$description,$date,$now)
                """;
            ledger.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); ledger.Parameters.AddWithValue("$company", companyId); ledger.Parameters.AddWithValue("$branch", branchId);
            ledger.Parameters.AddWithValue("$account", accountId); ledger.Parameters.AddWithValue("$amount", total); ledger.Parameters.AddWithValue("$currency", currency);
            ledger.Parameters.AddWithValue("$number", documentNo); ledger.Parameters.AddWithValue("$description", string.IsNullOrWhiteSpace(description) ? "Taksit tahsilatı" : description.Trim()); ledger.Parameters.AddWithValue("$date", date.ToString("O")); ledger.Parameters.AddWithValue("$now", now); ledger.ExecuteNonQuery();
        }
        using (var balance = connection.CreateCommand())
        {
            balance.Transaction = transaction; balance.CommandText = """
                INSERT INTO account_balances(company_id,account_id,debit,credit,balance,updated_at) VALUES($company,$account,0,$amount,-$amount,$now)
                ON CONFLICT(company_id,account_id) DO UPDATE SET credit=credit+$amount,balance=balance-$amount,updated_at=$now
                """;
            balance.Parameters.AddWithValue("$company", companyId); balance.Parameters.AddWithValue("$account", accountId); balance.Parameters.AddWithValue("$amount", total); balance.Parameters.AddWithValue("$now", now); balance.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
