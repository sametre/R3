namespace R3.Infrastructure;

/// <summary>
/// Pure barcode encoders for label printing - no printer or UI dependency. Output is a module pattern:
/// alternating bar/space widths starting with a bar (in narrow-module units), which a renderer turns
/// into rectangles. EAN-13 is used for 12/13-digit numeric barcodes (retail scanners expect it);
/// everything else is Code 128 (subset C for all-digit even-length data, otherwise subset B).
/// </summary>
public static class BarcodeSymbology
{
    public sealed record Encoded(string Symbology, IReadOnlyList<int> Modules, string HumanReadable);

    public static Encoded Encode(string data)
    {
        data = data.Trim();
        if (data.Length == 0) throw new ArgumentException("Barkod boş olamaz.");
        if (data.All(char.IsAsciiDigit) && data.Length is 12 or 13 && (data.Length == 12 || Ean13CheckDigit(data[..12]) == data[12] - '0'))
            return Ean13(data);
        return Code128(data);
    }

    // ---- EAN-13 -------------------------------------------------------------------------------
    private static readonly string[] EanL = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] EanG = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
    private static readonly string[] EanR = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
    private static readonly string[] EanParity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    public static int Ean13CheckDigit(string twelveDigits)
    {
        if (twelveDigits.Length != 12 || !twelveDigits.All(char.IsAsciiDigit)) throw new ArgumentException("EAN-13 kontrol hanesi için 12 rakam gerekir.");
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    private static Encoded Ean13(string data)
    {
        var full = data.Length == 12 ? data + Ean13CheckDigit(data) : data;
        var bits = new System.Text.StringBuilder("101");
        var parity = EanParity[full[0] - '0'];
        for (var i = 1; i <= 6; i++) bits.Append(parity[i - 1] == 'L' ? EanL[full[i] - '0'] : EanG[full[i] - '0']);
        bits.Append("01010");
        for (var i = 7; i <= 12; i++) bits.Append(EanR[full[i] - '0']);
        bits.Append("101");
        return new Encoded("EAN-13", BitsToModules(bits.ToString()), full);
    }

    // ---- Code 128 -----------------------------------------------------------------------------
    private static readonly string[] Code128Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222",
        "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321",
        "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121",
        "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224",
        "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113",
        "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];
    private const int StartB = 104, StartC = 105, Stop = 106;

    public static IReadOnlyList<int> Code128Values(string data)
    {
        var values = new List<int>();
        if (data.Length >= 4 && data.Length % 2 == 0 && data.All(char.IsAsciiDigit))
        {
            values.Add(StartC);
            for (var i = 0; i < data.Length; i += 2) values.Add((data[i] - '0') * 10 + (data[i + 1] - '0'));
        }
        else
        {
            values.Add(StartB);
            foreach (var ch in data)
            {
                if (ch < 32 || ch > 126) throw new ArgumentException($"Barkod Code 128-B ile yazdırılamayan karakter içeriyor: '{ch}'. Yalnızca standart ASCII karakterler desteklenir.");
                values.Add(ch - 32);
            }
        }
        var checksum = values[0];
        for (var i = 1; i < values.Count; i++) checksum += values[i] * i;
        values.Add(checksum % 103);
        values.Add(Stop);
        return values;
    }

    private static Encoded Code128(string data)
    {
        var modules = new List<int>();
        foreach (var value in Code128Values(data)) modules.AddRange(Code128Patterns[value].Select(c => c - '0'));
        return new Encoded("Code 128", modules, data);
    }

    private static IReadOnlyList<int> BitsToModules(string bits)
    {
        var modules = new List<int>(); var run = 1;
        for (var i = 1; i < bits.Length; i++)
        {
            if (bits[i] == bits[i - 1]) { run++; continue; }
            modules.Add(run); run = 1;
        }
        modules.Add(run);
        return modules;
    }
}
