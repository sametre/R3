namespace R3.Desktop;

public sealed class WorkspaceContext
{
    public Guid? CompanyId { get; private set; } public string CompanyName { get; private set; } = "Firma seçilmedi";
    public Guid? BranchId { get; private set; } public string BranchName { get; private set; } = "Şube seçilmedi";
    public Guid? WarehouseId { get; private set; } public string WarehouseName { get; private set; } = "Depo seçilmedi";
    public event EventHandler? Changed;
    public void SetCompany(Guid? id, string name) { CompanyId = id; CompanyName = name; BranchId = null; BranchName = "Şube seçilmedi"; WarehouseId = null; WarehouseName = "Depo seçilmedi"; Changed?.Invoke(this, EventArgs.Empty); }
    public void SetBranch(Guid? id, string name) { BranchId = id; BranchName = name; WarehouseId = null; WarehouseName = "Depo seçilmedi"; Changed?.Invoke(this, EventArgs.Empty); }
    public void SetWarehouse(Guid? id, string name) { WarehouseId = id; WarehouseName = name; Changed?.Invoke(this, EventArgs.Empty); }
}
