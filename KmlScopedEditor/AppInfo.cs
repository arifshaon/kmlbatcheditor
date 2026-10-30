using System.Reflection;

namespace KmlScopedEditor;

internal static class AppInfo
{
    public const string ProductName =
        "Simple KML Editor";

    public const string Tagline =
        "Edit, split, simplify and style KML/KMZ files.";

    public static string Version
    {
        get
        {
            var version =
                Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version;

            if (version is null)
                return "1.1.0";

            return
                $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }
}
