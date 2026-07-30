namespace R3.UnitTests.Architecture;

public sealed class ProjectStructureTests
{
    [Fact]
    public void DomainAssemblyNameIsCorrect()
    {
        string assemblyName = typeof(R3.Domain.Common.Entity<>).Assembly.GetName().Name!;
        Assert.Equal("R3.Domain", assemblyName);
    }
}
