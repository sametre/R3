using R3.Infrastructure.Security;

namespace R3.UnitTests.Security;

public sealed class PinHasherTests
{
    private const string DevelopmentHash =
        "R3PIN$150000$sQxErLw6GyPs4fQaFOFqOA==$ixvXmaclKU4PWNRSON9fgyP1/2DzDUj0PScNBz3kpNE=";

    [Fact]
    public void VerifyAcceptsCorrectPin()
    {
        Assert.True(PinHasher.Verify("1234", DevelopmentHash));
    }

    [Fact]
    public void VerifyRejectsIncorrectPin()
    {
        Assert.False(PinHasher.Verify("9999", DevelopmentHash));
    }
}
