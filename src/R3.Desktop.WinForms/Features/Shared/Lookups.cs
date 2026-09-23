using System.Data;
using R3.Infrastructure;

namespace R3.Desktop.WinForms.Features.Shared;

/// <summary>Combo item for master-data lookups: "KOD — Ad".</summary>
public sealed record LookupItem(string Id, string Code, string Name)
{
    public string Text => Code.Length == 0 ? Name : $"{Code} — {Name}";
    public override string ToString() => Text;
}

/// <summary>Master-data lists (brands, categories, units, product_groups, countries…) through LocalMasterDataService.</summary>
public static class Lookups
{
    public static List<LookupItem> Load(LocalMasterDataService service, string kind, bool activeOnly = true) => From(service.List(kind), activeOnly);

    public static List<LookupItem> From(DataTable table, bool activeOnly = true)
    {
        var hasActive = table.Columns.Contains("Aktif");
        return table.Rows.Cast<DataRow>()
            .Where(r => !activeOnly || !hasActive || r["Aktif"] is DBNull || Convert.ToInt64(r["Aktif"]) == 1)
            .Select(r => new LookupItem(r["Id"].ToString()!, table.Columns.Contains("Kod") ? r["Kod"]?.ToString() ?? "" : "", r["Ad"]?.ToString() ?? ""))
            .ToList();
    }
}
