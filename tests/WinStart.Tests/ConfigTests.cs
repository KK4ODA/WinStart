using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinStart.Core;
using Xunit;

namespace WinStart.Tests
{
    public class ConfigTests
    {
        [Fact]
        public void Defaults_are_sensible()
        {
            var config = new AppConfig();
            Assert.Empty(config.Apps);
            Assert.True(config.Settings.StartMinimized);
            Assert.True(config.Settings.SkipIfRunning);
            Assert.True(config.Settings.AutoCheckUpdates);
            Assert.False(config.Settings.WaitForNetwork);
            Assert.False(config.Settings.Sequential);
        }

        [Fact]
        public void Round_trips_everything()
        {
            var config = new AppConfig();
            config.Settings.StartMinimized = false;
            config.Settings.WaitForNetwork = true;
            config.Settings.NetworkTimeoutSeconds = 45;
            config.Settings.Sequential = true;
            config.Settings.SkipIfRunning = false;
            config.Settings.AutoCheckUpdates = false;
            config.Settings.SkippedVersion = "1.2.3";
            config.Settings.TrayHintShown = true;
            config.Apps.Add(new StartupEntry
            {
                Name = "WSJT-X \"test\"",
                TargetPath = @"C:\WSJT\wsjtx\bin\wsjtx.exe",
                Arguments = "--rig-name=\"IC-7300\" /min",
                DelaySeconds = 90,
                Enabled = false,
                StartMinimized = true,
            });
            config.Apps.Add(new StartupEntry { Name = "Calculator", TargetPath = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" });

            AppConfig loaded = AppConfig.FromJson(config.ToJson());

            Assert.False(loaded.Settings.StartMinimized);
            Assert.True(loaded.Settings.WaitForNetwork);
            Assert.Equal(45, loaded.Settings.NetworkTimeoutSeconds);
            Assert.True(loaded.Settings.Sequential);
            Assert.False(loaded.Settings.SkipIfRunning);
            Assert.False(loaded.Settings.AutoCheckUpdates);
            Assert.Equal("1.2.3", loaded.Settings.SkippedVersion);
            Assert.True(loaded.Settings.TrayHintShown);

            Assert.Equal(2, loaded.Apps.Count);
            StartupEntry first = loaded.Apps[0];
            Assert.Equal(config.Apps[0].Id, first.Id);
            Assert.Equal("WSJT-X \"test\"", first.Name);
            Assert.Equal(@"C:\WSJT\wsjtx\bin\wsjtx.exe", first.TargetPath);
            Assert.Equal("--rig-name=\"IC-7300\" /min", first.Arguments);
            Assert.Equal(90, first.DelaySeconds);
            Assert.False(first.Enabled);
            Assert.True(first.StartMinimized);
            Assert.True(loaded.Apps[1].IsShellApp);
            Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", loaded.Apps[1].AppUserModelId);
        }

        [Fact]
        public void Is_written_as_readable_indented_json()
        {
            var config = new AppConfig();
            config.Apps.Add(new StartupEntry { Id = "abc", Name = "Chrome", TargetPath = @"C:\Chrome\chrome.exe", DelaySeconds = 10 });

            string json = config.ToJson();

            Assert.Contains("\r\n  \"settings\": {\r\n", json);
            Assert.Contains("\"launchMode\": \"independent\"", json);
            Assert.Contains("\"path\": \"C:\\\\Chrome\\\\chrome.exe\"", json);
            Assert.Contains("\"delaySeconds\": 10", json);
        }

        [Fact]
        public void Tolerates_missing_and_mistyped_values()
        {
            const string json = @"{
              ""settings"": { ""waitForNetwork"": ""yes"", ""networkTimeoutSeconds"": 100000, ""launchMode"": ""SEQUENTIAL"", ""unknownSetting"": 5 },
              ""apps"": [
                { ""path"": ""C:\\Tools\\My App.exe"" },
                { ""name"": ""No path"" },
                ""not an object"",
                { ""path"": ""C:\\x.exe"", ""delaySeconds"": -5, ""enabled"": ""true"" },
                { ""path"": ""C:\\y.exe"", ""delaySeconds"": 99999999 }
              ]
            }";

            AppConfig config = AppConfig.FromJson(json);

            Assert.False(config.Settings.WaitForNetwork);                 // wrong type: default kept
            Assert.Equal(3600, config.Settings.NetworkTimeoutSeconds);   // clamped
            Assert.True(config.Settings.Sequential);
            Assert.Equal(3, config.Apps.Count);                           // entries without a path are dropped
            Assert.Equal("My App", config.Apps[0].Name);                  // named after the file
            Assert.Equal(StartupEntry.DefaultDelaySeconds, config.Apps[0].DelaySeconds);
            Assert.True(config.Apps[0].Enabled);
            Assert.Equal(0, config.Apps[1].DelaySeconds);
            Assert.True(config.Apps[1].Enabled);
            Assert.Equal(StartupEntry.MaxDelaySeconds, config.Apps[2].DelaySeconds);
        }

        [Fact]
        public void An_empty_object_loads_as_defaults()
        {
            AppConfig config = AppConfig.FromJson("{}");
            Assert.Empty(config.Apps);
            Assert.True(config.Settings.StartMinimized);
        }

        [Fact]
        public void Duplicate_ids_are_made_unique()
        {
            const string json = @"{ ""apps"": [ { ""id"": ""same"", ""path"": ""C:\\a.exe"" }, { ""id"": ""same"", ""path"": ""C:\\b.exe"" } ] }";
            AppConfig config = AppConfig.FromJson(json);
            Assert.Equal(2, config.Apps.Select(a => a.Id).Distinct().Count());
            Assert.Equal("same", config.Apps[0].Id);
        }

        [Fact]
        public void Store_reports_first_run_and_saves_atomically()
        {
            string folder = TestEnvironment.NewFolder();
            string path = Path.Combine(folder, "config.json");
            var store = new ConfigStore(path);

            AppConfig config = store.Load();
            Assert.True(store.CreatedNew);
            Assert.Null(store.LoadWarning);

            config.Apps.Add(new StartupEntry { Name = "A", TargetPath = @"C:\a.exe" });
            store.Save(config);
            config.Apps.Add(new StartupEntry { Name = "B", TargetPath = @"C:\b.exe" });
            store.Save(config); // second save replaces the existing file

            Assert.Equal(new[] { "config.json" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());

            var reopened = new ConfigStore(path);
            AppConfig loaded = reopened.Load();
            Assert.False(reopened.CreatedNew);
            Assert.Equal(new List<string> { "A", "B" }, loaded.Apps.Select(a => a.Name).ToList());
        }

        [Fact]
        public void Store_keeps_a_copy_of_an_unreadable_file()
        {
            string folder = TestEnvironment.NewFolder();
            string path = Path.Combine(folder, "config.json");
            File.WriteAllText(path, "{ this is not json");
            var store = new ConfigStore(path);

            AppConfig config = store.Load();

            Assert.Empty(config.Apps);
            Assert.False(store.CreatedNew);
            Assert.NotNull(store.LoadWarning);
            string backup = Directory.GetFiles(folder, "config.json.unreadable-*").Single();
            Assert.Equal("{ this is not json", File.ReadAllText(backup));
        }

        [Fact]
        public void Entries_compare_targets_ignoring_path_case()
        {
            var a = new StartupEntry { TargetPath = @"C:\Apps\App.exe", Arguments = "-x" };
            var b = new StartupEntry { TargetPath = @"c:\apps\app.EXE", Arguments = "-x" };
            var c = new StartupEntry { TargetPath = @"C:\Apps\App.exe", Arguments = "-y" };
            Assert.True(a.SameTargetAs(b));
            Assert.False(a.SameTargetAs(c));
        }

        [Fact]
        public void Clone_is_independent_but_keeps_the_id()
        {
            var original = new StartupEntry { Name = "A", TargetPath = @"C:\a.exe" };
            StartupEntry copy = original.Clone();
            copy.Name = "B";
            Assert.Equal("A", original.Name);
            Assert.Equal(original.Id, copy.Id);
        }
    }
}
