using System.Drawing.Drawing2D;
using R3.Desktop.Theme;

namespace R3.Desktop.Controls;

internal enum R3Glyph
{
    Desktop,
    Account,
    Inventory,
    Invoice,
    Finance,
    Reports,
    Settings
}

internal static class R3GlyphFactory
{
    public static Image Create(R3Glyph glyph, Color color, int size = 30)
    {
        var bitmap = new Bitmap(size, size);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        float scale = size / 32F;
        using var pen = new Pen(color, 2.2F * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        switch (glyph)
        {
            case R3Glyph.Desktop:
                graphics.DrawRectangle(pen, Scale(new RectangleF(4, 5, 24, 17), scale));
                graphics.DrawLine(pen, 11 * scale, 27 * scale, 21 * scale, 27 * scale);
                graphics.DrawLine(pen, 16 * scale, 22 * scale, 16 * scale, 27 * scale);
                break;
            case R3Glyph.Account:
                graphics.DrawEllipse(pen, Scale(new RectangleF(11, 4, 10, 10), scale));
                graphics.DrawArc(pen, Scale(new RectangleF(5, 16, 22, 13), scale), 190, 160);
                break;
            case R3Glyph.Inventory:
                graphics.DrawPolygon(pen,
                [Point(5, 10, scale), Point(16, 4, scale), Point(27, 10, scale), Point(16, 16, scale)]);
                graphics.DrawLine(pen, 5 * scale, 10 * scale, 5 * scale, 23 * scale);
                graphics.DrawLine(pen, 27 * scale, 10 * scale, 27 * scale, 23 * scale);
                graphics.DrawLine(pen, 5 * scale, 23 * scale, 16 * scale, 29 * scale);
                graphics.DrawLine(pen, 27 * scale, 23 * scale, 16 * scale, 29 * scale);
                graphics.DrawLine(pen, 16 * scale, 16 * scale, 16 * scale, 29 * scale);
                break;
            case R3Glyph.Invoice:
                graphics.DrawRectangle(pen, Scale(new RectangleF(7, 3, 18, 26), scale));
                graphics.DrawLine(pen, 11 * scale, 10 * scale, 21 * scale, 10 * scale);
                graphics.DrawLine(pen, 11 * scale, 16 * scale, 21 * scale, 16 * scale);
                graphics.DrawLine(pen, 11 * scale, 22 * scale, 18 * scale, 22 * scale);
                break;
            case R3Glyph.Finance:
                graphics.DrawEllipse(pen, Scale(new RectangleF(4, 6, 24, 20), scale));
                graphics.DrawLine(pen, 16 * scale, 10 * scale, 16 * scale, 22 * scale);
                graphics.DrawArc(pen, Scale(new RectangleF(11, 10, 10, 7), scale), 80, 220);
                graphics.DrawArc(pen, Scale(new RectangleF(11, 15, 10, 7), scale), 260, 220);
                break;
            case R3Glyph.Reports:
                graphics.DrawLine(pen, 7 * scale, 27 * scale, 7 * scale, 18 * scale);
                graphics.DrawLine(pen, 16 * scale, 27 * scale, 16 * scale, 10 * scale);
                graphics.DrawLine(pen, 25 * scale, 27 * scale, 25 * scale, 5 * scale);
                break;
            case R3Glyph.Settings:
                graphics.DrawEllipse(pen, Scale(new RectangleF(10, 10, 12, 12), scale));
                graphics.DrawEllipse(pen, Scale(new RectangleF(5, 5, 22, 22), scale));
                break;
        }

        return bitmap;
    }

    private static RectangleF Scale(RectangleF rectangle, float scale) =>
        new(rectangle.X * scale, rectangle.Y * scale, rectangle.Width * scale, rectangle.Height * scale);

    private static PointF Point(float x, float y, float scale) => new(x * scale, y * scale);
}
