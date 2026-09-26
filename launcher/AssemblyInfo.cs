using System.Reflection;

// What Windows shows under Properties → Details. The version is the launcher's own
// and changes only with its code, so Launcher.exe stays byte-identical from release
// to release (see DragNWash.Launcher.csproj).
[assembly: AssemblyTitle("Drag'n Wash Launcher")]
[assembly: AssemblyDescription("Runs before Drag'n Wash from the Steam launch options: shows the mod updates the game found, installs the ones you pick, and starts the game. Goes online only when you press Update. Unofficial fan project. Source: https://github.com/TomXV/dragnwash-modframework")]
[assembly: AssemblyCompany("TomXV")]
[assembly: AssemblyProduct("Drag'n Wash ModFramework")]
[assembly: AssemblyCopyright("Copyright (c) 2026 TomXV. MIT License.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
