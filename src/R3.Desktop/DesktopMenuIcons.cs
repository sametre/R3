using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace R3.Desktop;

// Compact line icons with a restrained semantic palette, rendered at 2x for high-DPI menus.
internal static class DesktopMenuIcons
{
    internal enum Kind { Store, Calculator, Box, Cart, Receipt, Bank, Cheque, Tools, Database, Close, Chart, People, Folder, Document, Transfer, Search, Mail, Truck, Tag, Lock, Settings, Home, Refresh, Export, Add, Edit, Print, Calendar }

    internal static Kind Select(string text, bool group)
    {
        var module = text switch
        {
            "Mağaza" => Kind.Store, "Muhasebe" => Kind.Calculator,
            "Stok" => Kind.Box, "Satınalma" => Kind.Cart, "Satış" => Kind.Receipt,
            "Kasa-Banka" or "Finans" => Kind.Bank, "Çek-Senet" => Kind.Cheque,
            "Araçlar" => Kind.Tools, "Asb Tools" => Kind.Database, "Kapat" => Kind.Close,
            "Giriş" => Kind.Home, "R3 Kısayolları" => Kind.Folder,
            _ => (Kind?)null
        };
        if (module.HasValue) return module.Value;
        var label = text.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        bool Has(params string[] words) => words.Any(label.Contains);
        if (Has("yenile")) return Kind.Refresh;
        if (Has("excel", "dışa aktar")) return Kind.Export;
        if (Has("yeni ", "ekle")) return Kind.Add;
        if (Has("düzenle")) return Kind.Edit;
        if (Has("print", "yazdır")) return Kind.Print;
        if (Has("tarih", "takvim", "tatil gün")) return Kind.Calendar;
        if (Has("yetki", "şifre", "kvkk")) return Kind.Lock;
        if (Has("rapor", "analiz", "icmal", "ekstre", "mizan", "karlılık", "karnesi")) return Kind.Chart;
        if (Has("transfer", "import", "export", "içe", "dışa")) return Kind.Transfer;
        if (Has("sorgu", " sor", "araştırma")) return Kind.Search;
        if (Has("e-mail", "sms", "mesaj", "mektup")) return Kind.Mail;
        if (Has("sevkiyat", "sevk", "kamyon", "kargo", "nakliye", "teslim")) return Kind.Truck;
        if (Has("kullanıcı", "müşteri", "cari", "kefil", "personel", "kasiyer", "eleman")) return Kind.People;
        if (Has("banka", "kasa", "kredi", "tahsilat", "ödeme")) return Kind.Bank;
        if (Has("çek", "senet", "bordro")) return Kind.Cheque;
        if (Has("fatura", "irsaliye", "fiş", "makbuz")) return Kind.Receipt;
        if (Has("fiyat", "etiket", "barkod", "iskonto", "prim")) return Kind.Tag;
        if (Has("veritabanı", "database", "yedek", "data")) return Kind.Database;
        if (Has("tanım", "parametre", "ayar", "kod", "bakım", "güncelle")) return Kind.Settings;
        if (Has("stok", "ürün", "depo", "envanter", "sayım", "malzeme")) return Kind.Box;
        if (Has("sipariş", "satınalma")) return Kind.Cart;
        return group ? Kind.Folder : Kind.Document;
    }

    internal static Bitmap Create(Kind kind)
    {
        var image = new Bitmap(32, 32);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(2, 2);
        var color = kind switch
        {
            Kind.Store or Kind.Box or Kind.Truck => Color.FromArgb(36, 117, 125),
            Kind.Bank or Kind.Cheque or Kind.Calculator or Kind.Export => Color.FromArgb(49, 116, 86),
            Kind.Cart or Kind.Receipt or Kind.Tag => Color.FromArgb(166, 109, 47),
            Kind.People or Kind.Lock => Color.FromArgb(110, 91, 145),
            Kind.Chart or Kind.Document or Kind.Mail or Kind.Home => Color.FromArgb(61, 106, 157),
            Kind.Close => Color.FromArgb(158, 74, 74),
            _ => Color.FromArgb(85, 103, 119)
        };
        using var tint = new SolidBrush(Color.FromArgb(20, color));
        if (kind is Kind.Receipt or Kind.Calculator or Kind.Document) g.FillRectangle(tint, 3, 2, 10, 12);
        if (kind is Kind.Cheque or Kind.Mail) g.FillRectangle(tint, 2, 3, 12, 10);
        using var pen = new Pen(color, 1.25f)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
        };
        void Line(float x1, float y1, float x2, float y2) => g.DrawLine(pen, x1, y1, x2, y2);
        void Rect(float x, float y, float w, float h) => g.DrawRectangle(pen, x, y, w, h);
        void Circle(float x, float y, float size) => g.DrawEllipse(pen, x, y, size, size);
        void Path(params PointF[] points) => g.DrawLines(pen, points);
        switch (kind)
        {
            case Kind.Refresh:
                g.DrawArc(pen, 2.5f, 2.5f, 11, 11, 45, 290);
                Path(new(14, 2), new(14, 6), new(10, 6)); break;
            case Kind.Export:
                Rect(2, 2, 9, 12); Line(4, 5, 8, 5); Line(4, 8, 7, 8);
                Path(new(8, 11), new(14, 11), new(12, 9)); Line(14, 11, 12, 13); break;
            case Kind.Add:
                Circle(2, 2, 12); Line(8, 5, 8, 11); Line(5, 8, 11, 8); break;
            case Kind.Edit:
                Path(new(2, 14), new(3, 10), new(11, 2), new(14, 5), new(6, 13), new(2, 14));
                Line(9, 4, 12, 7); break;
            case Kind.Print:
                Rect(4, 1.5f, 8, 4); Rect(2, 5.5f, 12, 6); Rect(4, 9, 8, 5.5f); Line(11, 7.5f, 12, 7.5f); break;
            case Kind.Calendar:
                Rect(2, 3, 12, 11); Line(2, 6, 14, 6); Line(5, 1.5f, 5, 4); Line(11, 1.5f, 11, 4);
                Line(5, 9, 6, 9); Line(10, 9, 11, 9); Line(5, 12, 6, 12); break;
            case Kind.Store:
                Path(new(2, 6), new(3, 2), new(13, 2), new(14, 6), new(2, 6));
                Path(new(3, 8), new(3, 14), new(13, 14), new(13, 8));
                Rect(6, 9, 4, 5); Line(6, 2, 5.5f, 6); Line(10, 2, 10.5f, 6); break;
            case Kind.Calculator:
                Rect(3, 1.5f, 10, 13); Rect(5, 3.5f, 6, 3);
                for (var y = 9; y <= 12; y += 3) { Line(5, y, 6, y); Line(10, y, 11, y); } break;
            case Kind.Box:
                Path(new(2, 4.5f), new(8, 1.5f), new(14, 4.5f), new(14, 11.5f), new(8, 14.5f), new(2, 11.5f), new(2, 4.5f), new(8, 7.5f), new(14, 4.5f));
                Line(8, 7.5f, 8, 14.5f); Line(5, 3, 11, 6); break;
            case Kind.Cart:
                Path(new(1.5f, 2), new(3.5f, 2), new(5.5f, 10), new(12.5f, 10), new(14, 4.5f), new(4.5f, 4.5f));
                Circle(5, 12, 1.5f); Circle(11, 12, 1.5f); break;
            case Kind.Receipt:
                Path(new(3, 14), new(3, 2), new(13, 2), new(13, 14), new(10.5f, 12.5f), new(8, 14), new(5.5f, 12.5f), new(3, 14));
                Line(5.5f, 5, 10.5f, 5); Line(5.5f, 8, 10.5f, 8); break;
            case Kind.Bank:
                Path(new(1.5f, 5), new(8, 1.5f), new(14.5f, 5), new(1.5f, 5));
                for (var x = 4; x <= 12; x += 4) Line(x, 7, x, 12);
                Line(2, 14, 14, 14); break;
            case Kind.Cheque:
                Rect(1.5f, 3, 13, 10); Circle(4, 6, 3); Line(9, 6, 12, 6); Line(9, 9, 12, 9); break;
            case Kind.Tools:
                Path(new(10, 2), new(8, 4), new(9, 7), new(12, 8), new(14, 6));
                Path(new(14, 6), new(13, 10), new(9, 10), new(5, 14), new(2, 11), new(6, 7), new(6, 3), new(10, 2)); break;
            case Kind.Database:
                g.DrawEllipse(pen, 2.5f, 2, 11, 4); Line(2.5f, 4, 2.5f, 12); Line(13.5f, 4, 13.5f, 12);
                g.DrawArc(pen, 2.5f, 6, 11, 4, 0, 180); g.DrawArc(pen, 2.5f, 10, 11, 4, 0, 180); break;
            case Kind.Close:
                Line(4, 4, 12, 12); Line(12, 4, 4, 12); break;
            case Kind.Chart:
                Path(new(2, 2), new(2, 14), new(14, 14));
                Line(5, 11, 5, 8); Line(9, 11, 9, 5); Line(13, 11, 13, 2); break;
            case Kind.People:
                Circle(5.5f, 2, 5); g.DrawArc(pen, 3.5f, 9, 9, 9, 180, 180); Line(3.5f, 13.5f, 12.5f, 13.5f); break;
            case Kind.Folder:
                Path(new(2, 13), new(2, 3), new(6, 3), new(8, 5), new(14, 5), new(14, 13), new(2, 13)); break;
            case Kind.Transfer:
                Path(new(2, 5), new(14, 5), new(11, 2));
                Path(new(14, 11), new(2, 11), new(5, 14)); break;
            case Kind.Search:
                Circle(2, 2, 8); Line(9, 9, 14, 14); break;
            case Kind.Mail:
                Rect(1.5f, 3, 13, 10); Path(new(2, 4), new(8, 9), new(14, 4)); break;
            case Kind.Truck:
                Rect(1.5f, 3, 8, 8); Path(new(9.5f, 6), new(12, 6), new(14.5f, 9), new(14.5f, 11), new(9.5f, 11));
                Circle(3, 11, 3); Circle(11, 11, 3); break;
            case Kind.Tag:
                Path(new(2, 2), new(8, 2), new(14, 8), new(8, 14), new(2, 8), new(2, 2)); Circle(4, 4, 2); break;
            case Kind.Lock:
                Rect(3, 7, 10, 7); g.DrawArc(pen, 5, 1.5f, 6, 8, 180, 180); Line(8, 10, 8, 12); break;
            case Kind.Settings:
                Line(2, 4, 14, 4); Line(2, 8, 14, 8); Line(2, 12, 14, 12);
                Line(5, 2, 5, 6); Line(11, 6, 11, 10); Line(7, 10, 7, 14); break;
            case Kind.Home:
                Path(new(1.5f, 7), new(8, 1.5f), new(14.5f, 7));
                Path(new(3, 6), new(3, 14), new(13, 14), new(13, 6)); Rect(6, 9, 4, 5); break;
            default:
                Path(new(3, 1.5f), new(10, 1.5f), new(13, 4.5f), new(13, 14.5f), new(3, 14.5f), new(3, 1.5f));
                Line(5.5f, 7, 10.5f, 7); Line(5.5f, 10, 10.5f, 10); break;
        }
        return image;
    }
}
