using System;
using WinStart.Discovery;
using Xunit;

namespace WinStart.Tests
{
    public class DiscoveryTests
    {
        [Theory]
        [InlineData(@"""C:\Program Files\App\app.exe"",0", @"C:\Program Files\App\app.exe")]
        [InlineData(@"C:\Program Files\App\app.exe,-101", @"C:\Program Files\App\app.exe")]
        [InlineData(@"C:\App\app.exe", @"C:\App\app.exe")]
        [InlineData(@"C:\App\App.EXE /background", @"C:\App\App.EXE")]
        [InlineData(@"""C:\App\app.exe"" --flag", @"C:\App\app.exe")]
        [InlineData(@"  ""C:\App\app.exe""  ", @"C:\App\app.exe")]
        public void Extracts_the_program_from_registry_values(string value, string expected)
        {
            Assert.Equal(expected, AppDiscovery.ExePathFromCommand(value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(@"C:\App\icon.ico")]
        [InlineData(@"C:\App\library.dll,3")]
        [InlineData(@"""C:\App\unterminated.exe")]
        [InlineData("app.exe")]
        public void Ignores_values_that_are_not_a_full_program_path(string? value)
        {
            Assert.Null(AppDiscovery.ExePathFromCommand(value));
        }

        [Fact]
        public void Expands_environment_variables_in_registry_values()
        {
            string? path = AppDiscovery.ExePathFromCommand(@"%SystemRoot%\notepad.exe");
            Assert.NotNull(path);
            Assert.DoesNotContain("%", path);
            Assert.EndsWith(@"\notepad.exe", path, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(@"C:\Program Files\WSJT\wsjtx.exe", true)]
        [InlineData(@"C:\Program Files\App\unins000.exe", false)]
        [InlineData(@"C:\Program Files\App\AppUpdater.exe", false)]
        [InlineData(@"C:\Program Files\App\setup.exe", false)]
        [InlineData(@"C:\ProgramData\Package Cache\{guid}\vc_redist.x64.exe", false)]
        [InlineData(@"C:\Program Files\WindowsApps\Some.Package_1.0_x64__abc\tool.exe", false)]
        [InlineData(@"C:\Program Files\Common Files\Vendor\service.exe", false)]
        public void Separates_applications_from_helpers(string path, bool expected)
        {
            Assert.Equal(expected, AppDiscovery.IsLikelyApplication(path));
        }

        [Fact]
        public void Windows_components_are_not_offered_from_the_registry()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Assert.False(AppDiscovery.IsLikelyApplication(system + @"\mmc.exe"));
        }

        [Theory]
        [InlineData("Uninstall Foo", @"C:\Foo\foo.exe", true)]
        [InlineData("Foo", @"C:\Foo\unins000.exe", true)]
        [InlineData("Remove Foo", @"C:\Windows\System32\msiexec.exe", true)]
        [InlineData("Foo", @"C:\Foo\foo.exe", false)]
        public void Recognises_uninstallers(string name, string target, bool expected)
        {
            Assert.Equal(expected, AppDiscovery.LooksLikeUninstaller(name, target));
        }

        [Fact]
        public void Normalising_makes_equivalent_paths_equal()
        {
            Assert.Equal(@"C:\WSJT\wsjtx\bin\wsjtx.exe", AppDiscovery.NormalizePath(@"C:\WSJT\wsjtx\bin\\wsjtx.exe"));
            Assert.Equal(@"C:\App\app.exe", AppDiscovery.NormalizePath(@"C:\App\sub\..\app.exe"));
        }
    }
}
