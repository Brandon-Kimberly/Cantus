using Cantus.Core.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Models;

public sealed class BuildInfoTests
{
    [Fact]
    public void BuildInfo_Properties_ArePopulatedWithValidDefaults()
    {
        BuildInfo.Version.Should().NotBeNullOrWhiteSpace();
        BuildInfo.SemVer.Should().NotBeNullOrWhiteSpace();
        BuildInfo.CommitSha.Should().NotBeNullOrWhiteSpace();
        BuildInfo.InformationalVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BuildInfo_SemVer_DoesNotContainBuildMetadata()
    {
        BuildInfo.SemVer.Should().NotContain("+");
    }

    [Fact]
    public void BuildInfo_CommitSha_IsShortOrUnknown()
    {
        if (BuildInfo.CommitSha != "unknown")
        {
            BuildInfo.CommitSha.Length.Should().BeInRange(1, 7);
        }
    }
}
