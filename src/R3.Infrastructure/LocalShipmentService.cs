using System.Data;

namespace R3.Infrastructure;

public sealed record ShipmentQueueSummary(int Pending, int PlannedToday, int InTransit, int Overdue);

public sealed class LocalShipmentService(StoreDatabase database)
{
    public void Transition(string shipmentId, string nextStatus, string userName)
    {
        if (nextStatus is not ("Planned" or "InTransit" or "Delivered" or "Cancelled")) throw new ArgumentException("Geçersiz sevkiyat durumu.");
        using var connection = database.OpenConnection(); using var transaction = connection.BeginTransaction();
        string companyId; string current;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction; read.CommandText = "SELECT company_id,status FROM shipment_orders WHERE id=$id"; read.Parameters.AddWithValue("$id", shipmentId);
            using var reader = read.ExecuteReader(); if (!reader.Read()) throw new KeyNotFoundException("Sevkiyat bulunamadı.");
            companyId = reader.GetString(0); current = reader.GetString(1);
        }
        var allowed = (current, nextStatus) switch
        {
            ("Pending", "Planned") => true,
            ("Planned", "InTransit") => true,
            ("InTransit", "Delivered") => true,
            ("Pending", "Cancelled") or ("Planned", "Cancelled") or ("InTransit", "Cancelled") => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"{current} durumundaki sevkiyat {nextStatus} durumuna geçirilemez.");
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction; update.CommandText = "UPDATE shipment_orders SET status=$status,updated_at=$now WHERE id=$id";
            update.Parameters.AddWithValue("$status", nextStatus); update.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O")); update.Parameters.AddWithValue("$id", shipmentId); update.ExecuteNonQuery();
        }
        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction; audit.CommandText = "INSERT INTO audit_logs(id,user_id,company_id,entity_type,entity_id,action,new_values,created_at) VALUES($id,$user,$company,'ShipmentOrder',$entity,$action,$value,$now)";
            audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); audit.Parameters.AddWithValue("$user", userName); audit.Parameters.AddWithValue("$company", companyId); audit.Parameters.AddWithValue("$entity", shipmentId); audit.Parameters.AddWithValue("$action", "ShipmentStatusChanged"); audit.Parameters.AddWithValue("$value", $"{current}->{nextStatus}"); audit.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O")); audit.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    public ShipmentQueueSummary GetSummary(string companyId)
    {
        var table = database.Query("""
            SELECT
              SUM(CASE WHEN status IN ('Pending','Planned') THEN 1 ELSE 0 END) AS pending,
              SUM(CASE WHEN status IN ('Pending','Planned') AND date(planned_shipment_date)=date('now','localtime') THEN 1 ELSE 0 END) AS today,
              SUM(CASE WHEN status='InTransit' THEN 1 ELSE 0 END) AS transit,
              SUM(CASE WHEN status IN ('Pending','Planned') AND planned_shipment_date IS NOT NULL AND datetime(planned_shipment_date)<datetime('now') THEN 1 ELSE 0 END) AS overdue
            FROM shipment_orders WHERE company_id=$company
            """, ("$company", companyId));
        var row = table.Rows[0];
        return new ShipmentQueueSummary(Number(row["pending"]), Number(row["today"]), Number(row["transit"]), Number(row["overdue"]));
    }

    public DataTable SearchPending(string companyId, string? search = null)
    {
        return database.Query("""
            SELECT s.id AS Id, COALESCE(s.shipment_no,'Planlanmadı') AS SevkNo,
                   s.order_date AS KayitTarihi, s.planned_shipment_date AS PlanlananSevk,
                   a.code AS CariKodu, a.name AS Cari, b.name AS Sube, w.name AS Depo,
                   COUNT(l.id) AS Satir, COALESCE(SUM(l.planned_quantity),0) AS PlanlananMiktar,
                   COALESCE(SUM(l.shipped_quantity),0) AS SevkEdilen, s.delivery_region_code AS SevkBolgesi,
                   s.carrier_code AS Nakliyeci, s.vehicle_plate AS Plaka, s.driver_name AS Surucu,
                   s.delivery_contact AS TeslimAlan, s.status AS Durum, s.description AS Aciklama
            FROM shipment_orders s
            JOIN accounts a ON a.id=s.account_id
            JOIN branches b ON b.id=s.branch_id
            JOIN warehouses w ON w.id=s.warehouse_id
            LEFT JOIN shipment_order_lines l ON l.shipment_order_id=s.id
            WHERE s.company_id=$company AND s.status IN ('Pending','Planned','InTransit')
              AND ($search='' OR COALESCE(s.shipment_no,'') LIKE $like OR a.code LIKE $like OR a.name LIKE $like OR s.vehicle_plate LIKE $like)
            GROUP BY s.id ORDER BY COALESCE(s.planned_shipment_date,s.order_date), s.order_date
            """, ("$company", companyId), ("$search", search?.Trim() ?? ""), ("$like", $"%{search?.Trim() ?? ""}%"));
    }

    private static int Number(object value) => value == DBNull.Value ? 0 : Convert.ToInt32(value);
}
