using Krypton.Toolkit;
using Microsoft.Extensions.Configuration;
using System.Net;
using R3.Application.Abstractions;
using R3.Desktop.Branding;
using R3.Desktop.Controls;
using R3.Desktop.Dialogs;
using R3.Desktop.Navigation;
using R3.Desktop.Theme;
using R3.Desktop.Workspaces;
using R3.Application.MasterData;
using R3.Application.Transactions;
using R3.Application.Dashboard;

namespace R3.Desktop.Forms;

public sealed class MainForm : KryptonForm
{
    private readonly IDataConnectionVerifier _connectionVerifier;
    private readonly IUserSession _userSession;
    private readonly IRetailMasterDataService _masterData;
    private readonly ITradeTransactionService _tradeTransactions;
    private readonly IDashboardService _dashboardService;
    private readonly R3DesktopSurface _desktop = new();
    private readonly Dictionary<string, R3ModuleWindow> _windows = new(StringComparer.Ordinal);
    private R3StatusBar _statusBar = null!;
    private R3ToastManager _toasts = null!;
    private readonly R3AiPanel _aiPanel=new();
    private readonly Button _aiButton=new();

    public MainForm(
        IDataConnectionVerifier connectionVerifier,
        IUserSession userSession,
        IRetailMasterDataService masterData,
        ITradeTransactionService tradeTransactions,
        IDashboardService dashboardService,
        IConfiguration configuration)
    {
        _connectionVerifier = connectionVerifier;
        _userSession = userSession;
        _masterData = masterData;
        _tradeTransactions = tradeTransactions;
        _dashboardService=dashboardService;
        R3Theme.Apply();
        InitializeDesktop(configuration.GetConnectionString("R3Database") ?? string.Empty);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        OpenModule("dashboard");
        await CheckDatabaseAsync();
    }

    private void InitializeDesktop(string connectionString)
    {
        Text = "R3 ERP • KOBİ Çözümleri";
        Icon = R3Branding.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 760);
        WindowState = FormWindowState.Maximized;

        var menu = new R3ModuleMenu(_userSession.Current);
        menu.ModuleSelected += (_, key) => HandleMenuSelection(key);

        (string server, string databaseUser) = ReadConnectionIdentity(connectionString);
        _statusBar = new R3StatusBar(_userSession.Current, server, databaseUser);
        _statusBar.StartConnectionMonitor(() => _connectionVerifier.CanConnectAsync());
        _toasts = new R3ToastManager(_desktop);
        _desktop.Resize += (_, _) => _toasts.Reposition();
        _aiPanel.QuestionSubmitted+=async(_,question)=>await AnswerAiAsync(question);
        _aiPanel.PdfSelected+=(_,path)=>_aiPanel.AddAnswer($"“{Path.GetFileName(path)}” belge motoruna hazırlandı. Çıkarılan alanlar önizleme ve kullanıcı onayından sonra taslak faturaya dönüştürülecek.");
        _aiButton.Text="✦  R3 AI";_aiButton.Size=new Size(104,38);_aiButton.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;_aiButton.FlatStyle=FlatStyle.Flat;_aiButton.BackColor=Color.FromArgb(37,99,235);_aiButton.ForeColor=Color.White;_aiButton.Font=new Font("Segoe UI Semibold",9);_aiButton.Cursor=Cursors.Hand;_aiButton.FlatAppearance.BorderSize=0;_aiButton.Click+=(_,_)=>_aiPanel.Toggle();
        _desktop.Controls.Add(_aiPanel);_desktop.Controls.Add(_aiButton);
        void PositionAi(){_aiButton.Location=new Point(Math.Max(12,_desktop.ClientSize.Width-_aiButton.Width-20),Math.Max(12,_desktop.ClientSize.Height-_aiButton.Height-18));_aiPanel.Location=new Point(Math.Max(12,_desktop.ClientSize.Width-_aiPanel.Width-20),Math.Max(12,_aiButton.Top-_aiPanel.Height-8));}
        _desktop.Resize+=(_,_)=>PositionAi();PositionAi();_aiButton.BringToFront();

        Controls.Add(_desktop);
        Controls.Add(menu);
        Controls.Add(_statusBar);
    }

    private async Task AnswerAiAsync(string question)
    {
        try
        {
            IReadOnlyList<InvoiceListItem> invoices=await _tradeTransactions.GetInvoicesAsync(_userSession.Current.CompanyId);
            string normalized=question.Trim();
            var matches=invoices.Where(x=>normalized.Contains(x.InvoiceNumber,StringComparison.CurrentCultureIgnoreCase)||normalized.Contains(x.AccountCode,StringComparison.CurrentCultureIgnoreCase)||normalized.Contains(x.AccountName,StringComparison.CurrentCultureIgnoreCase)).Take(5).ToList();
            if(matches.Count==0){_aiPanel.AddAnswer("Bu ifadeyle eşleşen bir fatura bulamadım. Fatura numarası, cari kodu veya müşteri unvanını biraz daha açık yazabilirsiniz.");return;}
            _aiPanel.AddAnswer(string.Join(Environment.NewLine,matches.Select(x=>$"{x.InvoiceNumber} • {x.AccountName} • {x.GrandTotal:N2} {x.CurrencyCode} • {x.Status}")));
        }
        catch(Exception ex){_aiPanel.AddAnswer($"Arama tamamlanamadı: {ex.Message}");}
    }

    private static (string Server, string DatabaseUser) ReadConnectionIdentity(string connectionString)
    {
        var values = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0].Trim(), part => part[1].Trim(), StringComparer.OrdinalIgnoreCase);
        string host = values.GetValueOrDefault("Server", values.GetValueOrDefault("Data Source", "Bilinmiyor"));
        string hostName = host.Split('\\', ',')[0];
        string ip = hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    hostName.Equals(".", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : ResolveIp(hostName);
        bool trusted = values.TryGetValue("Trusted_Connection", out string? trustedValue) &&
                       trustedValue.Equals("True", StringComparison.OrdinalIgnoreCase);
        string databaseUser = trusted ? $"Windows Auth: {Environment.UserName}" : values.GetValueOrDefault("User Id", "SQL");
        return ($"{host} • {ip}", databaseUser);
    }

    private static string ResolveIp(string host)
    {
        try
        {
            return Dns.GetHostAddresses(host).FirstOrDefault(address =>
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? host;
        }
        catch { return host; }
    }

    private void HandleMenuSelection(string key)
    {
        if (key == "desktop")
        {
            foreach (R3ModuleWindow window in _windows.Values)
                window.Visible = false;
            _statusBar.SetActiveTask(null);
            return;
        }

        OpenModule(key);
    }

    private void OpenModule(string key)
    {
        if (_windows.TryGetValue(key, out R3ModuleWindow? existing))
        {
            existing.RestoreFromTaskbar();
            _statusBar.AddOrActivateTask(key, existing.WindowTitle, existing.RestoreFromTaskbar);
            return;
        }

        ModuleDefinition definition = GetModuleDefinition(key);
        Control content = WorkspaceFactory.Create(key);
        if (content is R3SmartTable table)
        {
            WireTableActions(key, table);
            _ = LoadWorkspaceDataAsync(key, table);
        }
        if (content is DashboardWorkspace dashboard)
        {
            dashboard.ModuleRequested += (_, moduleKey) => OpenModule(moduleKey);
            dashboard.PeriodRequested += async (_,period) => await LoadDashboardAsync(dashboard,period.From,period.To);
            dashboard.NewNoteRequested += async (_,date) =>
            {
                using var dialog=new AgendaNoteDialog(date);
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                await _dashboardService.AddNoteAsync(_userSession.Current.CompanyId,_userSession.Current.BranchId,_userSession.Current.UserId,dialog.EventDate,dialog.NoteTitle,dialog.NoteDescription);
                DateTime month=new(dialog.EventDate.Year,dialog.EventDate.Month,1);
                await LoadDashboardAsync(dashboard,month,month.AddMonths(1));
                _toasts.Show(ToastKind.Success,"Ajanda notu eklendi","Not seçilen tarihe kaydedildi.");
            };
            DateTime month=new(DateTime.Today.Year,DateTime.Today.Month,1);
            _=LoadDashboardAsync(dashboard,month,month.AddMonths(1));
        }
        if (content is ManagerWorkspace manager)
            manager.ModuleRequested += (_, moduleKey) => OpenModule(moduleKey);
        var window = new R3ModuleWindow(
            _desktop,
            key,
            definition.Title,
            definition.Glyph,
            content);

        window.Closed += (_, _) =>
        {
            _windows.Remove(key);
            _statusBar.RemoveTask(key);
        };
        window.Minimized += (_, _) => _statusBar.SetMinimized(key);
        window.Activated += (_, _) => _statusBar.SetActiveTask(key);

        _windows.Add(key, window);
        _desktop.Controls.Add(window);
        window.OpenMaximized();
        _statusBar.AddOrActivateTask(key, definition.ShortTitle, window.RestoreFromTaskbar);
    }

    private async Task LoadDashboardAsync(DashboardWorkspace dashboard,DateTime from,DateTime to)
    {
        try{dashboard.SetSnapshot(await _dashboardService.GetSnapshotAsync(_userSession.Current.CompanyId,from,to));}
        catch(Exception ex){_toasts.Show(ToastKind.Error,"Gösterge paneli yüklenemedi",ex.Message,6000);}
    }

    private void WireTableActions(string key, R3SmartTable table)
    {
        table.CommandRequested += (_, command) => HandleModuleCommand(key, command);
        table.NewRequested += (_, _) =>
        {
            if (key is "accounts" or "customers" or "suppliers")
            {
                using var dialog = new R3ModalDialog();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _toasts.Show(ToastKind.Success, "Kayıt başarılı", "Cari kart başarıyla kaydedildi.");
                return;
            }

            _toasts.Show(ToastKind.Information, "Yeni kayıt", "Kayıt ekranı sonraki modül adımında bağlanacak.");
        };
        table.EditRequested += (_, _) =>
        {
            if (!table.HasSelectedRow)
                _toasts.Show(ToastKind.Warning, "Kayıt seçilmedi", "Düzenlemek için tablodan bir kayıt seçin.");
            else if (table.SelectedDataItem is ProductListItem product)
                _ = OpenProductEditorAsync(key, product);
            else if (table.SelectedDataItem is AccountListItem account)
                _ = OpenAccountEditorAsync(key, account);
            else if (table.SelectedDataItem is InvoiceListItem invoice)
                _ = OpenInvoiceEditorAsync(key, invoice);
            else if (table.SelectedDataItem is FinanceListItem finance)
                _ = OpenFinanceEditorAsync(key, finance);
        };
        table.DeleteRequested += (_, _) =>
        {
            if (!table.HasSelectedRow)
                _toasts.Show(ToastKind.Warning, "Kayıt seçilmedi", "Silmek için tablodan bir kayıt seçin.");
        };
        table.RefreshRequested += async (_, _) => await LoadWorkspaceDataAsync(key, table);
    }

    private async void HandleModuleCommand(string moduleKey, string command)
    {
        if (command is "new-account" or "account-card")
        {
            AccountListItem? selected = command == "account-card" ? SelectedItem<AccountListItem>(moduleKey) : null;
            if (command == "account-card" && selected is null) { _toasts.Show(ToastKind.Warning,"Cari seçilmedi","Güncellemek için bir cari seçin."); return; }
            await OpenAccountEditorAsync(moduleKey, selected);
            return;
        }

        if (command == "new-product")
        {
            await OpenProductEditorAsync(moduleKey, null);
            return;
        }

        if (command == "stock-card")
        {
            ProductListItem? selected = SelectedItem<ProductListItem>(moduleKey);
            if (selected is null) { _toasts.Show(ToastKind.Warning,"Ürün seçilmedi","Güncellemek için bir ürün seçin."); return; }
            await OpenProductEditorAsync(moduleKey, selected);
            return;
        }

        if (command == "variant")
        {
            OpenModule("variants");
            return;
        }

        if (command == "barcode")
        {
            OpenModule("barcodes");
            return;
        }

        if (command is "new-sales-invoice" or "open-invoice")
        {
            InvoiceListItem? selected = command == "open-invoice" ? SelectedItem<InvoiceListItem>(moduleKey) : null;
            if (command == "open-invoice" && selected is null) { _toasts.Show(ToastKind.Warning,"Fatura seçilmedi","Güncellemek için bir fatura seçin."); return; }
            await OpenInvoiceEditorAsync(moduleKey, selected);
            return;
        }

        if (command is "collection" or "payment" or "open-finance")
        {
            FinanceListItem? selected = command == "open-finance" ? SelectedItem<FinanceListItem>(moduleKey) : null;
            if (command == "open-finance" && selected is null) { _toasts.Show(ToastKind.Warning,"İşlem seçilmedi","Güncellemek için bir finans işlemi seçin."); return; }
            await OpenFinanceEditorAsync(moduleKey, selected);
            return;
        }

        if (command is "post-invoice" or "cancel-invoice")
        {
            InvoiceListItem? selected = SelectedItem<InvoiceListItem>(moduleKey);
            if (selected is null) { _toasts.Show(ToastKind.Warning,"Fatura seçilmedi","İşlem için bir taslak fatura seçin."); return; }
            bool post = command == "post-invoice";
            if (!R3ConfirmationDialog.Confirm(this,post?"Faturayı Onayla":"Faturayı İptal Et",
                post?"Stok ve cari hareketleri oluşturulacak. Onaylanan belge doğrudan değiştirilemez.":"Taslak belge iptal durumuna alınacak.",
                post?"Onayla":"İptal Et")) return;
            try
            {
                if(post) await _tradeTransactions.PostInvoiceAsync(selected.InvoiceId,_userSession.Current.UserId);
                else await _tradeTransactions.CancelInvoiceAsync(selected.InvoiceId,_userSession.Current.UserId);
                _toasts.Show(ToastKind.Success,post?"Fatura onaylandı":"Fatura iptal edildi",post?"Stok ve cari hareketleri atomik olarak oluşturuldu.":"Taslak belge iptal edildi.");
                if(_windows.TryGetValue(moduleKey,out R3ModuleWindow? invoiceWindow))await ReloadTableInWindowAsync(moduleKey,invoiceWindow);
            }
            catch(Exception ex){_toasts.Show(ToastKind.Error,"Fatura işlemi tamamlanamadı",ex.Message,6000);}
            return;
        }

        if (command is "post-finance" or "cancel-finance")
        {
            FinanceListItem? selected = SelectedItem<FinanceListItem>(moduleKey);
            if(selected is null){_toasts.Show(ToastKind.Warning,"Finans işlemi seçilmedi","İşlem için bir taslak kayıt seçin.");return;}
            bool post=command=="post-finance";
            if(!R3ConfirmationDialog.Confirm(this,post?"Finans İşlemini Onayla":"Finans İşlemini İptal Et",
                post?"Cari ve kasa/banka hareketleri oluşturulacak.":"Taslak finans kaydı iptal edilecek.",post?"Onayla":"İptal Et"))return;
            try
            {
                if(post)await _tradeTransactions.PostFinanceAsync(selected.FinancialTransactionId,_userSession.Current.UserId);
                else await _tradeTransactions.CancelFinanceAsync(selected.FinancialTransactionId,_userSession.Current.UserId);
                _toasts.Show(ToastKind.Success,post?"Finans işlemi onaylandı":"Finans işlemi iptal edildi",post?"Cari ve finans hareketleri oluşturuldu.":"Taslak kayıt iptal edildi.");
                if(_windows.TryGetValue(moduleKey,out R3ModuleWindow? financeWindow))await ReloadTableInWindowAsync(moduleKey,financeWindow);
            }
            catch(Exception ex){_toasts.Show(ToastKind.Error,"Finans işlemi tamamlanamadı",ex.Message,6000);}
            return;
        }

        if(command=="reverse-invoice")
        {
            InvoiceListItem? selected=SelectedItem<InvoiceListItem>(moduleKey);
            if(selected is null){_toasts.Show(ToastKind.Warning,"Fatura seçilmedi","Ters kayıt için onaylı bir fatura seçin.");return;}
            if(!R3ConfirmationDialog.Confirm(this,"Fatura Ters Kaydı","Stok ve cari hareketleri karşı kayıtlarla geri alınacak. Bu işlem silme yapmaz.","Ters Kayıt"))return;
            try{await _tradeTransactions.ReverseInvoiceAsync(selected.InvoiceId,_userSession.Current.UserId,"Kullanıcı tarafından ters kayıt");_toasts.Show(ToastKind.Success,"Ters kayıt tamamlandı","Faturanın stok ve cari etkileri geri alındı.");if(_windows.TryGetValue(moduleKey,out R3ModuleWindow? w))await ReloadTableInWindowAsync(moduleKey,w);}catch(Exception ex){_toasts.Show(ToastKind.Error,"Ters kayıt yapılamadı",ex.Message,6000);}
            return;
        }

        if(command=="reverse-finance")
        {
            FinanceListItem? selected=SelectedItem<FinanceListItem>(moduleKey);
            if(selected is null){_toasts.Show(ToastKind.Warning,"Finans işlemi seçilmedi","Ters kayıt için onaylı bir işlem seçin.");return;}
            if(!R3ConfirmationDialog.Confirm(this,"Finans Ters Kaydı","Cari ve kasa/banka etkileri karşı kayıtlarla geri alınacak.","Ters Kayıt"))return;
            try{await _tradeTransactions.ReverseFinanceAsync(selected.FinancialTransactionId,_userSession.Current.UserId,"Kullanıcı tarafından ters kayıt");_toasts.Show(ToastKind.Success,"Ters kayıt tamamlandı","Finans hareketinin etkileri geri alındı.");if(_windows.TryGetValue(moduleKey,out R3ModuleWindow? w))await ReloadTableInWindowAsync(moduleKey,w);}catch(Exception ex){_toasts.Show(ToastKind.Error,"Ters kayıt yapılamadı",ex.Message,6000);}
            return;
        }

        if(command=="allocate-payment")
        {
            InvoiceListItem? selected=SelectedItem<InvoiceListItem>(moduleKey);
            if(selected is null){_toasts.Show(ToastKind.Warning,"Fatura seçilmedi","Ödeme eşleştirmek için onaylı bir fatura seçin.");return;}
            await using var dialog=new PaymentAllocationDialog(_tradeTransactions,_userSession.Current,selected);
            if(await dialog.ShowAsync(this)==DialogResult.OK){_toasts.Show(ToastKind.Success,"Ödeme eşleştirildi","Faturanın ödenen ve kalan tutarı güncellendi.");if(_windows.TryGetValue(moduleKey,out R3ModuleWindow? w))await ReloadTableInWindowAsync(moduleKey,w);}
            return;
        }

        if(command=="invoice-movements"){OpenModule("stock-movements");return;}

        string message = command switch
        {
            "new-product" => "Yeni stok kartı ve varyant bilgileri açılıyor.",
            "variant" => "Ürüne bağlı renk ve beden varyantları açılıyor.",
            "barcode" => "Barkod yönetimi açılıyor.",
            "stock-card" => "Seçili ürünün stok kartı açılıyor.",
            "movement" => "Seçili kaydın hareket geçmişi açılıyor.",
            "price" => "Fiyat güncelleme işlemi açılıyor.",
            "new-sales-invoice" => "Yeni satış faturası hazırlanıyor.",
            "open-invoice" => "Seçili fatura açılıyor.",
            "dispatch-to-invoice" => "İrsaliyeden fatura oluşturma ekranı açılıyor.",
            "return-invoice" => "Kaynak belgeye bağlı iade işlemi açılıyor.",
            "payment-plan" => "Faturanın ödeme planı açılıyor.",
            "send-einvoice" => "e-Fatura gönderim kontrolü başlatılıyor.",
            "collection" => "Yeni tahsilat işlemi açılıyor.",
            "payment" => "Yeni ödeme işlemi açılıyor.",
            "cash-transfer" => "Kasalar arası virman işlemi açılıyor.",
            "bank-transfer" => "Bankalar arası virman işlemi açılıyor.",
            "cash-close" => "Kasa kapanış kontrolü açılıyor.",
            "statement" => "Cari ekstre açılıyor.",
            "reconciliation" => "Cari mutabakat ekranı açılıyor.",
            _ => "Modüle özel işlem açılıyor."
        };
        _toasts.Show(ToastKind.Information, "İşlem", message);
    }

    private async Task LoadWorkspaceDataAsync(string key, R3SmartTable table)
    {
        try
        {
            object? data = key switch
            {
                "inventory" or "products" => await _masterData.GetProductsAsync(_userSession.Current.CompanyId),
                "variants" => await _masterData.GetVariantsAsync(_userSession.Current.CompanyId),
                "accounts" => await _masterData.GetAccountsAsync(_userSession.Current.CompanyId),
                "customers" => await _masterData.GetAccountsAsync(_userSession.Current.CompanyId, 1),
                "suppliers" => await _masterData.GetAccountsAsync(_userSession.Current.CompanyId, 2),
                "invoice" or "sales-invoices" => await _tradeTransactions.GetInvoicesAsync(_userSession.Current.CompanyId),
                "finance" => await _tradeTransactions.GetFinanceTransactionsAsync(_userSession.Current.CompanyId),
                _ => null
            };
            if (data is not null && !table.IsDisposed)
                table.SetDataSource(data);
        }
        catch (Exception ex)
        {
            _toasts.Show(ToastKind.Error, "Liste yüklenemedi", ex.Message, 5000);
        }
    }

    private async Task ReloadTableInWindowAsync(string key, R3ModuleWindow window)
    {
        R3SmartTable? table = FindControl<R3SmartTable>(window);
        if (table is not null)
            await LoadWorkspaceDataAsync(key, table);
    }

    private T? SelectedItem<T>(string moduleKey) where T : class =>
        _windows.TryGetValue(moduleKey, out R3ModuleWindow? window)
            ? FindControl<R3SmartTable>(window)?.SelectedDataItem as T
            : null;

    private async Task OpenProductEditorAsync(string moduleKey, ProductListItem? item)
    {
        using var dialog = new ProductEditorDialog(_masterData, _userSession.Current, item);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _toasts.Show(ToastKind.Success, item is null ? "Ürün kaydedildi" : "Ürün güncellendi", "Ürün kartı ve varyant bilgileri MSSQL'e kaydedildi.");
        if (_windows.TryGetValue(moduleKey, out R3ModuleWindow? window))
            await ReloadTableInWindowAsync(moduleKey, window);
    }

    private async Task OpenAccountEditorAsync(string moduleKey, AccountListItem? item)
    {
        using var dialog = new AccountEditorDialog(_masterData, _userSession.Current, item);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _toasts.Show(ToastKind.Success, item is null ? "Cari kaydedildi" : "Cari güncellendi", "Cari kart ve adres bilgileri MSSQL'e kaydedildi.");
        if (_windows.TryGetValue(moduleKey, out R3ModuleWindow? window))
            await ReloadTableInWindowAsync(moduleKey, window);
    }

    private async Task OpenInvoiceEditorAsync(string moduleKey, InvoiceListItem? item)
    {
        await using var dialog = new InvoiceEditorDialog(_tradeTransactions, _userSession.Current, item);
        if (await dialog.ShowAsync(this) != DialogResult.OK) return;
        _toasts.Show(ToastKind.Success,item is null?"Fatura kaydedildi":"Fatura güncellendi","Fatura başlık ve satırları MSSQL'e kaydedildi.");
        if (_windows.TryGetValue(moduleKey,out R3ModuleWindow? window)) await ReloadTableInWindowAsync(moduleKey,window);
    }

    private async Task OpenFinanceEditorAsync(string moduleKey, FinanceListItem? item)
    {
        await using var dialog = new FinanceEditorDialog(_tradeTransactions, _userSession.Current, item);
        if (await dialog.ShowAsync(this) != DialogResult.OK) return;
        _toasts.Show(ToastKind.Success,item is null?"Finans işlemi kaydedildi":"Finans işlemi güncellendi","Kasa/banka işlemi MSSQL'e kaydedildi.");
        if (_windows.TryGetValue(moduleKey,out R3ModuleWindow? window)) await ReloadTableInWindowAsync(moduleKey,window);
    }

    private static T? FindControl<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) return match;
            T? nested = FindControl<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static ModuleDefinition GetModuleDefinition(string key)
    {
        R3NavigationItem? item = R3NavigationCatalog.Find(key);
        return item is null
            ? new ModuleDefinition("R3 Çalışma Alanı", "R3", "R3 çalışma alanı.", R3Glyph.Desktop)
            : new ModuleDefinition(item.Title, item.Title, $"{item.Title} çalışma alanı.", item.Glyph);
    }

    private async Task CheckDatabaseAsync()
    {
        try
        {
            bool connected = await _connectionVerifier.CanConnectAsync();
            _statusBar.SetDatabaseState(connected);
            if (!connected)
                _toasts.Show(ToastKind.Error, "Veritabanı bağlantısı", "EngineeringERP sunucusuna bağlantı kurulamadı.", 6000);
        }
        catch (Exception)
        {
            _statusBar.SetDatabaseState(false);
            _toasts.Show(ToastKind.Error, "Bağlantı hatası", "Sunucu ve ağ ayarlarını kontrol edin.", 6000);
        }
    }

    private sealed record ModuleDefinition(
        string Title,
        string ShortTitle,
        string Description,
        R3Glyph Glyph);
}
