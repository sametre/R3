using Krypton.Toolkit;
using R3.Application.Authentication;
using R3.Application.MasterData;
using R3.Desktop.Branding;
using R3.Desktop.Theme;

namespace R3.Desktop.Dialogs;

internal sealed class AccountEditorDialog : KryptonForm
{
    private readonly IRetailMasterDataService _service; private readonly UserSessionData _session;
    private readonly AccountListItem? _editing;
    private readonly Dictionary<string,KryptonTextBox> _text=new();
    private readonly KryptonComboBox _type=new(),_currency=new(),_paymentPlan=new();
    private readonly KryptonNumericUpDown _risk=new(){DecimalPlaces=2,Maximum=100000000,ThousandsSeparator=true},_term=new(){Maximum=3650};
    private readonly CheckBox _eInvoice=new(){Text="e-Fatura mükellefi"},_active=new(){Text="Cari aktif",Checked=true};
    private readonly Label _message=new(); private readonly KryptonButton _save;

    public AccountEditorDialog(IRetailMasterDataService service,UserSessionData session,AccountListItem? editing=null)
    {
        _service=service;_session=session;_editing=editing;_save=R3Theme.CreatePrimaryButton(editing is null?"Cariyi Kaydet":"Değişiklikleri Kaydet");
        Text=editing is null?"R3 • Yeni Cari Kart":$"R3 • Cari Güncelle • {editing.AccountCode}";Icon=R3Branding.AppIcon;StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(980,690);MinimumSize=new Size(900,640);
        var header=new Panel{Dock=DockStyle.Top,Height=68,BackColor=Color.White,Padding=new Padding(22,10,22,8)};
        header.Controls.Add(new Label{Text="Cari Hesap Kartı",Dock=DockStyle.Top,Height=32,Font=new Font("Segoe UI Semibold",16,FontStyle.Bold),ForeColor=R3Colors.Text});
        header.Controls.Add(new Label{Text="Müşteri, tedarikçi, vergi, risk, vade, adres ve e-Fatura bilgileri",Dock=DockStyle.Bottom,Height=22,ForeColor=R3Colors.MutedText});
        var tabs=new TabControl{Dock=DockStyle.Fill};
        tabs.TabPages.Add(Page("Genel ve Ticari",General()));
        tabs.TabPages.Add(Page("Vergi ve E-Fatura",Tax()));
        tabs.TabPages.Add(Page("Adres ve İletişim",Contact()));
        tabs.TabPages.Add(Page("Risk ve Ödeme",Risk()));
        var footer=new Panel{Dock=DockStyle.Bottom,Height=58,BackColor=Color.White,Padding=new Padding(16,10,16,10)};
        _save.Dock=DockStyle.Right;_save.Width=125;_save.Click+=async(_,_)=>await SaveAsync();
        var cancel=new KryptonButton{Text="Vazgeç",Dock=DockStyle.Right,Width=90};cancel.Click+=(_,_)=>DialogResult=DialogResult.Cancel;
        _message.Dock=DockStyle.Fill;_message.ForeColor=Color.Firebrick;_message.TextAlign=ContentAlignment.MiddleLeft;
        footer.Controls.Add(_message);footer.Controls.Add(cancel);footer.Controls.Add(_save);
        Controls.Add(tabs);Controls.Add(footer);Controls.Add(header);AcceptButton=_save;CancelButton=cancel;
        _type.DropDownStyle=ComboBoxStyle.DropDownList;_type.DataSource=new[]{new{Id=(byte)1,Name="Müşteri"},new{Id=(byte)2,Name="Tedarikçi"},new{Id=(byte)3,Name="Müşteri + Tedarikçi"},new{Id=(byte)4,Name="Personel"},new{Id=(byte)5,Name="Diğer"}};_type.ValueMember="Id";_type.DisplayMember="Name";
        _currency.DropDownStyle=ComboBoxStyle.DropDownList;_currency.DataSource=new[]{"TRY","USD","EUR","GBP"};
    }
    protected override async void OnShown(EventArgs e){base.OnShown(e);try{var x=await _service.GetLookupsAsync(_session.CompanyId);var plans=x.PaymentPlans.ToList();plans.Insert(0,new LookupItem(0,"","Seçilmedi"));_paymentPlan.DataSource=plans;_paymentPlan.DisplayMember="DisplayText";_paymentPlan.ValueMember="Id";if(_editing is not null)Populate();}catch(Exception ex){_message.Text=ex.Message;}}
    private Control General(){var t=Table();Add(t,0,0,"Cari Kodu",T("code"));Add(t,1,0,"Cari Türü",_type);Add(t,0,1,"Ticari Unvan / Ad Soyad",T("legal"),2);Add(t,0,2,"Kısa / Ticari Ad",T("trade"));Add(t,1,2,"Aktiflik",_active);Add(t,0,3,"Fiyat Listesi",T("price"));Add(t,1,3,"İskonto Grubu",T("discount"));Add(t,0,4,"Notlar",T("notes",true),2);return t;}
    private Control Tax(){var t=Table();Add(t,0,0,"Vergi Dairesi",T("taxoffice"));Add(t,1,0,"Vergi Numarası",T("taxno"));Add(t,0,1,"T.C. Kimlik Numarası",T("identity"));Add(t,1,1,"MERSİS Numarası",T("mersis"));Add(t,0,2,"e-Fatura",_eInvoice);Add(t,1,2,"e-Fatura Etiketi / Alias",T("alias"));return t;}
    private Control Contact(){var t=Table();Add(t,0,0,"Telefon",T("phone"));Add(t,1,0,"E-posta",T("email"));Add(t,0,1,"İl",T("city"));Add(t,1,1,"İlçe",T("district"));Add(t,0,2,"Fatura Adresi",T("address",true),2);return t;}
    private Control Risk(){var t=Table();Add(t,0,0,"Para Birimi",_currency);Add(t,1,0,"Ödeme Planı",_paymentPlan);Add(t,0,1,"Risk Limiti",_risk);Add(t,1,1,"Vade Günü",_term);return t;}
    private async Task SaveAsync()
    {
        _message.Text="";if(string.IsNullOrWhiteSpace(V("code"))||string.IsNullOrWhiteSpace(V("legal"))){_message.Text="Cari kodu ve unvan zorunludur.";return;}
        if(V("identity") is {Length:>0} id&&id.Length!=11){_message.Text="T.C. Kimlik Numarası 11 haneli olmalıdır.";return;}
        _save.Enabled=false;
        try
        {
            var r=new AccountSaveRequest(_session.CompanyId,_session.UserId,V("code")!,_type.SelectedValue is byte accountType?accountType:(byte)1,V("legal")!,V("trade"),V("taxoffice"),V("taxno"),V("identity"),V("mersis"),
                _currency.Text,_risk.Value,(short)_term.Value,V("price"),V("discount"),_paymentPlan.SelectedValue is int p&&p>0?p:null,V("phone"),V("email"),V("city"),V("district"),V("address"),_eInvoice.Checked,V("alias"),V("notes"),_active.Checked,_editing?.AccountId);
            await _service.SaveAccountAsync(r);DialogResult=DialogResult.OK;Close();
        }catch(Exception ex){_message.Text=ex.Message.Contains("UQ_",StringComparison.OrdinalIgnoreCase)?"Cari kodu daha önce kullanılmış.":$"Kayıt yapılamadı: {ex.Message}";}finally{_save.Enabled=true;}
    }
    private KryptonTextBox T(string key,bool multiline=false){var x=new KryptonTextBox{Multiline=multiline};_text[key]=x;return x;}
    private void Populate(){if(_editing is null)return;_text["code"].Text=_editing.AccountCode;_text["legal"].Text=_editing.LegalName;_text["trade"].Text=_editing.TradeName??"";_text["taxoffice"].Text=_editing.TaxOffice??"";_text["taxno"].Text=_editing.TaxNumber??"";_text["identity"].Text=_editing.IdentityNumber??"";_text["mersis"].Text=_editing.MersisNumber??"";_text["city"].Text=_editing.City??"";_text["district"].Text=_editing.District??"";_text["address"].Text=_editing.Address??"";_text["phone"].Text=_editing.Phone??"";_text["email"].Text=_editing.Email??"";_text["price"].Text=_editing.PriceListCode??"";_text["discount"].Text=_editing.DiscountGroupCode??"";_text["alias"].Text=_editing.EInvoiceAlias??"";_text["notes"].Text=_editing.Notes??"";_risk.Value=_editing.CreditLimit;_term.Value=_editing.PaymentTermDays;_currency.SelectedItem=_editing.CurrencyCode;_paymentPlan.SelectedValue=_editing.PaymentPlanId??0;_type.SelectedValue=_editing.AccountTypeId;_eInvoice.Checked=_editing.IsEInvoiceUser;_active.Checked=_editing.Status=="Aktif";}
    private string? V(string key)=>_text.TryGetValue(key,out var x)&&!string.IsNullOrWhiteSpace(x.Text)?x.Text.Trim():null;
    private static TableLayoutPanel Table(){var t=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=10,Padding=new Padding(20)};t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));for(int i=0;i<5;i++){t.RowStyles.Add(new RowStyle(SizeType.Absolute,24));t.RowStyles.Add(new RowStyle(SizeType.Absolute,56));}return t;}
    private static void Add(TableLayoutPanel t,int col,int row,string label,Control editor,int span=1){t.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.BottomLeft,ForeColor=R3Colors.MutedText,Font=new Font("Segoe UI",8,FontStyle.Bold)},col,row*2);editor.Dock=DockStyle.Fill;editor.Margin=new Padding(0,5,12,6);t.Controls.Add(editor,col,row*2+1);if(span>1){t.SetColumnSpan(editor,span);}}
    private static TabPage Page(string title,Control content){var p=new TabPage(title){BackColor=Color.White,Padding=new Padding(10)};content.Dock=DockStyle.Fill;p.Controls.Add(content);return p;}
}
