# Building from Source & Contributing

## AI-Assisted Development Transparency

OpenCMTools was built with substantial **AI-assisted development** under human product direction, architectural constraints, and live testing in a multi-VM Microsoft Configuration Manager lab.

- Every line of code is open source under the [MIT License](https://opensource.org/licenses/MIT).
- We welcome thorough code reviews, security audits, bug reports, feature ideas, and pull requests from ConfigMgr administrators and .NET developers.

---

## Building on Windows (Full WPF Solution + WiX v4 MSI)

### Prerequisites
- **.NET 8 SDK** (or Visual Studio 2022 with the *.NET desktop development* workload)
- **.NET Framework 4.8 Developer Pack** (included on Windows 10/11 or via Visual Studio)
- **Windows PowerShell 5.1** (built into Windows — used by `Build-IconsDll.ps1`)

### Build Steps
```powershell
# 1. Build both native .NET 4.8 MECM icon resource assemblies (Duotone & Outline; version auto-read from Directory.Build.props)
.\installer\scripts\Build-IconsDll.ps1 -IconsDir "src\OpenCMTools.Icons\Icons" -OutputDll "src\OpenCMTools.Icons\bin\Release\net48\OpenCMTools.Icons.Duotone.dll"
.\installer\scripts\Build-IconsDll.ps1 -IconsDir "src\OpenCMTools.Icons\IconsOutline" -OutputDll "src\OpenCMTools.Icons\bin\Release\net48\OpenCMTools.Icons.Outline.dll"

# 2. Build full solution
dotnet build OpenCMTools.sln -c Release

# 3. Run unit tests (both net48 and net8.0)
dotnet test tests/OpenCMTools.Tests/OpenCMTools.Tests.csproj -c Release

# 4. Build WiX v4 MSI installer
dotnet build installer/OpenCMTools.Installer/OpenCMTools.Installer.wixproj -c Release
```

To compile `OpenCMTools.ConsoleExtension` directly against a locally installed MECM Admin Console instead of the compile-time stubs:

```powershell
dotnet build OpenCMTools.sln -c Release -p:ConfigMgrConsolePath="C:\Program Files (x86)\Microsoft Endpoint Manager\AdminConsole\bin"
```

---

## Building & Testing on Linux / macOS

`OpenCMTools.Core`, `OpenCMTools.Connectors`, `OpenCMTools.ConfigMgrSdk.Stubs`, and `OpenCMTools.Tests` (`net8.0`) build and run natively on Linux and macOS using Microsoft's official `net48` reference assemblies:

```bash
dotnet build src/OpenCMTools.Core/OpenCMTools.Core.csproj -c Release
dotnet build src/OpenCMTools.Connectors/OpenCMTools.Connectors.csproj -c Release
dotnet test tests/OpenCMTools.Tests/OpenCMTools.Tests.csproj --framework net8.0 -c Release
```

---

## Building the Documentation Site Locally

The documentation site uses **MkDocs Material** (`mkdocs.yml` + `docs/`):

```bash
pip install mkdocs-material
mkdocs serve
```

Then open `http://127.0.0.1:8000` in your browser to preview live edits and full-text search.

---

## Fork-Friendly Update Source

OpenCMTools never hardcodes a personal GitHub repository in source code. When built in GitHub Actions, `Directory.Build.props` automatically reads `$(GITHUB_REPOSITORY)` and embeds it as `AssemblyMetadata("UpdateRepository")`.

If you fork OpenCMTools:
1. Enable GitHub Actions on your fork to build MSI / Portable ZIP releases automatically on tags (`v*`).
2. Enable **GitHub Pages → Source: GitHub Actions** on your fork to host your own searchable documentation site.
3. Your fork's compiled binaries will automatically point **Check for Updates** at your fork's GitHub Releases (and users can still override or disable update checks in **Settings → Updates**).

---

## Coding Conventions for Pull Requests

- **Zero proprietary Microsoft MECM SDK DLLs** committed to the repository.
- **Zero mandatory third-party binaries or NuGet bloat** (only optional user-configured Sysinternals PsExec).
- **Pure 7-bit ASCII** in all `installer/scripts/*.ps1` and `console-extensions/**/*.xml` files (enforced by automated unit tests and Windows PowerShell 5.1 AST validation in CI).
- **Pure C# 5.0** in `src/OpenCMTools.ConsoleExtension/OpenCMToolsActionHandler.cs` and `DashboardConsoleViews.cs` so the built-in Windows `.NET Framework 4.8` `csc.exe` can compile them on any workstation.
- **Theme safety:** Always use `{DynamicResource ...}` for theme brushes/styles in XAML, and always set explicit `MinWidth` on every `Width="*"` `DataGridTextColumn` (enforced by automated unit tests).
