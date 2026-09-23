using R3.Infrastructure;

namespace R3.Domain.Tests;

public sealed class BarcodeSymbologyTests
{
    [Fact]
    public void ValidThirteenDigitCodeIsEan13With95Modules()
    {
        var encoded = BarcodeSymbology.Encode("4006381333931");
        Assert.Equal("EAN-13", encoded.Symbology);
        Assert.Equal(95, encoded.Modules.Sum());          // 3 + 42 + 5 + 42 + 3
        Assert.Equal(1, encoded.Modules[0]);              // starts with the 101 guard
    }

    [Fact]
    public void TwelveDigitsGetTheirCheckDigitAppended()
    {
        Assert.Equal(1, BarcodeSymbology.Ean13CheckDigit("400638133393"));
        Assert.Equal("4006381333931", BarcodeSymbology.Encode("400638133393").HumanReadable);
    }

    [Fact]
    public void ThirteenDigitsWithWrongCheckDigitFallBackToCode128()
    {
        // e.g. an internal numeric code that merely has 13 digits - still printable, just not as EAN.
        Assert.Equal("Code 128", BarcodeSymbology.Encode("4006381333930").Symbology);
    }

    [Fact]
    public void Code128ChecksumIsWeightedSumMod103()
    {
        // Start B (104) + Σ value×position: W55·1 i73·2 k75·3 i73·4 p80·5 e69·6 d68·7 i73·8 a65·9 = 3281; 3281 mod 103 = 88.
        var values = BarcodeSymbology.Code128Values("Wikipedia");
        Assert.Equal(104, values[0]);
        Assert.Equal(88, values[^2]);
        Assert.Equal(106, values[^1]);
        Assert.Equal(11 * (9 + 2) + 13, BarcodeSymbology.Encode("Wikipedia").Modules.Sum()); // start+data+checksum × 11, stop 13
    }

    [Fact]
    public void EvenLengthDigitsUseCompactSubsetC()
    {
        var values = BarcodeSymbology.Code128Values("123456");
        Assert.Equal(105, values[0]);
        Assert.Equal([12, 34, 56], values.Skip(1).Take(3));
    }

    [Fact]
    public void NonAsciiCharactersAreRejectedWithATurkishMessage() =>
        Assert.Throws<ArgumentException>(() => BarcodeSymbology.Encode("ÜRÜN-1"));
}
