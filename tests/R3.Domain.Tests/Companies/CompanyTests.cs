using R3.Domain.Companies;

namespace R3.Domain.Tests.Companies;

public sealed class CompanyTests
{
    [Fact]
    public void Constructor_NormalizesValidValues()
    {
        var company = new Company("  R3 Teknoloji  ", "1234567890");

        Assert.Equal("R3 Teknoloji", company.Name);
        Assert.Equal("1234567890", company.TaxNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("123456789")]
    public void Constructor_RejectsInvalidTaxNumber(string taxNumber)
    {
        Assert.Throws<ArgumentException>(() => new Company("R3 Teknoloji", taxNumber));
    }
}

