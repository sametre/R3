using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using R3.Desktop.ContextActions;
using R3.Infrastructure;

namespace R3.Desktop.Views;

/// <summary>
/// Ayarlar > Kullanıcı ve Yetkiler. Two tabs: Kullanıcılar (create/edit/password/activate, ASB MNUSER
/// equivalent) and Roller ve Yetkiler (role list + a grouped permission matrix, ASB MNUSERGRUP/YETKI
/// equivalent). All rules (last-admin guard, self-deactivation, username format) live in
/// <see cref="LocalUserAdminService"/>; this view only shows its messages.
/// </summary>
public sealed class UserRoleManagementView : DockPanel
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(103, 113, 121));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(22, 124, 130));
    private readonly LocalUserAdminService _users;
    private readonly StoreDatabase _db;
    private readonly string _companyId;
    private readonly string _actingUser;
    private readonly Action _permissionsChanged;

    public UserRoleManagementView(LocalUserAdminService users, StoreDatabase db, string companyId, string actingUser, Action permissionsChanged)
    {
        _users = users; _db = db; _companyId = companyId; _actingUser = actingUser; _permissionsChanged = permissionsChanged;
        Margin = new Thickness(18);
        var title = new TextBlock { Text = "Kullanıcı ve Yetkiler", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(47, 56, 63)), Margin = new Thickness(0, 0, 0, 2) };
        SetDock(title, Dock.Top); Children.Add(title);
        var help = new TextBlock { Text = "Yönetici rolü tüm yetkilere sahiptir. Diğer roller için yetkiler kaydedilene kadar rol sınırsız çalışır; kaydedildikten sonra yalnızca işaretli yetkiler geçerlidir.", Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        SetDock(help, Dock.Top); Children.Add(help);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Kullanıcılar", Content = BuildUsersTab() });
        tabs.Items.Add(new TabItem { Header = "Roller ve Yetkiler", Content = BuildRolesTab() });
        Children.Add(tabs);
    }

    private Window? Owner => Window.GetWindow(this);

    private UIElement BuildUsersTab()
    {
        var root = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var bar = Toolbar(root);
        var search = new TextBox { Width = 220, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Kullanıcı adı, ad soyad, e-posta veya rol ara" };
        var showInactive = new CheckBox { Content = "Pasifleri göster", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var grid = MakeGrid();
        Col(grid, "Kullanıcı", "KullaniciAdi", 130); Col(grid, "Ad Soyad", "AdSoyad", 190); Col(grid, "Rol", "Rol", 150);
        Col(grid, "E-posta", "Eposta", 190); Col(grid, "Varsayılan Şube", "VarsayilanSube", 140); Col(grid, "Varsayılan Depo", "VarsayilanDepo", 140);
        Col(grid, "Son Giriş", "SonGiris", 120); grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Aktif", Binding = new Binding("Aktif") { Converter = new LongToBoolConverter() }, Width = 55 });

        void Refresh()
        {
            var selected = (grid.SelectedItem as DataRowView)?["Id"].ToString();
            grid.ItemsSource = _users.SearchUsers(search.Text, activeOnly: showInactive.IsChecked != true).DefaultView;
            Reselect(grid, selected);
        }
        void Edit(bool create)
        {
            var row = grid.SelectedItem as DataRowView;
            if (!create && row == null) { Info("Önce bir kullanıcı seçin."); return; }
            var existing = create ? null : _users.GetUser(row!["Id"].ToString()!);
            var dialog = new UserAccountDialog(_users, _db, _companyId, existing, _actingUser) { Owner = Owner };
            if (dialog.ShowDialog() != true) return;
            Refresh(); Reselect(grid, dialog.SavedId);
        }
        void ResetPassword()
        {
            if (grid.SelectedItem is not DataRowView row) { Info("Önce bir kullanıcı seçin."); return; }
            var dialog = new PasswordResetDialog(row["KullaniciAdi"].ToString()!) { Owner = Owner };
            if (dialog.ShowDialog() != true) return;
            Try(() => _users.ResetPassword(row["Id"].ToString()!, dialog.Password, _actingUser), "Şifre değiştirilemedi");
            Info($"{row["KullaniciAdi"]} kullanıcısının şifresi değiştirildi.");
        }
        Task SetActive(bool active)
        {
            if (grid.SelectedItem is DataRowView row && Try(() => _users.SetUserActive(row["Id"].ToString()!, active, _actingUser), "Kullanıcı güncellenemedi")) Refresh();
            return Task.CompletedTask;
        }

        AddButton(bar, "+ Yeni Kullanıcı (F2)", () => Edit(true), primary: true); AddButton(bar, "Düzenle (F3)", () => Edit(false));
        void Access()
        {
            if (grid.SelectedItem is not DataRowView row) { Info("Önce bir kullanıcı seçin."); return; }
            var dialog = new UserAccessDialog(_db, _companyId, row["KullaniciAdi"].ToString()!, _users.GetAccess(row["Id"].ToString()!)) { Owner = Owner };
            if (dialog.ShowDialog() == true && Try(() => _users.SaveAccess(row["Id"].ToString()!, dialog.BranchIds, dialog.WarehouseIds, _actingUser), "Erişim kaydedilemedi")) Refresh();
        }
        AddButton(bar, "Şifre Sıfırla", ResetPassword); AddButton(bar, "Şube / Depo Erişimi", Access); AddButton(bar, "Yenile (F5)", Refresh);
        bar.Children.Add(new TextBlock { Text = "Ara:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) }); bar.Children.Add(search); bar.Children.Add(showInactive);
        showInactive.Click += (_, _) => Refresh();
        ErpGridContext.Register(grid, "settings.users", StandardContextActions.Users(() => Edit(false), ResetPassword, SetActive), () => { Refresh(); return Task.CompletedTask; }, "User");
        KeyboardInteractionService.AttachDebouncedSearch(search, Refresh);
        KeyboardInteractionService.AttachListShortcuts(root, search, () => Edit(true), () => Edit(false), Refresh);
        grid.MouseDoubleClick += (_, _) => Edit(false);
        root.Children.Add(grid); Refresh();
        return root;
    }

    private UIElement BuildRolesTab()
    {
        var layout = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: roles
        var left = new DockPanel(); layout.Children.Add(left);
        var roleBar = Toolbar(left);
        var roles = MakeGrid();
        Col(roles, "Kod", "Kod", 75); Col(roles, "Rol", "Ad", 130); Col(roles, "Kullanıcı", "KullaniciSayisi", 60); Col(roles, "Yetki", "YetkiSayisi", 115);
        roles.Columns.Add(new DataGridCheckBoxColumn { Header = "Aktif", Binding = new Binding("Aktif") { Converter = new LongToBoolConverter() }, Width = 45 });
        left.Children.Add(roles);

        // Right: permission matrix
        var right = new DockPanel(); Grid.SetColumn(right, 2); layout.Children.Add(right);
        var header = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) }; SetDock(header, Dock.Top); right.Children.Add(header);
        var state = new TextBlock { Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) }; SetDock(state, Dock.Top); right.Children.Add(state);
        var matrixBar = Toolbar(right);
        var module = new ComboBox { Width = 140, Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
        var filter = new TextBox { Width = 170, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Yetki ara" };
        var matrix = new DataGrid
        {
            Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"),
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        var allowedColumn = new DataGridCheckBoxColumn { Header = "İzin", Binding = new Binding(nameof(PermissionItem.IsAllowed)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 48 };
        matrix.Columns.Add(allowedColumn);
        matrix.Columns.Add(new DataGridTextColumn { Header = "Yetki", Binding = new Binding(nameof(PermissionItem.Label)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        matrix.Columns.Add(new DataGridTextColumn { Header = "Kod", Binding = new Binding(nameof(PermissionItem.Key)), Width = 230, IsReadOnly = true, Foreground = Muted });
        var groupHeader = new FrameworkElementFactory(typeof(TextBlock));
        groupHeader.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        groupHeader.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        groupHeader.SetValue(TextBlock.ForegroundProperty, Accent);
        groupHeader.SetValue(TextBlock.MarginProperty, new Thickness(4, 8, 0, 3));
        matrix.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = groupHeader } });
        // Single click toggles the checkbox instead of the stock DataGrid "click to select, click again to edit".
        matrix.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (matrix.IsReadOnly || e.OriginalSource is not DependencyObject source) return;
            var row = ItemsControl.ContainerFromElement(matrix, source) as DataGridRow;
            if (row?.Item is PermissionItem item && FindParent<DataGridCell>(source) is { Column: var column } && column == allowedColumn) { item.IsAllowed = !item.IsAllowed; e.Handled = true; }
        };
        matrix.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && !matrix.IsReadOnly && matrix.SelectedItem is PermissionItem item) { item.IsAllowed = !item.IsAllowed; e.Handled = true; }
        };
        right.Children.Add(matrix);

        List<PermissionItem> items = [];
        ListCollectionView? view = null;
        string? currentRoleId = null, currentRoleCode = null;
        var dirty = false;
        var unconfigured = false;

        void UpdateState()
        {
            if (currentRoleId == null) { header.Text = "Rol seçin"; state.Text = ""; return; }
            var granted = items.Count(x => x.IsAllowed);
            state.Text = string.Equals(currentRoleCode, LocalUserAdminService.AdminRoleCode, StringComparison.OrdinalIgnoreCase)
                ? "Yönetici rolü her zaman tüm yetkilere sahiptir; bu matris yalnızca bilgi amaçlıdır."
                : $"{granted} / {items.Count} yetki işaretli"
                  + (unconfigured && !dirty ? "  •  bu rol henüz yapılandırılmadı, şu an tüm modüllere erişebilir; kaydettiğinizde yalnızca işaretliler geçerli olur" : "")
                  + (dirty ? "  •  kaydedilmemiş değişiklik var (Ctrl+S)" : "");
        }
        void LoadMatrix(DataRowView? role)
        {
            currentRoleId = role?["Id"].ToString(); currentRoleCode = role?["Kod"].ToString();
            unconfigured = role?["YetkiSayisi"].ToString()?.StartsWith("Sınırsız", StringComparison.Ordinal) == true;
            header.Text = role == null ? "Rol seçin" : $"{role["Ad"]} ({role["Kod"]}) — yetkiler";
            var isAdmin = string.Equals(currentRoleCode, LocalUserAdminService.AdminRoleCode, StringComparison.OrdinalIgnoreCase);
            items = currentRoleId == null ? [] : _users.GetRolePermissions(currentRoleId).Select(p => new PermissionItem(p.Key, p.Module, p.Label, isAdmin || p.IsAllowed)).ToList();
            foreach (var item in items) item.PropertyChanged += (_, _) => { dirty = true; UpdateState(); };
            view = new ListCollectionView(items);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PermissionItem.Module)));
            view.Filter = x => x is PermissionItem p
                && (module.SelectedItem is not string m || m == "Tüm modüller" || p.Module == m)
                && (string.IsNullOrWhiteSpace(filter.Text) || p.Label.Contains(filter.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) || p.Key.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            matrix.ItemsSource = view;
            matrix.IsReadOnly = isAdmin || currentRoleId == null;
            dirty = false; UpdateState();
        }
        // Selection is tracked by role id, never by DataRowView reference: RefreshRoles swaps the
        // ItemsSource, so a remembered row object goes stale after every save.
        var suppressSelection = false;
        string? SelectedRoleId() => (roles.SelectedItem as DataRowView)?["Id"].ToString();
        void SelectRole(string? id)
        {
            suppressSelection = true;
            Reselect(roles, id);
            suppressSelection = false;
        }
        bool SaveCurrentMatrix()
        {
            if (currentRoleId == null || matrix.IsReadOnly) return true;
            matrix.CommitEdit(DataGridEditingUnit.Row, true);
            if (!Try(() => _users.SaveRolePermissions(currentRoleId, items.Where(x => x.IsAllowed).Select(x => x.Key), _actingUser), "Yetkiler kaydedilemedi")) return false;
            dirty = false; _permissionsChanged(); UpdateState(); return true;
        }
        // Yes = save then continue, No = discard then continue, Cancel = stay.
        bool ConfirmLeave()
        {
            if (!dirty) return true;
            var answer = MessageBox.Show(Owner!, "Seçili rolün yetkilerinde kaydedilmemiş değişiklikler var. Kaydedilsin mi?", "Kullanıcı ve Yetkiler", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes) return SaveCurrentMatrix();
            dirty = false; return true;
        }
        void RefreshRoles(string? selectId)
        {
            suppressSelection = true;
            roles.ItemsSource = _users.SearchRoles().DefaultView;
            Reselect(roles, selectId);
            if (roles.SelectedItem == null && roles.Items.Count > 0) roles.SelectedIndex = 0;
            suppressSelection = false;
            var shown = SelectedRoleId();
            if (shown != currentRoleId) LoadMatrix(roles.SelectedItem as DataRowView);
        }
        void SaveMatrix() { if (SaveCurrentMatrix()) RefreshRoles(currentRoleId); }
        void ReloadRoles() { if (ConfirmLeave()) { var id = currentRoleId; currentRoleId = null; RefreshRoles(id); } }
        void SetVisible(bool allowed)
        {
            if (matrix.IsReadOnly || view == null) return;
            foreach (var item in view.Cast<PermissionItem>()) item.IsAllowed = allowed;
        }
        void EditRole(bool create)
        {
            var row = roles.SelectedItem as DataRowView;
            if (!create && row == null) { Info("Önce bir rol seçin."); return; }
            if (create && !ConfirmLeave()) return;
            var existing = create ? null : new RoleEdit(row!["Id"].ToString()!, row["Kod"].ToString()!, row["Ad"].ToString()!, row["Aciklama"].ToString() ?? "", Convert.ToInt64(row["Aktif"]) == 1);
            var dialog = new RoleDialog(existing) { Owner = Owner };
            if (dialog.ShowDialog() != true) return;
            string? id = null;
            if (Try(() => id = _users.SaveRole(dialog.Result(existing?.Id ?? ""), _actingUser), "Rol kaydedilemedi")) { RefreshRoles(id); _permissionsChanged(); }
        }
        Task SetRoleActive(bool active)
        {
            if (roles.SelectedItem is DataRowView row
                && Try(() => _users.SaveRole(new RoleEdit(row["Id"].ToString()!, row["Kod"].ToString()!, row["Ad"].ToString()!, row["Aciklama"].ToString() ?? "", active), _actingUser), "Rol güncellenemedi"))
                RefreshRoles(row["Id"].ToString());
            return Task.CompletedTask;
        }

        roles.SelectionChanged += (_, _) =>
        {
            if (suppressSelection) return;
            var target = SelectedRoleId();
            if (target == currentRoleId) return;
            if (!ConfirmLeave()) { SelectRole(currentRoleId); return; }
            // A save refreshes counts; keep the row the user actually clicked.
            RefreshRoles(target);
        };

        AddButton(roleBar, "+ Yeni Rol", () => EditRole(true), primary: true); AddButton(roleBar, "Düzenle", () => EditRole(false)); AddButton(roleBar, "Yenile", ReloadRoles);
        module.ItemsSource = new[] { "Tüm modüller" }.Concat(R3.Application.Security.PermissionCatalog.All.Select(R3.Application.Security.PermissionLabels.Module).Distinct()).ToList();
        module.SelectedIndex = 0;
        module.SelectionChanged += (_, _) => view?.Refresh();
        KeyboardInteractionService.AttachDebouncedSearch(filter, () => view?.Refresh());
        matrixBar.Children.Add(module); matrixBar.Children.Add(new TextBlock { Text = "Ara:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) }); matrixBar.Children.Add(filter);
        AddButton(matrixBar, "Görünenleri işaretle", () => SetVisible(true)); AddButton(matrixBar, "Görünenleri kaldır", () => SetVisible(false));
        AddButton(matrixBar, "Kaydet (Ctrl+S)", SaveMatrix, primary: true);
        right.PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { SaveMatrix(); e.Handled = true; } };
        ErpGridContext.Register(roles, "settings.roles", StandardContextActions.MasterData(() => roles.Focus(), () => EditRole(false), SetRoleActive), () => { ReloadRoles(); return Task.CompletedTask; }, "Role");
        roles.MouseDoubleClick += (_, _) => EditRole(false);
        RefreshRoles(null);
        return layout;
    }

    private static StackPanel Toolbar(DockPanel parent)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        SetDock(bar, Dock.Top); parent.Children.Add(bar); return bar;
    }

    private static void AddButton(Panel bar, string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Height = 26, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        if (primary) { button.Background = Accent; button.Foreground = Brushes.White; button.BorderThickness = new Thickness(0); }
        button.Click += (_, _) => action(); bar.Children.Add(button);
    }

    private static DataGrid MakeGrid() => new()
    {
        Style = (Style)System.Windows.Application.Current.FindResource("ProfessionalDataGridStyle"),
        IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column
    };

    private static void Col(DataGrid grid, string header, string path, double width) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width });

    private static void Reselect(DataGrid grid, string? id)
    {
        if (id == null) return;
        foreach (var item in grid.Items)
            if (item is DataRowView row && row["Id"].ToString() == id) { grid.SelectedItem = item; grid.ScrollIntoView(item); return; }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null && child is not T) child = VisualTreeHelper.GetParent(child);
        return child as T;
    }

    private void Info(string message) => MessageBox.Show(Owner!, message, "Kullanıcı ve Yetkiler", MessageBoxButton.OK, MessageBoxImage.Information);

    private bool Try(Action action, string title)
    {
        try { action(); return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(Owner!, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning); return false;
        }
    }

    private sealed class PermissionItem(string key, string module, string label, bool isAllowed) : INotifyPropertyChanged
    {
        private bool _isAllowed = isAllowed;
        public string Key { get; } = key;
        public string Module { get; } = module;
        public string Label { get; } = label;
        public bool IsAllowed { get => _isAllowed; set { if (_isAllowed == value) return; _isAllowed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAllowed))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class LongToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is not DBNull && value != null && System.Convert.ToInt64(value) == 1;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}

public sealed class UserAccountDialog : EditorDialog
{
    public string? SavedId { get; private set; }

    public UserAccountDialog(LocalUserAdminService users, StoreDatabase db, string companyId, UserAccountEdit? existing, string actingUser)
        : base(existing == null ? "Yeni Kullanıcı" : "Kullanıcıyı Düzenle")
    {
        Width = 420;
        var userName = Field("Kullanıcı kodu *", new TextBox { Text = existing?.UserName ?? "", MaxLength = 30, CharacterCasing = CharacterCasing.Normal });
        var displayName = Field("Ad soyad *", new TextBox { Text = existing?.DisplayName ?? "", MaxLength = 100 });
        var email = Field("E-posta", new TextBox { Text = existing?.Email ?? "", MaxLength = 100 });
        var role = Field("Rol *", new ComboBox { ItemsSource = users.RoleLookup().DefaultView, DisplayMemberPath = "Name", SelectedValuePath = "Code", SelectedValue = existing?.RoleCode ?? "USER" });
        var branches = db.Query("SELECT '' AS Id, '(Seçilmedi)' AS Name, '' AS Code UNION ALL SELECT id, code || ' - ' || name, code FROM branches WHERE company_id=$c AND is_active=1 ORDER BY 3", ("$c", companyId));
        var branch = Field("Varsayılan şube (girişte önerilir)", new ComboBox { ItemsSource = branches.DefaultView, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = existing?.DefaultBranchId ?? "" });
        var warehouse = Field("Varsayılan depo", new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id" });
        void LoadWarehouses()
        {
            var branchId = branch.SelectedValue as string ?? "";
            warehouse.ItemsSource = db.Query("SELECT '' AS Id, '(Seçilmedi)' AS Name, '' AS Code UNION ALL SELECT id, code || ' - ' || name, code FROM warehouses WHERE branch_id=$b AND is_active=1 ORDER BY 3", ("$b", branchId)).DefaultView;
            warehouse.SelectedValue = existing?.DefaultBranchId == branchId ? existing?.DefaultWarehouseId ?? "" : "";
            if (warehouse.SelectedIndex < 0) warehouse.SelectedIndex = 0;
        }
        branch.SelectionChanged += (_, _) => LoadWarehouses();
        if (branch.SelectedIndex < 0) branch.SelectedIndex = 0;
        LoadWarehouses();

        PasswordBox? password = null, confirm = null;
        if (existing == null)
        {
            password = Field($"Şifre * (en az {LocalUserAdminService.MinimumPasswordLength} karakter)", new PasswordBox());
            confirm = Field("Şifre (tekrar) *", new PasswordBox());
        }
        var active = new CheckBox { Content = "Aktif", IsChecked = existing?.IsActive ?? true, Margin = new Thickness(0, 10, 0, 0) }; Fields.Children.Add(active);
        Finish(() =>
        {
            if (password != null && password.Password != confirm!.Password) throw new ArgumentException("Şifreler eşleşmiyor.");
            var edit = new UserAccountEdit(existing?.Id ?? "", userName.Text, displayName.Text, email.Text, role.SelectedValue as string ?? "",
                branch.SelectedValue as string, warehouse.SelectedValue as string, active.IsChecked == true);
            try { SavedId = users.SaveUser(edit, password?.Password, actingUser); }
            catch (InvalidOperationException ex) { throw new ArgumentException(ex.Message); }
        });
        Loaded += (_, _) => { if (existing == null) userName.Focus(); else displayName.Focus(); };
    }
}

/// <summary>ASB YETKI SUB/DEP equivalent: tick the şubeler/depolar a user may work in. Nothing ticked =
/// unrestricted; the Yönetici role is never restricted.</summary>
public sealed class UserAccessDialog : EditorDialog
{
    private readonly List<(CheckBox Box, string Id, string BranchId)> _branches = [], _warehouses = [];
    public IReadOnlyCollection<string> BranchIds => _branches.Where(x => x.Box.IsChecked == true).Select(x => x.Id).ToList();
    public IReadOnlyCollection<string> WarehouseIds => _warehouses.Where(x => x.Box.IsChecked == true).Select(x => x.Id).ToList();

    public UserAccessDialog(StoreDatabase db, string companyId, string userName, LocalUserAdminService.UserAccess current) : base($"Şube / Depo Erişimi — {userName}")
    {
        Width = 460;
        Fields.Children.Add(new TextBlock { Text = "Hiçbir şube işaretlenmezse kullanıcı tüm şubelere, hiçbir depo işaretlenmezse tüm depolara erişir. Yönetici rolü kısıtlanmaz. Girişte ve alt çubuktaki şube/depo seçiminde uygulanır.", TextWrapping = TextWrapping.Wrap, FontSize = 10.5, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
        var list = new StackPanel();
        foreach (DataRow branch in db.Query("SELECT id, code || ' — ' || name FROM branches WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", companyId)).Rows)
        {
            var branchId = branch[0].ToString()!;
            var box = new CheckBox { Content = branch[1].ToString(), FontWeight = FontWeights.SemiBold, IsChecked = current.BranchIds.Contains(branchId), Margin = new Thickness(0, 6, 0, 2) };
            list.Children.Add(box); _branches.Add((box, branchId, branchId));
            foreach (DataRow warehouse in db.Query("SELECT id, code || ' — ' || name FROM warehouses WHERE branch_id=$b AND is_active=1 ORDER BY code", ("$b", branchId)).Rows)
            {
                var wbox = new CheckBox { Content = warehouse[1].ToString(), IsChecked = current.WarehouseIds.Contains(warehouse[0].ToString()!), Margin = new Thickness(22, 2, 0, 2) };
                list.Children.Add(wbox); _warehouses.Add((wbox, warehouse[0].ToString()!, branchId));
                // Ticking a depo implies its şube once any şube restriction exists.
                wbox.Checked += (_, _) => { if (_branches.Any(b => b.Box.IsChecked == true)) box.IsChecked = true; };
            }
            box.Unchecked += (_, _) => { foreach (var w in _warehouses.Where(w => w.BranchId == branchId)) w.Box.IsChecked = false; };
        }
        Fields.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Finish(() => { });
    }
}

public sealed class PasswordResetDialog : EditorDialog
{
    public string Password { get; private set; } = "";

    public PasswordResetDialog(string userName) : base($"Şifre Sıfırla — {userName}")
    {
        var password = Field($"Yeni şifre * (en az {LocalUserAdminService.MinimumPasswordLength} karakter)", new PasswordBox());
        var confirm = Field("Yeni şifre (tekrar) *", new PasswordBox());
        Finish(() =>
        {
            if (password.Password.Length < LocalUserAdminService.MinimumPasswordLength) throw new ArgumentException($"Şifre en az {LocalUserAdminService.MinimumPasswordLength} karakter olmalıdır.");
            if (password.Password != confirm.Password) throw new ArgumentException("Şifreler eşleşmiyor.");
            Password = password.Password;
        });
        Loaded += (_, _) => password.Focus();
    }
}

public sealed class RoleDialog : EditorDialog
{
    private readonly TextBox _code, _name, _description; private readonly CheckBox _active;

    public RoleDialog(RoleEdit? existing) : base(existing == null ? "Yeni Rol" : "Rolü Düzenle")
    {
        var system = existing != null && LocalUserAdminService.IsSystemRole(existing.Code);
        _code = Field("Rol kodu * (ör. SATIS, DEPO)", new TextBox { Text = existing?.Code ?? "", MaxLength = 20, CharacterCasing = CharacterCasing.Upper, IsReadOnly = system });
        _name = Field("Rol adı *", new TextBox { Text = existing?.Name ?? "", MaxLength = 100 });
        _description = Field("Açıklama", new TextBox { Text = existing?.Description ?? "", MaxLength = 300, Height = 54, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap });
        _active = new CheckBox { Content = "Aktif", IsChecked = existing?.IsActive ?? true, Margin = new Thickness(0, 10, 0, 0), IsEnabled = !string.Equals(existing?.Code, LocalUserAdminService.AdminRoleCode, StringComparison.OrdinalIgnoreCase) };
        Fields.Children.Add(_active);
        if (system) Fields.Children.Add(new TextBlock { Text = "Sistem rolü: kod değiştirilemez.", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });
        Finish(() => { if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Rol kodu ve adı zorunludur."); });
        Loaded += (_, _) => (system ? _name : _code).Focus();
    }

    public RoleEdit Result(string id) => new(id, _code.Text.Trim(), _name.Text.Trim(), _description.Text.Trim(), _active.IsChecked == true);
}
