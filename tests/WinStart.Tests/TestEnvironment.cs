using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    // .NET Framework does not define this attribute; the compiler only needs it to exist.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace WinStart.Tests
{
    internal static class TestEnvironment
    {
        /// <summary>
        /// Runs before any test: points WinStart's data folder at a scratch location so tests never
        /// read or write the real settings, history or log of the person running them.
        /// </summary>
        [ModuleInitializer]
        internal static void Initialize()
        {
            Environment.SetEnvironmentVariable("WINSTART_DATA_DIR", Path.Combine(Path.GetTempPath(), "WinStart.Tests", "data"));
        }

        /// <summary>A fresh, empty folder for one test.</summary>
        public static string NewFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "WinStart.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
