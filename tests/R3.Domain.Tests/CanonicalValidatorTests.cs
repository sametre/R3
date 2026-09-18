using R3.Application;
using R3.Contracts;

namespace R3.Domain.Tests;

public sealed class CanonicalValidatorTests
{
    [Fact] public async Task ProductRejectsInvalidVat() => Assert.False((await new CreateProductValidator().ValidateAsync(new CreateProductRequest("P", "Ürün", Guid.NewGuid(), 101))).IsValid);
    [Fact] public async Task ProductRequiresCode() => Assert.False((await new CreateProductValidator().ValidateAsync(new CreateProductRequest("", "Ürün", Guid.NewGuid(), 20))).IsValid);
    [Fact] public async Task AccountRequiresCode() => Assert.False((await new CreateAccountValidator().ValidateAsync(new CreateAccountRequest("", "Cari"))).IsValid);

    private static CreateCompanyRequest CompanyRequest(string taxNumber) => new("R3", "R3 Teknoloji", "R3 Teknoloji", "", taxNumber, "", "", "");

    [Theory]
    [InlineData("1234567890")]
    [InlineData("12345678901")]
    [InlineData("")]
    public async Task CompanyAcceptsValidOrEmptyTaxNumber(string taxNumber) => Assert.True((await new CreateCompanyValidator().ValidateAsync(CompanyRequest(taxNumber))).IsValid);

    [Theory]
    [InlineData("ABC")]
    [InlineData("123456789")]
    [InlineData("123456789012")]
    public async Task CompanyRejectsInvalidTaxNumber(string taxNumber) => Assert.False((await new CreateCompanyValidator().ValidateAsync(CompanyRequest(taxNumber))).IsValid);
}
