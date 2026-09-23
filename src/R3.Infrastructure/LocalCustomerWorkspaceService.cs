using System.Data;

namespace R3.Infrastructure;

public sealed record CustomerWorkspaceData(AccountDetail Customer, DataTable Statement, DataTable Notes,
    DataTable Installments, DataTable PendingProducts, DataTable DeliveredProducts, DataTable Cheques,
    DataTable History, decimal Debit, decimal Credit, decimal Balance, decimal Overdue);

/// <summary>Read model for the customer workspace. Every query is scoped to the company and customer.</summary>
public sealed class LocalCustomerWorkspaceService(StoreDatabase database)
{
    public DataTable Customers(string companyId) => new LocalAccountService(database).Lookup(companyId, "Customer");

    public DataTable SearchCustomers(string companyId, string code, string name, string identityNumber, string mobile, string phone, string city, string district)
        => database.Query("""
            SELECT a.id AS Id, a.code AS [Cari Kodu], a.name AS [Cari Hesap Adı],
                   a.identity_number AS [T.C. No], a.mobile_phone AS [Gsm No], a.phone AS [Ev Tel],
                   COALESCE((SELECT b.name FROM branches b WHERE b.company_id=a.company_id ORDER BY b.code LIMIT 1),'') AS Şube,
                   COALESCE((SELECT aa.address_line FROM account_addresses aa WHERE aa.account_id=a.id AND aa.is_active=1 ORDER BY aa.is_default DESC, aa.created_at LIMIT 1),'') AS Adres,
                   COALESCE((SELECT aa.delivery_region_code FROM account_addresses aa WHERE aa.account_id=a.id AND aa.is_active=1 ORDER BY aa.is_default DESC, aa.created_at LIMIT 1),'') AS Referans
            FROM accounts a
            WHERE a.company_id=$company AND a.is_active=1 AND a.account_type IN ('Customer','CustomerAndSupplier')
              AND ($code='' OR a.code LIKE '%' || $code || '%')
              AND ($name='' OR a.name LIKE '%' || $name || '%')
              AND ($identity='' OR a.identity_number LIKE '%' || $identity || '%')
              AND ($mobile='' OR a.mobile_phone LIKE '%' || $mobile || '%')
              AND ($phone='' OR a.phone LIKE '%' || $phone || '%')
              AND ($city='' OR EXISTS(SELECT 1 FROM account_addresses aa WHERE aa.account_id=a.id AND aa.is_active=1 AND aa.city LIKE '%' || $city || '%'))
              AND ($district='' OR EXISTS(SELECT 1 FROM account_addresses aa WHERE aa.account_id=a.id AND aa.is_active=1 AND aa.district LIKE '%' || $district || '%'))
            ORDER BY a.code
            LIMIT 500
            """, ("$company", companyId), ("$code", code.Trim()), ("$name", name.Trim()),
            ("$identity", identityNumber.Trim()), ("$mobile", mobile.Trim()), ("$phone", phone.Trim()),
            ("$city", city.Trim()), ("$district", district.Trim()));

    public CustomerWorkspaceData Load(string companyId, string accountId, string currency, DateTime today)
    {
        var detail = new LocalAccountService(database).GetDetail(companyId, accountId);
        if (detail == null || detail.Account.AccountType is not ("Customer" or "CustomerAndSupplier"))
            throw new ArgumentException("Müşteri bu firmada bulunamadı.");
        DataTable Query(string sql) => database.Query(sql, ("$c", companyId), ("$a", accountId), ("$currency", currency));
        var statement = Query("""
            SELECT t.transaction_at AS Tarih, t.transaction_type AS Islem,
                   COALESCE(t.document_no,'') AS BelgeNo, COALESCE(b.name,'') AS Sube,
                   t.description AS Aciklama, COALESCE(s.document_no,'') AS FaturaNo,
                   s.grand_total AS Tutar, s.discount_total AS Iskonto,
                   (SELECT SUM(l.quantity) FROM sales_document_lines l WHERE l.sales_document_id=s.id) AS Adet,
                   t.debit AS Borc, t.credit AS Alacak,
                   SUM(t.debit-t.credit) OVER (ORDER BY t.transaction_at,t.created_at,t.id) AS Bakiye,
                   t.currency_code AS Doviz
            FROM account_transactions t
            LEFT JOIN branches b ON b.id=t.branch_id AND b.company_id=t.company_id
            LEFT JOIN sales_documents s ON s.id=t.document_id AND s.company_id=t.company_id
                AND s.account_id=t.account_id AND t.document_type='SalesInvoice'
            WHERE t.company_id=$c AND t.account_id=$a AND t.currency_code=$currency
            ORDER BY t.transaction_at,t.created_at,t.id
            """);
        var notes = Query("""
            SELECT content AS [Not], created_at AS Tarih, created_by AS Giren, note_type AS [Not Tipi], title AS Başlık
            FROM account_notes WHERE account_id=$a
            AND EXISTS(SELECT 1 FROM accounts WHERE id=$a AND company_id=$c)
            ORDER BY is_pinned DESC, created_at DESC
            """);
        var installments = Query("""
            SELECT l.id AS Id, l.installment_no AS [Taksit No], l.due_date AS Vade, l.amount AS Tutar,
                   l.paid_amount AS Ödenen, MAX(0,l.amount-l.paid_amount) AS Kalan,
                   p.currency_code AS Döviz, l.paid_at AS [Ödeme Tarihi]
            FROM payment_plan_lines l JOIN payment_plans p ON p.id=l.payment_plan_id
            WHERE p.company_id=$c AND p.account_id=$a AND p.currency_code=$currency
              AND p.status<>'Cancelled' AND l.status<>'Cancelled'
            ORDER BY l.due_date,l.installment_no
            """);
        DataTable Products(bool delivered) => Query("""
            SELECT COALESCE(s.shipment_no,'') AS [Sevk No], p.code AS [Ürün Kodu], p.name AS Ürün,
                   l.planned_quantity AS Planlanan, l.shipped_quantity AS [Sevk Edilen], l.unit_code AS Birim,
                   s.planned_shipment_date AS [Planlanan Tarih],
                   CASE s.status WHEN 'Pending' THEN 'Bekliyor' WHEN 'Planned' THEN 'Planlandı'
                       WHEN 'InTransit' THEN 'Yolda' WHEN 'Delivered' THEN 'Teslim edildi' ELSE s.status END AS Durum
            FROM shipment_orders s JOIN shipment_order_lines l ON l.shipment_order_id=s.id
            JOIN products p ON p.id=l.product_id
            WHERE s.company_id=$c AND s.account_id=$a AND
            """ + (delivered ? " s.status='Delivered'" : " s.status IN ('Pending','Planned','InTransit')") + " ORDER BY s.order_date,s.id,l.id");
        var cheques = Query("""
            SELECT cheque_number AS [Çek No], due_date AS Vade, amount AS Tutar, currency_code AS Döviz,
                   drawer_name AS Keşideci, bank_name AS Banka,
                   CASE status WHEN 'Portfolio' THEN 'Portföyde' WHEN 'DepositedForCollection' THEN 'Bankada'
                     WHEN 'Bounced' THEN 'Karşılıksız' ELSE status END AS Durum
            FROM cheques WHERE company_id=$c AND account_id=$a AND currency_code=$currency
              AND instrument_type='Cheque' AND direction='Received'
              AND status IN ('Portfolio','DepositedForCollection','Bounced') ORDER BY due_date
            """);
        var history = Query("""
            SELECT created_at AS Tarih, COALESCE(user_id,'') AS Kullanıcı, action AS İşlem,
                   old_values AS [Eski Değer], new_values AS [Yeni Değer]
            FROM audit_logs WHERE company_id=$c AND entity_type IN ('Account','AccountAddress','AccountContact','AccountNote')
              AND (entity_id=$a OR new_values=$a) ORDER BY created_at DESC
            """);
        decimal Sum(string column) => statement.AsEnumerable().Sum(r => Convert.ToDecimal(r[column]));
        var overdue = installments.AsEnumerable().Where(r => DateTime.TryParse(r["Vade"].ToString(), out var due) && due.Date < today.Date)
            .Sum(r => Convert.ToDecimal(r["Kalan"]));
        return new(detail, statement, notes, installments, Products(false), Products(true), cheques, history,
            Sum("Borc"), Sum("Alacak"), Sum("Borc") - Sum("Alacak"), overdue);
    }
}
