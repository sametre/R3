using System.Data;

namespace R3.Infrastructure;

public sealed record ShipmentQueueSummary(int Pending, int PlannedToday, int InTransit, int Overdue);

public sealed class LocalShipmentService(StoreDatabase database)
{
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
