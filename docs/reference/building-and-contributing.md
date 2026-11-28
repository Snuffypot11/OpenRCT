# Building from Source & Contributing

## AI-Assisted Development Transparency

OpenRCT was built with substantial **AI-assisted development** under human product direction, architectural constraints, and live testing in a multi-VM Microsoft Configuration Manager lab.

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
.\installer\scripts\Build-IconsDll.ps1 -IconsDir "src\OpenRCT.Icons\Icons" -OutputDll "src\OpenRCT.Icons\bin\Release\net48\OpenRCT.Icons.Duotone.dll"
.\installer\scripts\Build-IconsDll.ps1 -IconsDir "src\OpenRCT.Icons\IconsOutline" -OutputDll "src\OpenRCT.Icons\bin\Release\net48\OpenRCT.Icons.Outline.dll"

# 2. Build full solution
dotnet build OpenRCT.sln -c Release

# 3. Run unit tests (both net48 and net8.0)
dotnet test tests/OpenRCT.Tests/OpenRCT.Tests.csproj -c Release

# 4. Build WiX v4 MSI installer
dotnet build installer/OpenRCT.Installer/OpenRCT.Installer.wixproj -c Release
```

To compile `OpenRCT.ConsoleExtension` directly against a locally installed MECM Admin Console instead of the compile-time stubs:

```powershell
dotnet build OpenRCT.sln -c Release -p:ConfigMgrConsolePath="C:\Program Files (x86)\Microsoft Endpoint Manager\AdminConsole\bin"
```

---

## Building & Testing on Linux / macOS

`OpenRCT.Core`, `OpenRCT.Connectors`, `OpenRCT.ConfigMgrSdk.Stubs`, and `OpenRCT.Tests` (`net8.0`) build and run natively on Linux and macOS using Microsoft's official `net48` reference assemblies:

```bash
dotnet build src/OpenRCT.Core/OpenRCT.Core.csproj -c Release
dotnet build src/OpenRCT.Connectors/OpenRCT.Connectors.csproj -c Release
dotnet test tests/OpenRCT.Tests/OpenRCT.Tests.csproj --framework net8.0 -c Release
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

OpenRCT never hardcodes a personal GitHub repository in source code. When built in GitHub Actions, `Directory.Build.props` automatically reads `$(GITHUB_REPOSITORY)` and embeds it as `AssemblyMetadata("UpdateRepository")`.

If you fork OpenRCT:
1. Enable GitHub Actions on your fork to build MSI / Portable ZIP releases automatically on tags (`v*`).
2. Enable **GitHub Pages → Source: GitHub Actions** on your fork to host your own searchable documentation site.
3. Your fork's compiled binaries will automatically point **Check for Updates** at your fork's GitHub Releases (and users can still override or disable update checks in **Settings → Updates**).

---

## Coding Conventions for Pull Requests

- **Zero proprietary Microsoft MECM SDK DLLs** committed to the repository.
- **Zero mandatory third-party binaries or NuGet bloat** (only optional user-configured Sysinternals PsExec).
- **Pure 7-bit ASCII** in all `installer/scripts/*.ps1` and `console-extensions/**/*.xml` files (enforced by automated unit tests and Windows PowerShell 5.1 AST validation in CI).
- **Pure C# 5.0** in `src/OpenRCT.ConsoleExtension/OpenRctActionHandler.cs` and `DashboardConsoleViews.cs` so the built-in Windows `.NET Framework 4.8` `csc.exe` can compile them on any workstation.
- **Theme safety:** Always use `{DynamicResource ...}` for theme brushes/styles in XAML, and always set explicit `MinWidth` on every `Width="*"` `DataGridTextColumn` (enforced by automated unit tests).
