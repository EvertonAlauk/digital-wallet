using System.Reflection;
using DigitalWallet.Domain;
using FluentAssertions;

namespace DigitalWallet.UnitTests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_DoesNotReferenceAnyOtherProject()
    {
        var domain = typeof(AssemblyMarker).Assembly;

        var projectReferences = domain.GetReferencedAssemblies()
            .Where(a => a.Name is not null && a.Name.StartsWith("DigitalWallet.", StringComparison.Ordinal))
            .Select(a => a.Name)
            .ToList();

        projectReferences.Should().BeEmpty();
    }
}
