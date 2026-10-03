using System.IO;
using WinStart.Core;
using WinStart.Update;
using Xunit;

namespace WinStart.Tests
{
    public class UpdateCheckerTests
    {
        private const string Release = @"{
          ""tag_name"": ""v1.4.0"",
          ""html_url"": ""https://github.com/KK4ODA/WinStart/releases/tag/v1.4.0"",
          ""prerelease"": false,
          ""body"": ""## What's new\r\n- Faster startup\r\n"",
          ""assets"": [
            { ""name"": ""SHA256SUMS.txt"", ""size"": 90, ""browser_download_url"": ""https://github.com/KK4ODA/WinStart/releases/download/v1.4.0/SHA256SUMS.txt"" },
            { ""name"": ""WinStart-Setup-1.4.0.exe"", ""size"": 2345678, ""browser_download_url"": ""https://github.com/KK4ODA/WinStart/releases/download/v1.4.0/WinStart-Setup-1.4.0.exe"" },
            { ""name"": ""notes.txt"", ""size"": 10, ""browser_download_url"": ""https://github.com/KK4ODA/WinStart/releases/download/v1.4.0/notes.txt"" }
          ]
        }";

        [Fact]
        public void Reads_version_notes_and_assets()
        {
            ReleaseInfo release = UpdateChecker.ParseRelease(Release);

            Assert.Equal("1.4.0", release.Version.ToString());
            Assert.Equal("https://github.com/KK4ODA/WinStart/releases/tag/v1.4.0", release.PageUrl);
            Assert.StartsWith("## What's new", release.Notes);
            Assert.Equal("WinStart-Setup-1.4.0.exe", release.InstallerName);
            Assert.Equal("https://github.com/KK4ODA/WinStart/releases/download/v1.4.0/WinStart-Setup-1.4.0.exe", release.InstallerUrl);
            Assert.Equal(2345678, release.InstallerSize);
            Assert.EndsWith("/SHA256SUMS.txt", release.ChecksumsUrl);
            Assert.True(release.CanInstall);
        }

        [Fact]
        public void Never_trusts_downloads_from_outside_the_repository()
        {
            string json = Release.Replace("https://github.com/KK4ODA/WinStart/releases/download/", "https://example.com/KK4ODA/WinStart/releases/download/")
                                 .Replace("https://github.com/KK4ODA/WinStart/releases/tag/v1.4.0", "https://example.com/phish");

            ReleaseInfo release = UpdateChecker.ParseRelease(json);

            Assert.Null(release.InstallerUrl);
            Assert.Null(release.ChecksumsUrl);
            Assert.False(release.CanInstall);
            Assert.Equal(AppInfo.ReleasesUrl, release.PageUrl); // falls back to the real releases page
        }

        [Fact]
        public void A_release_without_assets_cannot_be_installed_automatically()
        {
            ReleaseInfo release = UpdateChecker.ParseRelease(@"{ ""tag_name"": ""v2.0.0"" }");
            Assert.Equal("2.0.0", release.Version.ToString());
            Assert.False(release.CanInstall);
            Assert.Equal("", release.Notes);
        }

        [Theory]
        [InlineData(@"{ ""tag_name"": ""nightly"" }")]
        [InlineData(@"{ }")]
        [InlineData("not json at all")]
        [InlineData("[]")]
        public void Unusable_answers_become_a_readable_error(string json)
        {
            Assert.Throws<UpdateException>(() => UpdateChecker.ParseRelease(json));
        }

        [Fact]
        public void Only_higher_versions_count_as_updates()
        {
            SemVer current = AppInfo.SemVer;
            Assert.True(UpdateChecker.IsNewer(new ReleaseInfo { Version = new SemVer(current.Major, current.Minor, current.Patch + 1) }));
            Assert.True(UpdateChecker.IsNewer(new ReleaseInfo { Version = new SemVer(current.Major + 1, 0, 0) }));
            Assert.False(UpdateChecker.IsNewer(new ReleaseInfo { Version = new SemVer(current.Major, current.Minor, current.Patch) }));
            Assert.False(UpdateChecker.IsNewer(new ReleaseInfo { Version = new SemVer(0, 0, 1) }));
        }

        [Fact]
        public void The_running_version_is_a_valid_semantic_version()
        {
            Assert.True(SemVer.TryParse(AppInfo.Version, out SemVer? version));
            Assert.Equal(AppInfo.Version, version!.ToString());
            Assert.DoesNotContain("+", AppInfo.Version);
        }
    }

    public class ChecksumsTests
    {
        private const string HashA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string HashB = "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210";

        [Fact]
        public void Finds_the_checksum_for_a_file()
        {
            string sums = "# comment\n" + HashA + "  WinStart-Setup-1.0.0.exe\r\n" + HashB + " *Other File.zip\n\n";
            Assert.Equal(HashA, Checksums.Find(sums, "WinStart-Setup-1.0.0.exe"));
            Assert.Equal(HashA, Checksums.Find(sums, "winstart-setup-1.0.0.EXE"));
            Assert.Equal(HashB.ToLowerInvariant(), Checksums.Find(sums, "Other File.zip"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("WinStart-Setup-1.0.0.exe")]
        [InlineData("0123  WinStart-Setup-1.0.0.exe")]
        [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  WinStart-Setup-1.0.0.exe")]
        [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  WinStart-Setup-9.9.9.exe")]
        public void Returns_null_when_there_is_no_valid_entry(string sums)
        {
            Assert.Null(Checksums.Find(sums, "WinStart-Setup-1.0.0.exe"));
        }

        [Fact]
        public void Hashes_files_with_sha256()
        {
            string path = Path.Combine(TestEnvironment.NewFolder(), "abc.txt");
            File.WriteAllText(path, "abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Checksums.Sha256OfFile(path));
        }
    }

    public class ReleaseNotesTests
    {
        [Fact]
        public void Markdown_becomes_plain_text()
        {
            const string markdown = "## Added\n- **Sequential** launching\n* A `--quit` switch\n\nSee [the guide](https://example.com/guide).";
            string text = ReleaseNotes.ToPlainText(markdown);
            Assert.Equal("Added\r\n• Sequential launching\r\n• A --quit switch\r\n\r\nSee the guide.", text);
        }

        [Fact]
        public void Lines_wrapped_in_the_source_are_joined_again()
        {
            const string markdown = "First release.\r\n\r\n### Added\r\n\r\n- Finds installed programs (Start menu, Store\r\n  apps and registered programs) with search.\r\n- Second item.\r\n\r\nA paragraph that was\nwrapped by hand.\r\n1. One\r\n2. Two";
            string text = ReleaseNotes.ToPlainText(markdown);
            Assert.Equal(
                "First release.\r\n\r\nAdded\r\n\r\n"
                + "• Finds installed programs (Start menu, Store apps and registered programs) with search.\r\n"
                + "• Second item.\r\n\r\n"
                + "A paragraph that was wrapped by hand.\r\n1. One\r\n2. Two",
                text);
        }

        [Fact]
        public void Text_directly_under_a_heading_stays_on_its_own_line()
        {
            Assert.Equal("Fixed\r\nA crash at startup.", ReleaseNotes.ToPlainText("## Fixed\nA crash at startup."));
        }
    }
}
