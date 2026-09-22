using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record ChequeEdit(
    string Id, string CompanyId, string BranchId, string InstrumentType, string Direction, string? AccountId,
    decimal Amount, string CurrencyCode, DateTime DueDate, DateTime? IssueDate, string ChequeNumber, string DrawerName,
    string BankName, string BankBranchName, string BankAccountNumber, string Description = "");

/// <summary>
/// Çek/Senet (checks + promissory notes) portfolio engine. One instrument moves through a small
/// forward-only status machine instead of free-text status, mirroring how inventory_documents/
/// electronic_documents model lifecycle elsewhere in this codebase:
///
///   Received: Portfolio -&gt; DepositedForCollection -&gt; Collected | Bounced
///                        -&gt; Endorsed | ReturnedToDrawer   (directly from Portfolio)
///   Given:    Portfolio -&gt; Paid | ReturnedToDrawer
///
/// Money only moves on Collect/CollectToCash (received) and Pay (given) - depositing a received
/// cheque for collection does not touch cash_transactions/bank_transactions yet, since the funds
/// have not actually cleared. Every transition is logged to cheque_status_history so "who moved
/// this and when" is answerable the same way audit_logs answers it for other entities.
/// </summary>
public sealed class LocalChequeService(StoreDatabase database)
{
    private static readonly string[] InstrumentTypes = ["Cheque", "PromissoryNote"];
    private static readonly string[] Directions = ["Received", "Given"];

    public StoreDatabase Database => database;

    public DataTable Search(string companyId, string? branchId = null, string? direction = null, string? status = null,
        string? instrumentType = null, string? search = null, DateTime? dueFrom = null, DateTime? dueTo = null)
    {
        var q = $"%{search?.Trim() ?? ""}%";
        return database.Query("""
            SELECT c.id AS Id, c.instrument_type AS Tur, c.direction AS Yon, c.status AS Durum,
                   COALESCE(a.code || ' — ' || a.name,'') AS Cari, c.amount AS Tutar, c.currency_code AS Doviz,
                   c.due_date AS VadeTarihi, c.issue_date AS DuzenlemeTarihi, c.cheque_number AS BelgeNo,
                   c.drawer_name AS Kesideci, c.bank_name AS Banka, c.bank_branch_name AS BankaSubesi,
                   c.bank_account_number AS HesapNo, c.endorser AS Ciranta, c.settlement_date AS KapanisTarihi,
                   c.description AS Aciklama
            FROM cheques c
            LEFT JOIN accounts a ON a.id=c.account_id
            WHERE c.company_id=$company AND ($branch='' OR c.branch_id=$branch)
              AND ($direction='' OR c.direction=$direction) AND ($status='' OR c.status=$status) AND ($type='' OR c.instrument_type=$type)
              AND ($q='' OR c.cheque_number LIKE $q OR c.drawer_name LIKE $q OR a.code LIKE $q OR a.name LIKE $q)
              AND ($dueFrom='' OR c.due_date>=$dueFrom) AND ($dueTo='' OR c.due_date<=$dueTo)
            ORDER BY c.due_date, c.created_at
            """, ("$company", companyId), ("$branch", branchId ?? ""), ("$direction", direction ?? ""), ("$status", status ?? ""),
            ("$type", instrumentType ?? ""), ("$q", q), ("$dueFrom", dueFrom?.ToString("O") ?? ""), ("$dueTo", dueTo?.ToString("O") ?? ""));
    }

    public (int Portfolio, int Deposited, int Overdue, decimal PortfolioAmount) Summary(string companyId)
    {
        var t = database.Query("""
            SELECT
              SUM(CASE WHEN status='Portfolio' THEN 1 ELSE 0 END) AS Portfolio,
              SUM(CASE WHEN status='DepositedForCollection' THEN 1 ELSE 0 END) AS Deposited,
              SUM(CASE WHEN status IN ('Portfolio','DepositedForCollection') AND due_date<$today THEN 1 ELSE 0 END) AS Overdue,
              COALESCE(SUM(CASE WHEN status IN ('Portfolio','DepositedForCollection') THEN amount ELSE 0 END),0) AS PortfolioAmount
            FROM cheques WHERE company_id=$company
            """, ("$company", companyId), ("$today", DateTime.UtcNow.ToString("O"))).Rows[0];
        return (Convert.ToInt32(t["Portfolio"]), Convert.ToInt32(t["Deposited"]), Convert.ToInt32(t["Overdue"]), Convert.ToDecimal(t["PortfolioAmount"]));
    }

    public string Receive(ChequeEdit edit, string userName) => Create(edit with { Direction = "Received" }, userName, "ChequeReceived");
    public string Give(ChequeEdit edit, string userName) => Create(edit with { Direction = "Given" }, userName, "ChequeGiven");

    private string Create(ChequeEdit edit, string userName, string auditAction)
    {
        if (!InstrumentTypes.Contains(edit.InstrumentType)) throw new ArgumentException("Geçersiz enstrüman tipi (Çek/Senet).");
        if (!Directions.Contains(edit.Direction)) throw new ArgumentException("Geçersiz yön (Alınan/Verilen).");
        if (edit.Amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalıdır.");
        if (string.IsNullOrWhiteSpace(edit.CurrencyCode)) throw new ArgumentException("Para birimi zorunludur.");
        if (string.IsNullOrWhiteSpace(edit.DrawerName)) throw new ArgumentException("Keşideci/borçlu adı zorunludur.");

        var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cheques(id,company_id,branch_id,instrument_type,direction,status,account_id,amount,currency_code,due_date,issue_date,
                    cheque_number,drawer_name,bank_name,bank_branch_name,bank_account_number,description,created_at,created_by,updated_at,updated_by)
                VALUES($id,$company,$branch,$type,$direction,'Portfolio',$account,$amount,$currency,$due,$issue,$number,$drawer,$bank,$bankBranch,$bankAccount,$description,$now,$user,$now,$user)
                """;
            Add(cmd, "$id", id); Add(cmd, "$company", edit.CompanyId); Add(cmd, "$branch", edit.BranchId); Add(cmd, "$type", edit.InstrumentType);
            Add(cmd, "$direction", edit.Direction); AddNullable(cmd, "$account", edit.AccountId); Add(cmd, "$amount", edit.Amount); Add(cmd, "$currency", edit.CurrencyCode);
            Add(cmd, "$due", edit.DueDate.ToString("O")); AddNullable(cmd, "$issue", edit.IssueDate?.ToString("O"));
            Add(cmd, "$number", edit.ChequeNumber.Trim()); Add(cmd, "$drawer", edit.DrawerName.Trim()); Add(cmd, "$bank", edit.BankName.Trim());
            Add(cmd, "$bankBranch", edit.BankBranchName.Trim()); Add(cmd, "$bankAccount", edit.BankAccountNumber.Trim()); Add(cmd, "$description", edit.Description);
            Add(cmd, "$now", now); Add(cmd, "$user", userName);
            cmd.ExecuteNonQuery();
        }
        LogHistory(c, tx, id, "", "Portfolio", userName, now, "");
        Audit(c, tx, edit.CompanyId, "Cheque", id, auditAction, $"{edit.ChequeNumber} — {edit.Amount:N2} {edit.CurrencyCode}", now);
        tx.Commit();
        return id;
    }

    /// <summary>Received only: portföyden bankaya tahsile verildi. No cash/bank posting yet - the
    /// funds have not cleared, this only records where the instrument physically is.</summary>
    public void DepositForCollection(string companyId, string chequeId, string bankAccountId, string userName)
        => Transition(companyId, chequeId, "Received", ["Portfolio"], "DepositedForCollection", userName, cmd =>
        {
            cmd.CommandText = "UPDATE cheques SET status='DepositedForCollection', bank_account_id=$bank, updated_at=$now, updated_by=$user WHERE id=$id";
            Add(cmd, "$bank", bankAccountId);
        });

    /// <summary>Received, deposited at a bank: tahsil edildi. Posts the actual bank income now that
    /// the funds have cleared.</summary>
    public void Collect(string companyId, string chequeId, DateTime date, string userName)
    {
        var cheque = ReadCheque(companyId, chequeId) ?? throw new ArgumentException("Çek/senet bulunamadı.");
        if (cheque.Direction != "Received") throw new ArgumentException("Sadece alınan çek/senet tahsil edilebilir.");
        if (cheque.Status != "DepositedForCollection") throw new ArgumentException("Önce çek/senet bankaya tahsile verilmelidir.");
        if (string.IsNullOrWhiteSpace(cheque.BankAccountId)) throw new ArgumentException("Tahsile verilen banka hesabı bulunamadı.");
        var bank = new LocalBankService(database);
        bank.PostBankIn(companyId, cheque.BranchId, cheque.BankAccountId!, "ChequeCollection", cheque.AccountId, date, cheque.Amount, cheque.CurrencyCode, 1,
            cheque.ChequeNumber, $"Çek/senet tahsilatı: {cheque.ChequeNumber} — {cheque.DrawerName}", userName, chequeId);
        SetStatus(companyId, chequeId, "DepositedForCollection", "Collected", userName, "", settlementDate: date);
    }

    /// <summary>Received: doğrudan kasaya tahsil edildi (bankaya uğramadan). Used when a customer's
    /// cheque is cashed directly rather than deposited to a company bank account first.</summary>
    public void CollectToCash(string companyId, string chequeId, string cashAccountId, DateTime date, string userName)
    {
        var cheque = ReadCheque(companyId, chequeId) ?? throw new ArgumentException("Çek/senet bulunamadı.");
        if (cheque.Direction != "Received") throw new ArgumentException("Sadece alınan çek/senet tahsil edilebilir.");
        if (cheque.Status is not ("Portfolio" or "DepositedForCollection")) throw new ArgumentException("Bu çek/senet artık tahsil edilebilir durumda değil.");
        var cash = new LocalCashService(database);
        cash.PostCashIn(companyId, cheque.BranchId, cashAccountId, "ChequeCollection", cheque.AccountId, date, cheque.Amount, cheque.CurrencyCode, 1,
            cheque.ChequeNumber, $"Çek/senet tahsilatı: {cheque.ChequeNumber} — {cheque.DrawerName}", userName);
        SetStatus(companyId, chequeId, cheque.Status, "Collected", userName, "", settlementDate: date);
    }

    /// <summary>Received, was deposited: karşılıksız çıktı. No reversal needed since Collect is the
    /// only step that ever posted money - this just records the outcome.</summary>
    public void Bounce(string companyId, string chequeId, string userName, string? note = null)
        => Transition(companyId, chequeId, "Received", ["DepositedForCollection"], "Bounced", userName, cmd => cmd.CommandText = "UPDATE cheques SET status='Bounced', updated_at=$now, updated_by=$user WHERE id=$id", note);

    /// <summary>Received: başka bir ödemede ciro edildi (kullanıldı). No cash posting - the
    /// instrument left the portfolio by being handed to someone else, not by being cashed.</summary>
    public void Endorse(string companyId, string chequeId, string endorser, string userName)
        => Transition(companyId, chequeId, "Received", ["Portfolio"], "Endorsed", userName, cmd =>
        {
            cmd.CommandText = "UPDATE cheques SET status='Endorsed', endorser=$endorser, updated_at=$now, updated_by=$user WHERE id=$id";
            Add(cmd, "$endorser", endorser.Trim());
        });

    /// <summary>Given: vadesinde ödendi. Posts the payment from the selected cash or bank account -
    /// exactly one of cashAccountId/bankAccountId must be supplied.</summary>
    public void Pay(string companyId, string chequeId, string? cashAccountId, string? bankAccountId, DateTime date, string userName)
    {
        if (string.IsNullOrWhiteSpace(cashAccountId) == string.IsNullOrWhiteSpace(bankAccountId))
            throw new ArgumentException("Ödeme kasadan veya bankadan yapılmalı (ikisi birden değil).");
        var cheque = ReadCheque(companyId, chequeId) ?? throw new ArgumentException("Çek/senet bulunamadı.");
        if (cheque.Direction != "Given") throw new ArgumentException("Sadece verilen çek/senet ödenebilir.");
        if (cheque.Status != "Portfolio") throw new ArgumentException("Bu çek/senet zaten ödenmiş veya iade edilmiş.");
        if (!string.IsNullOrWhiteSpace(cashAccountId))
        {
            var cash = new LocalCashService(database);
            cash.PostCashOut(companyId, cheque.BranchId, cashAccountId, "ChequePayment", cheque.AccountId, date, cheque.Amount, cheque.CurrencyCode, 1,
                cheque.ChequeNumber, $"Çek/senet ödemesi: {cheque.ChequeNumber} — {cheque.DrawerName}", userName);
        }
        else
        {
            var bank = new LocalBankService(database);
            bank.PostBankOut(companyId, cheque.BranchId, bankAccountId!, "ChequePayment", cheque.AccountId, date, cheque.Amount, cheque.CurrencyCode, 1,
                cheque.ChequeNumber, $"Çek/senet ödemesi: {cheque.ChequeNumber} — {cheque.DrawerName}", userName, chequeId);
        }
        SetStatus(companyId, chequeId, "Portfolio", "Paid", userName, "", settlementDate: date);
    }

    /// <summary>Either direction, still in Portfolio: keşideciye/tarafa iade edildi - anlaşma iptal
    /// oldu, karşılık ödenmeden enstrüman geri verildi. No cash posting.</summary>
    public void ReturnToDrawer(string companyId, string chequeId, string userName, string? note = null)
        => Transition(companyId, chequeId, null, ["Portfolio"], "ReturnedToDrawer", userName, cmd => cmd.CommandText = "UPDATE cheques SET status='ReturnedToDrawer', updated_at=$now, updated_by=$user WHERE id=$id", note);

    public DataTable History(string companyId, string chequeId) => database.Query("""
        SELECT h.from_status AS Eski, h.to_status AS Yeni, h.changed_at AS Tarih, h.changed_by AS Kullanici, h.note AS Not
        FROM cheque_status_history h JOIN cheques c ON c.id=h.cheque_id
        WHERE h.cheque_id=$id AND c.company_id=$company ORDER BY h.changed_at
        """, ("$id", chequeId), ("$company", companyId));

    private sealed record ChequeRow(string BranchId, string Direction, string Status, string? AccountId, decimal Amount, string CurrencyCode,
        string ChequeNumber, string DrawerName, string? BankAccountId);

    private ChequeRow? ReadCheque(string companyId, string chequeId)
    {
        var t = database.Query("SELECT branch_id,direction,status,account_id,amount,currency_code,cheque_number,drawer_name,bank_account_id FROM cheques WHERE id=$id AND company_id=$company",
            ("$id", chequeId), ("$company", companyId));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        return new ChequeRow(r["branch_id"].ToString()!, r["direction"].ToString()!, r["status"].ToString()!, r["account_id"] is DBNull ? null : r["account_id"].ToString(),
            Convert.ToDecimal(r["amount"]), r["currency_code"].ToString()!, r["cheque_number"].ToString()!, r["drawer_name"].ToString()!,
            r["bank_account_id"] is DBNull ? null : r["bank_account_id"].ToString());
    }

    private void Transition(string companyId, string chequeId, string? requireDirection, string[] fromStatuses, string toStatus, string userName,
        Action<SqliteCommand> configure, string? note = null)
    {
        var cheque = ReadCheque(companyId, chequeId) ?? throw new ArgumentException("Çek/senet bulunamadı.");
        if (requireDirection != null && cheque.Direction != requireDirection) throw new ArgumentException($"Bu işlem sadece {(requireDirection == "Received" ? "alınan" : "verilen")} çek/senetler için geçerlidir.");
        if (!fromStatuses.Contains(cheque.Status)) throw new ArgumentException("Çek/senet bu durumda iken bu işlem yapılamaz.");
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx; configure(cmd);
            Add(cmd, "$now", now); Add(cmd, "$user", userName); Add(cmd, "$id", chequeId);
            cmd.ExecuteNonQuery();
        }
        LogHistory(c, tx, chequeId, cheque.Status, toStatus, userName, now, note ?? "");
        Audit(c, tx, companyId, "Cheque", chequeId, $"Cheque{toStatus}", cheque.ChequeNumber, now);
        tx.Commit();
    }

    private void SetStatus(string companyId, string chequeId, string fromStatus, string toStatus, string userName, string note, DateTime? settlementDate = null)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE cheques SET status=$status, settlement_date=$settlement, updated_at=$now, updated_by=$user WHERE id=$id AND company_id=$company";
            Add(cmd, "$status", toStatus); AddNullable(cmd, "$settlement", settlementDate?.ToString("O")); Add(cmd, "$now", now); Add(cmd, "$user", userName);
            Add(cmd, "$id", chequeId); Add(cmd, "$company", companyId);
            cmd.ExecuteNonQuery();
        }
        LogHistory(c, tx, chequeId, fromStatus, toStatus, userName, now, note);
        Audit(c, tx, companyId, "Cheque", chequeId, $"Cheque{toStatus}", note, now);
        tx.Commit();
    }

    private static void LogHistory(SqliteConnection c, SqliteTransaction tx, string chequeId, string fromStatus, string toStatus, string userName, string now, string note)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO cheque_status_history(id,cheque_id,from_status,to_status,changed_at,changed_by,note) VALUES($id,$cheque,$from,$to,$at,$user,$note)";
        Add(cmd, "$id", Guid.NewGuid().ToString()); Add(cmd, "$cheque", chequeId); Add(cmd, "$from", fromStatus); Add(cmd, "$to", toStatus);
        Add(cmd, "$at", now); Add(cmd, "$user", userName); Add(cmd, "$note", note);
        cmd.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string companyId, string entityType, string entityId, string action, string detail, string now)
    {
        using var audit = c.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO audit_logs(id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$c,$type,$entity,$action,$new,$now)";
        Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$c", companyId); Add(audit, "$type", entityType); Add(audit, "$entity", entityId);
        Add(audit, "$action", action); Add(audit, "$new", detail); Add(audit, "$now", now);
        audit.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
    private static void AddNullable(SqliteCommand c, string n, string? v) => c.Parameters.AddWithValue(n, (object?)v ?? DBNull.Value);
}
