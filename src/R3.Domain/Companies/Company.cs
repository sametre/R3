using R3.Domain.Common;

namespace R3.Domain.Companies;

public sealed class Company : Entity
{
    private Company()
    {
    }

    public Company(string name, string taxNumber)
    {
        Name = Require(name, nameof(name), 150);
        TaxNumber = RequireDigits(taxNumber, nameof(taxNumber), 10, 11);
    }

    public string Name { get; private set; } = string.Empty;
    public string TaxNumber { get; private set; } = string.Empty;

    public void Rename(string name)
    {
        Name = Require(name, nameof(name), 150);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 150)
        {
            throw new ArgumentException($"Değer 1-{maximumLength} karakter olmalıdır.", parameterName);
        }

        return normalized;
    }

    private static string RequireDigits(string value, string parameterName, params int[] lengths)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!lengths.Contains(normalized.Length) || normalized.Any(character => !char.IsDigit(character)))
        {
            throw new ArgumentException("Vergi numarası 10 veya 11 rakamdan oluşmalıdır.", parameterName);
        }

        return normalized;
    }
}

