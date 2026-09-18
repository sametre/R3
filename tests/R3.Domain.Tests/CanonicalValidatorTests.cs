using R3.Application;

namespace R3.Domain.Tests;

public sealed class CanonicalValidatorTests
{
    [Fact] public async Task ProductRejectsInvalidVat() => Assert.False((await new CreateProductValidator().ValidateAsync(new CreateProductRequest("P", "Ürün", Guid.NewGuid(), 101))).IsValid);
    [Fact] public async Task ProductRequiresCode() => Assert.False((await new CreateProductValidator().ValidateAsync(new CreateProductRequest("", "Ürün", Guid.NewGuid(), 20))).IsValid);
    [Fact] public async Task AccountRequiresCode() => Assert.False((await new CreateAccountValidator().ValidateAsync(new CreateAccountRequest("", "Cari"))).IsValid);
}
