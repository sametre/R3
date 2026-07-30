using R3.Desktop.Theme;

namespace R3.Desktop.Workspaces;

internal sealed class ManagerWorkspace : Panel
{
    public event EventHandler<string>? ModuleRequested;

    public ManagerWorkspace()
    {
        Dock = DockStyle.Fill;
        BackColor = R3Colors.Canvas;
        Padding = new Padding(18);

        var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = R3Colors.Canvas };
        header.Controls.Add(new Label
        {
            Text = "Merkez Yönetici Paneli", Dock = DockStyle.Top, Height = 34,
            Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold), ForeColor = R3Colors.Text
        });
        header.Controls.Add(new Label
        {
            Text = "Tüm mağazalar, finans, stok ve kritik operasyonlar tek merkezde",
            Dock = DockStyle.Bottom, Height = 24, Font = new Font("Segoe UI", 9), ForeColor = R3Colors.MutedText
        });

        var kpis = new TableLayoutPanel { Dock = DockStyle.Top, Height = 112, ColumnCount = 6, Padding = new Padding(0, 8, 0, 10) };
        for (int i = 0; i < 6; i++) kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666F));
        kpis.Controls.Add(Kpi("BUGÜNKÜ SATIŞ", "0,00 TL", "Satış hareketleri"), 0, 0);
        kpis.Controls.Add(Kpi("BRÜT KÂR", "0,00 TL", "Güncel dönem"), 1, 0);
        kpis.Controls.Add(Kpi("FİŞ SAYISI", "0", "Tüm mağazalar"), 2, 0);
        kpis.Controls.Add(Kpi("ORTALAMA SEPET", "0,00 TL", "Günlük ortalama"), 3, 0);
        kpis.Controls.Add(Kpi("KRİTİK STOK", "0", "Ürün / varyant"), 4, 0);
        kpis.Controls.Add(Kpi("BEKLEYEN ONAY", "0", "Yönetici işlemi"), 5, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 6, 0, 0) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        content.Controls.Add(Section("Mağaza Performansı", "Ciro, fiş, sepet ve kârlılık karşılaştırmaları\n\nHenüz görüntülenecek canlı satış verisi bulunmuyor.", "reports"), 0, 0);
        content.Controls.Add(Section("Kritik Uyarılar", "• Kritik stok\n• Kapanmayan kasa\n• Bekleyen transfer\n• POS eşleşme farkı\n• Fiyat etiketi farkı", "manager-alerts"), 1, 0);
        content.Controls.Add(Section("Görev ve Onaylar", "• İndirim onayları\n• İade onayları\n• Sayım farkları\n• Kasa farkları\n• Belge iptalleri", "manager-approvals"), 2, 0);

        Controls.Add(content);
        Controls.Add(kpis);
        Controls.Add(header);
    }

    private static Control Kpi(string title, string value, string subtitle)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(13, 9, 13, 7) };
        card.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Bottom, Height = 20, ForeColor = R3Colors.MutedText, Font = new Font("Segoe UI", 8) });
        card.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, ForeColor = R3Colors.Primary, Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        card.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 20, ForeColor = R3Colors.MutedText, Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold) });
        return card;
    }

    private Control Section(string title, string description, string key)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(16) };
        var open = new Button { Text = "Aç →", Dock = DockStyle.Bottom, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = R3Colors.Primary, Font = new Font("Segoe UI Semibold", 8.5F) };
        open.FlatAppearance.BorderColor = R3Colors.Border;
        open.Click += (_, _) => ModuleRequested?.Invoke(this, key);
        card.Controls.Add(open);
        card.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, ForeColor = R3Colors.MutedText, Font = new Font("Segoe UI", 9), Padding = new Padding(0, 12, 0, 0) });
        card.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 30, ForeColor = R3Colors.Text, Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold) });
        return card;
    }
}
