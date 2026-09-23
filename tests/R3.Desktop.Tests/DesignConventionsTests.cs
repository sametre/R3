using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using R3.Desktop.Design;

namespace R3.Desktop.Tests;

public sealed class DesignConventionsTests
{
    [Theory]
    [InlineData("N2", true)]
    [InlineData("N0", true)]
    [InlineData("{0:N2}", true)]
    [InlineData("{0:N2} ₺", true)]
    [InlineData("#,##0.00", true)]
    [InlineData("C", true)]
    [InlineData("dd.MM.yyyy", false)]
    [InlineData("Fatura {0}", false)]
    [InlineData(null, false)]
    public void RecognizesNumericFormats(string? format, bool expected) => Assert.Equal(expected, DesignConventions.IsNumericFormat(format));

    [Fact]
    public void NumericColumnsAreRightAlignedAndDecimalsGetN2()
    {
        RunSta(() =>
        {
            var table = new DataTable();
            table.Columns.Add("Kod", typeof(string)); table.Columns.Add("Tutar", typeof(decimal)); table.Columns.Add("Adet", typeof(long)); table.Columns.Add("Oran", typeof(decimal));
            table.Rows.Add("A", 1500m, 3L, 0.5m);
            var grid = new DataGrid();
            var code = new DataGridTextColumn { Binding = new Binding("Kod") };
            var amount = new DataGridTextColumn { Binding = new Binding("Tutar") };
            var count = new DataGridTextColumn { Binding = new Binding("Adet") };
            var rate = new DataGridTextColumn { Binding = new Binding("Oran") { StringFormat = "P1" } };
            foreach (var c in new[] { code, amount, count, rate }) grid.Columns.Add(c);

            DesignConventions.FormatNumericColumns(grid);        // first pass: no data yet, types unknown
            grid.ItemsSource = table.DefaultView;
            DesignConventions.FormatNumericColumns(grid);        // data arrived: numeric columns must still be recognized

            Assert.Contains(code.ElementStyle.Setters.OfType<Setter>(), s => s.Property == TextBlock.TextAlignmentProperty && (TextAlignment)s.Value == TextAlignment.Left);
            Assert.Equal("N2", ((Binding)amount.Binding).StringFormat);
            Assert.Equal("P1", ((Binding)rate.Binding).StringFormat);         // existing format is kept
            foreach (var c in new[] { amount, count, rate })
                Assert.Contains(c.ElementStyle.Setters.OfType<Setter>(), s => s.Property == TextBlock.TextAlignmentProperty && (TextAlignment)s.Value == TextAlignment.Right);
        });
    }

    [Fact]
    public void TokensResolveWithoutARunningApplication() =>
        RunSta(() => Assert.NotNull(Ui.Brush("R3.Grid.Line.Brush")));

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
