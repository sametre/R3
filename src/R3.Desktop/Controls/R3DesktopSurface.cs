using System.Drawing.Imaging;
using R3.Desktop.Branding;
using R3.Desktop.Theme;

namespace R3.Desktop.Controls;

internal sealed class R3DesktopSurface : Panel
{
    public R3DesktopSurface()
    {
        Dock = DockStyle.Fill;
        BackColor = R3Theme.Canvas;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        const int logoWidth = 390;
        int logoHeight = (int)(logoWidth * R3Branding.Logo.Height / (double)R3Branding.Logo.Width);
        var logoBounds = new Rectangle(
            Math.Max(0, (ClientSize.Width - logoWidth) / 2),
            Math.Max(20, (ClientSize.Height - logoHeight) / 2 - 32),
            logoWidth,
            logoHeight);

        var matrix = new ColorMatrix { Matrix33 = 0.075F };
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        e.Graphics.DrawImage(
            R3Branding.Logo,
            logoBounds,
            0,
            0,
            R3Branding.Logo.Width,
            R3Branding.Logo.Height,
            GraphicsUnit.Pixel,
            attributes);

        using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
        using var subtitleFont = new Font("Segoe UI", 9);
        using var titleBrush = new SolidBrush(Color.FromArgb(72, R3Theme.MutedText));
        using var subtitleBrush = new SolidBrush(Color.FromArgb(54, R3Theme.MutedText));
        const string title = "KOBİ ÇÖZÜMLERİ";
        const string subtitle = "İşletmenizin tüm süreçleri tek çalışma alanında";
        SizeF titleSize = e.Graphics.MeasureString(title, titleFont);
        SizeF subtitleSize = e.Graphics.MeasureString(subtitle, subtitleFont);
        float titleY = logoBounds.Bottom + 8;
        e.Graphics.DrawString(title, titleFont, titleBrush, (ClientSize.Width - titleSize.Width) / 2, titleY);
        e.Graphics.DrawString(subtitle, subtitleFont, subtitleBrush, (ClientSize.Width - subtitleSize.Width) / 2, titleY + 32);
    }
}
