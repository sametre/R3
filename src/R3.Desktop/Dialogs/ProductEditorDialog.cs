using System.ComponentModel;
using Krypton.Toolkit;
using R3.Application.Authentication;
using R3.Application.MasterData;
using R3.Desktop.Branding;
using R3.Desktop.Theme;

namespace R3.Desktop.Dialogs;

internal sealed class ProductEditorDialog : KryptonForm
{
    private readonly IRetailMasterDataService _service;
    private readonly UserSessionData _session;
    private readonly ProductListItem? _editing;
    private readonly KryptonTextBox _code = new(), _name = new(), _shortName = new(), _barcode = new(), _description = new();
    private readonly KryptonTextBox _shelf = new(), _aisle = new(), _manufacturer = new(), _origin = new();
    private readonly KryptonComboBox _type = new(), _unit = new(), _brand = new(), _category = new(), _group = new();
    private readonly KryptonNumericUpDown _vat = Number(2,100), _purchase = Number(), _sales = Number(), _wholesale = Number(), _campaign = Number();
    private readonly KryptonNumericUpDown _minStock = Number(3), _maxStock = Number(3), _critical = Number(3), _warranty = Number(0,1200);
    private readonly CheckBox _trackLot = new() { Text = "Parti / lot takibi" }, _trackSerial = new() { Text = "Seri numarası takibi" }, _active = new() { Text = "Ürün aktif", Checked = true };
    private readonly BindingList<VariantRow> _variants = [];
    private readonly DataGridView _variantGrid = new();
    private readonly Label _message = new();
    private readonly KryptonButton _save;

    public ProductEditorDialog(IRetailMasterDataService service, UserSessionData session, ProductListItem? editing = null)
    {
        _service = service; _session = session; _editing = editing;
        _save = R3Theme.CreatePrimaryButton("Ürünü Kaydet");
        Text = editing is null ? "R3 • Yeni Ürün Kartı" : $"R3 • Ürün Güncelle • {editing.ProductCode}"; Icon = R3Branding.AppIcon; StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040,700); ClientSize = new Size(1180,760);

        var header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White, Padding = new Padding(22,10,22,8) };
        header.Controls.Add(new Label { Text = "Ürün Kartı ve Varyant Yönetimi", Dock = DockStyle.Top, Height = 32, Font = new Font("Segoe UI Semibold",16,FontStyle.Bold), ForeColor=R3Colors.Text });
        header.Controls.Add(new Label { Text = "Genel bilgiler, fiyat, stok kuralları, renk–beden varyantları ve barkodlar", Dock=DockStyle.Bottom,Height=22,Font=new Font("Segoe UI",9),ForeColor=R3Colors.MutedText });

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI",9) };
        tabs.TabPages.Add(Page("Genel Bilgiler", GeneralPage()));
        tabs.TabPages.Add(Page("Fiyat ve Stok", PricePage()));
        tabs.TabPages.Add(Page("Varyantlar", VariantPage()));

        var footer = new Panel { Dock=DockStyle.Bottom,Height=58,BackColor=Color.White,Padding=new Padding(16,10,16,10) };
        _save.Dock=DockStyle.Right; _save.Width=130; _save.Click += async (_,_) => await SaveAsync();
        var cancel=new KryptonButton { Text="Vazgeç",Dock=DockStyle.Right,Width=90 };
        cancel.Click += (_,_) => DialogResult=DialogResult.Cancel;
        _message.Dock=DockStyle.Fill; _message.ForeColor=Color.Firebrick; _message.TextAlign=ContentAlignment.MiddleLeft;
        footer.Controls.Add(_message); footer.Controls.Add(cancel); footer.Controls.Add(_save);
        Controls.Add(tabs); Controls.Add(footer); Controls.Add(header);
        AcceptButton=_save; CancelButton=cancel;
        ConfigureEditors();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            MasterDataLookups x=await _service.GetLookupsAsync(_session.CompanyId);
            Bind(_unit,x.Units,false); Bind(_brand,x.Brands,true); Bind(_category,x.Categories,true); Bind(_group,x.ProductGroups,true);
            if (_editing is not null) PopulateEditing();
        }
        catch(Exception ex) { _message.Text=$"Tanımlar yüklenemedi: {ex.Message}"; }
    }

    private Control GeneralPage()
    {
        var table=FormTable(4,6);
        Add(table,0,0,"Stok / Model Kodu",_code); Add(table,1,0,"Ürün Adı",_name,2); Add(table,3,0,"Kısa Ad",_shortName);
        Add(table,0,1,"Ürün Tipi",_type); Add(table,1,1,"Ana Birim",_unit); Add(table,2,1,"Ana Barkod",_barcode); Add(table,3,1,"Aktiflik",_active);
        Add(table,0,2,"Marka",_brand); Add(table,1,2,"Kategori",_category); Add(table,2,2,"Ürün Grubu",_group); Add(table,3,2,"KDV Oranı",_vat);
        Add(table,0,3,"Üretici Kodu",_manufacturer); Add(table,1,3,"Menşei (TR)",_origin); Add(table,2,3,"Garanti (Ay)",_warranty);
        var location=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 }; location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        _shelf.CueHint.CueHintText="Raf"; _aisle.CueHint.CueHintText="Reyon"; location.Controls.Add(_shelf,0,0); location.Controls.Add(_aisle,1,0);
        Add(table,3,3,"Raf / Reyon",location);
        Add(table,0,4,"Açıklama",_description,4);
        return table;
    }

    private Control PricePage()
    {
        var table=FormTable(4,4);
        Add(table,0,0,"Alış Fiyatı",_purchase); Add(table,1,0,"Satış Fiyatı",_sales); Add(table,2,0,"Toptan Fiyatı",_wholesale); Add(table,3,0,"Kampanyalı Fiyat",_campaign);
        Add(table,0,1,"Minimum Stok",_minStock); Add(table,1,1,"Maksimum Stok",_maxStock); Add(table,2,1,"Kritik Stok",_critical); Add(table,3,1,"Takip Seçenekleri",Flow(_trackLot,_trackSerial));
        return table;
    }

    private Control VariantPage()
    {
        var panel=new Panel { Dock=DockStyle.Fill,Padding=new Padding(12),BackColor=Color.White };
        _variantGrid.Dock=DockStyle.Fill; _variantGrid.AutoGenerateColumns=false; _variantGrid.DataSource=_variants;
        _variantGrid.AllowUserToAddRows=true; _variantGrid.AllowUserToDeleteRows=true; _variantGrid.RowHeadersVisible=false;
        _variantGrid.BackgroundColor=Color.White; _variantGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        Column("VariantCode","VARYANT KODU",150); Column("ColorCode","RENK KODU",90); Column("ColorName","RENK",100);
        Column("SizeCode","BEDEN KODU",90); Column("SizeName","BEDEN",80); Column("Barcode","BARKOD",125);
        Column("PurchasePrice","ALIŞ",90); Column("SalesPrice","SATIŞ",90); Column("WholesalePrice","TOPTAN",90);
        Column("CampaignPrice","KAMPANYA",95); Column("ShelfCode","RAF",70); Column("AisleCode","REYON",70);
        _variantGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName="TrackLot",HeaderText="LOT",Width=50 });
        _variantGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName="TrackSerial",HeaderText="SERİ",Width=50 });
        var tools=new FlowLayoutPanel { Dock=DockStyle.Top,Height=44,Padding=new Padding(0,4,0,4) };
        tools.Controls.Add(SmallButton("+ Varyant Ekle",()=>_variants.Add(new VariantRow())));
        tools.Controls.Add(SmallButton("Renk × Beden Matrisi",GenerateMatrix));
        tools.Controls.Add(new Label { Text="Örnek: TSH-100-M-SIYAH • Her satırın barkod, fiyat ve raf bilgisi ayrıdır.",AutoSize=true,Margin=new Padding(18,9,0,0),ForeColor=R3Colors.MutedText });
        panel.Controls.Add(_variantGrid); panel.Controls.Add(tools); return panel;
    }

    private async Task SaveAsync()
    {
        _message.Text="";
        if(string.IsNullOrWhiteSpace(_code.Text)||string.IsNullOrWhiteSpace(_name.Text)||_unit.SelectedValue is null)
        { _message.Text="Stok kodu, ürün adı ve ana birim zorunludur."; return; }
        if(_variants.Any(v=>string.IsNullOrWhiteSpace(v.VariantCode)))
        { _message.Text="Her varyantın varyant kodu zorunludur."; return; }
        _save.Enabled=false;
        try
        {
            var request=new ProductSaveRequest(_session.CompanyId,_session.UserId,_code.Text.Trim(),_name.Text.Trim(),Null(_shortName),Null(_description),
                _type.SelectedValue is byte productType ? productType : (byte)1,_unit.SelectedValue is int unitId ? unitId : 0,Id(_brand),Id(_category),Id(_group),Null(_barcode),_vat.Value,
                _purchase.Value,_sales.Value,_wholesale.Value,_campaign.Value,_minStock.Value,_maxStock.Value,_critical.Value,Null(_shelf),Null(_aisle),
                Null(_manufacturer),Null(_origin)?.ToUpperInvariant(),(short)_warranty.Value,_trackLot.Checked,_trackSerial.Checked,_active.Checked,
                _variants.Select(v=>new ProductVariantSaveRequest(v.VariantCode.Trim(),v.ColorCode,v.ColorName,v.SizeCode,v.SizeName,v.Barcode,
                    v.PurchasePrice,v.SalesPrice,v.WholesalePrice,v.CampaignPrice,v.ShelfCode,v.AisleCode,v.TrackLot,v.TrackSerial)).ToList(),_editing?.ProductId);
            await _service.SaveProductAsync(request); DialogResult=DialogResult.OK; Close();
        }
        catch(Exception ex) { _message.Text=ex.Message.Contains("UQ_",StringComparison.OrdinalIgnoreCase)?"Stok, varyant veya barkod kodu daha önce kullanılmış.":$"Kayıt yapılamadı: {ex.Message}"; }
        finally { _save.Enabled=true; }
    }

    private void GenerateMatrix()
    {
        using var dialog=new VariantMatrixDialog(_code.Text.Trim());
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        foreach(string color in dialog.Colors)
        foreach(string size in dialog.Sizes)
        {
            string colorCode=Code(color),sizeCode=Code(size);
            _variants.Add(new VariantRow { VariantCode=$"{_code.Text.Trim()}-{sizeCode}-{colorCode}",ColorCode=colorCode,ColorName=color,SizeCode=sizeCode,SizeName=size,
                PurchasePrice=_purchase.Value,SalesPrice=_sales.Value,WholesalePrice=_wholesale.Value,CampaignPrice=_campaign.Value,ShelfCode=_shelf.Text,AisleCode=_aisle.Text,TrackLot=_trackLot.Checked,TrackSerial=_trackSerial.Checked });
        }
    }

    private void ConfigureEditors()
    {
        _type.DropDownStyle=ComboBoxStyle.DropDownList; _type.DataSource=new[] { new { Id=(byte)1,Name="Stok" },new { Id=(byte)2,Name="Hizmet" },new { Id=(byte)3,Name="Hammadde" },new { Id=(byte)4,Name="Yarı Mamul" },new { Id=(byte)5,Name="Mamul" } }; _type.ValueMember="Id";_type.DisplayMember="Name";
        foreach(var c in new[]{_unit,_brand,_category,_group})c.DropDownStyle=ComboBoxStyle.DropDownList;
        _description.Multiline=true; _origin.MaxLength=2;
    }
    private void PopulateEditing()
    {
        if (_editing is null) return;
        _code.Text=_editing.ProductCode;_name.Text=_editing.ProductName;_shortName.Text=_editing.ShortName??"";
        _barcode.Text=_editing.Barcode??"";_vat.Value=_editing.VatRate;_purchase.Value=_editing.PurchasePrice;
        _sales.Value=_editing.SalesPrice;_wholesale.Value=_editing.WholesalePrice;_campaign.Value=_editing.CampaignPrice;
        _critical.Value=_editing.CriticalStockLevel;_shelf.Text=_editing.ShelfCode??"";_aisle.Text=_editing.AisleCode??"";
        _minStock.Value=_editing.MinimumStock;_maxStock.Value=_editing.MaximumStock??0;_description.Text=_editing.Description??"";
        _manufacturer.Text=_editing.ManufacturerCode??"";_origin.Text=_editing.CountryOfOrigin??"";_warranty.Value=_editing.WarrantyMonths;
        _trackLot.Checked=_editing.TrackLot;_trackSerial.Checked=_editing.TrackSerial;
        _active.Checked=_editing.Status=="Aktif";
        foreach(LookupItem unit in (IEnumerable<LookupItem>)_unit.DataSource!)
            if(unit.Code.Equals(_editing.UnitCode,StringComparison.OrdinalIgnoreCase)){_unit.SelectedValue=unit.Id;break;}
        _save.Text="Değişiklikleri Kaydet";
    }
    private void Column(string property,string title,int width)=>_variantGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName=property,HeaderText=title,Width=width });
    private static TabPage Page(string title,Control content){var p=new TabPage(title){BackColor=Color.White,Padding=new Padding(10)};content.Dock=DockStyle.Fill;p.Controls.Add(content);return p;}
    private static TableLayoutPanel FormTable(int columns,int fields){var t=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=columns,RowCount=fields*2,Padding=new Padding(12)};for(int i=0;i<columns;i++)t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));for(int i=0;i<fields;i++){t.RowStyles.Add(new RowStyle(SizeType.Absolute,24));t.RowStyles.Add(new RowStyle(SizeType.Absolute,45));}return t;}
    private static void Add(TableLayoutPanel t,int col,int row,string label,Control editor,int span=1){t.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.BottomLeft,ForeColor=R3Colors.MutedText,Font=new Font("Segoe UI",8,FontStyle.Bold)},col,row*2);editor.Dock=DockStyle.Fill;editor.Margin=new Padding(0,4,10,5);t.Controls.Add(editor,col,row*2+1);if(span>1){t.SetColumnSpan(editor,span);}}
    private static FlowLayoutPanel Flow(params Control[] controls){var f=new FlowLayoutPanel{Dock=DockStyle.Fill};foreach(var c in controls){c.AutoSize=true;c.Margin=new Padding(4,10,10,0);f.Controls.Add(c);}return f;}
    private static KryptonNumericUpDown Number(int decimals=4,decimal max=100000000){return new KryptonNumericUpDown{DecimalPlaces=decimals,Maximum=max,ThousandsSeparator=true};}
    private static void Bind(KryptonComboBox combo,IReadOnlyList<LookupItem> data,bool optional){var list=data.ToList();if(optional)list.Insert(0,new LookupItem(0,"","Seçilmedi"));combo.DataSource=list;combo.DisplayMember="DisplayText";combo.ValueMember="Id";}
    private static int? Id(KryptonComboBox c)=>c.SelectedValue is int id&&id>0?id:null;
    private static string? Null(KryptonTextBox t)=>string.IsNullOrWhiteSpace(t.Text)?null:t.Text.Trim();
    private static string Code(string value)=>new(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static Button SmallButton(string text,Action action){var b=new Button{Text=text,Height=32,AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=R3Colors.Text};b.FlatAppearance.BorderColor=R3Colors.Border;b.Click+=(_,_)=>action();return b;}

    private sealed class VariantRow
    {
        public string VariantCode { get; set; }=""; public string? ColorCode { get; set; } public string? ColorName { get; set; }
        public string? SizeCode { get; set; } public string? SizeName { get; set; } public string? Barcode { get; set; }
        public decimal? PurchasePrice { get; set; } public decimal? SalesPrice { get; set; } public decimal? WholesalePrice { get; set; }
        public decimal? CampaignPrice { get; set; } public string? ShelfCode { get; set; } public string? AisleCode { get; set; }
        public bool TrackLot { get; set; } public bool TrackSerial { get; set; }
    }
}

internal sealed class VariantMatrixDialog : Form
{
    private readonly TextBox _colors=new(){Multiline=true,Dock=DockStyle.Fill,Text="Siyah\r\nBeyaz"};
    private readonly TextBox _sizes=new(){Multiline=true,Dock=DockStyle.Fill,Text="S\r\nM\r\nL"};
    public IReadOnlyList<string> Colors=>Lines(_colors); public IReadOnlyList<string> Sizes=>Lines(_sizes);
    public VariantMatrixDialog(string modelCode)
    {
        Text=$"{modelCode} • Varyant Matrisi";StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(520,360);
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new Padding(16)};
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute,28));table.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        table.Controls.Add(new Label{Text="RENKLER (her satıra bir renk)",Dock=DockStyle.Fill},0,0);table.Controls.Add(new Label{Text="BEDENLER (her satıra bir beden)",Dock=DockStyle.Fill},1,0);
        table.Controls.Add(_colors,0,1);table.Controls.Add(_sizes,1,1);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
        var ok=new Button{Text="Matrisi Oluştur",Width=120,Height=32};ok.Click+=(_,_)=>{if(Colors.Count>0&&Sizes.Count>0)DialogResult=DialogResult.OK;};
        footer.Controls.Add(ok);Controls.Add(table);Controls.Add(footer);AcceptButton=ok;
    }
    private static IReadOnlyList<string> Lines(TextBox box)=>box.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
}
