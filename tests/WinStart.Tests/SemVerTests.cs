using System.Linq;
using WinStart.Update;
using Xunit;

namespace WinStart.Tests
{
    public class SemVerTests
    {
        [Theory]
        [InlineData("1.2.3", "1.2.3")]
        [InlineData("v1.2.3", "1.2.3")]
        [InlineData("V10.20.30", "10.20.30")]
        [InlineData("1.2", "1.2.0")]
        [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
        [InlineData("1.2.3+abc123", "1.2.3")]
        [InlineData("1.2.3-rc.1+build.5", "1.2.3-rc.1")]
        [InlineData("  1.0.0  ", "1.0.0")]
        public void Parses_valid_versions(string text, string expected)
        {
            Assert.True(SemVer.TryParse(text, out SemVer? version));
            Assert.Equal(expected, version!.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("latest")]
        [InlineData("1")]
        [InlineData("1.2.3.4")]
        [InlineData("1.x.3")]
        [InlineData("1.2.3-")]
        [InlineData("1.2.3-beta..1")]
        [InlineData("-1.2.3")]
        public void Rejects_invalid_versions(string? text)
        {
            Assert.False(SemVer.TryParse(text, out _));
        }

        [Fact]
        public void Orders_versions_as_the_semver_specification_requires()
        {
            string[] ascending =
            {
                "0.9.9", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
                "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "1.10.0", "2.0.0",
            };
            SemVer[] versions = ascending.Select(Parse).ToArray();

            for (int i = 0; i < versions.Length; i++)
            {
                for (int j = 0; j < versions.Length; j++)
                {
                    int expected = i.CompareTo(j);
                    int actual = System.Math.Sign(versions[i].CompareTo(versions[j]));
                    Assert.True(expected == actual, $"{ascending[i]} vs {ascending[j]}: expected {expected}, got {actual}");
                }
            }
        }

        [Fact]
        public void Build_metadata_does_not_affect_ordering()
        {
            Assert.Equal(0, Parse("1.2.3+one").CompareTo(Parse("1.2.3+two")));
        }

        [Fact]
        public void Anything_is_newer_than_nothing()
        {
            Assert.True(Parse("0.0.1").CompareTo(null) > 0);
        }

        private static SemVer Parse(string text)
        {
            Assert.True(SemVer.TryParse(text, out SemVer? version), "could not parse " + text);
            return version!;
        }
    }
}
