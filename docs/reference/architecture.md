# Architecture, Protocols & Required Ports

## Solution Structure

OpenRCT is a clean multi-project C# / WPF solution targeting **.NET Framework 4.8** (with `OpenRCT.Core` and `OpenRCT.Tests` multi-targeting `net48;net8.0` for cross-platform unit testing):

| Project | Target | Responsibility |
|---|---|---|
| `OpenRCT.Core` | `net48;net8.0` | Domain models, `ClientActionRegistry`, `BulkActionRunner`, `MultiDeviceGrouper`, dashboard analytics, `SettingsService`, `CsvExporter`, `GitHubUpdateChecker`, Named-Pipe IPC |
| `OpenRCT.Connectors` | `net48` | Native Windows/MECM connectors: `WmiClient`, `DeviceReachabilityChecker`, `SmsProviderClient`, `ActiveDirectoryClient`, `SystemInfoCollector`, `EndpointActionService`, `RemoteWindowsSecurityService`, `RemoteEventViewerService`, `RemoteRegistryService` |
| `OpenRCT.UI` | `net48` (WPF) | All tool windows, 6 dashboards, reusable controls (`ExpandableGroupedGridControl`, `InteractivePieChart`, `VectorIcon`, `ErrorDrawerControl`), Light/Dark themes, `ThemeManager`, `ConsoleIconSyncService`, `ToolDispatcher` |
| `OpenRCT.App` | `net48` (WPF EXE) | Standalone launcher (`MainLauncherWindow`), CLI dispatcher, Named-Pipe multi-select aggregator, embedded Option B `OpenRCT.ico` |
| `OpenRCT.ConsoleExtension` | `net48` | In-process MECM Console bridge (`OpenRctActionHandler.cs` + `DashboardConsoleViews.cs`, pure C# 5.0) |
| `OpenRCT.ConfigMgrSdk.Stubs` | `net48` | Minimal compile-time stub interfaces for CI builds — **zero Microsoft proprietary SDK DLLs are committed** |
| `OpenRCT.Icons` *(assets folder)* | `net48` (Resource DLLs) | Dual native `.NET 4.8` icon resource assemblies (`OpenRCT.Icons.Duotone.dll` & `OpenRCT.Icons.Outline.dll`) compiled from `.ico` assets by `Build-IconsDll.ps1` (version auto-read from `Directory.Build.props`) |
| `OpenRCT.Installer` *(standalone `.wixproj`)* | WiX v4 | `OpenRCT.msi` per-machine installer with silent deployment properties and automatic console registration (built outside `OpenRCT.sln`) |
| `OpenRCT.Tests` | `net48;net8.0` | Automated xUnit test suite covering analytics, grouping, IPC, XML/XAML integrity, ASCII safety, and `.NET 4.8` icon resource loading |

---

## How Zero-SDK MECM Console Integration Works

Because Microsoft's Configuration Manager Console SDK DLLs are proprietary and version-specific, OpenRCT **never bundles Microsoft SDK binaries**:

1. During installation (`Install-OpenRCT.ps1`), OpenRCT inspects your workstation's local `<AdminConsole>\bin\` assemblies via .NET Reflection to discover the exact `OverviewControllerBase`, `Overview`, and `IConsoleView2` types for your installed MECM version.
2. It compiles `OpenRctActionHandler.cs` and `DashboardConsoleViews.cs` directly against your local MECM Console binaries using the built-in `.NET Framework 4.8` `csc.exe`, producing a strongly matched `OpenRCT.ConsoleExtension.dll`.
3. `OpenRCT.ConsoleExtension.dll` registers a custom `AppDomain.AssemblyResolve` handler (`OpenRctAssemblyResolver`) reading `OpenRCT.installdir.txt` / `HKLM\SOFTWARE\OpenRCT\InstallDir` so all OpenRCT WPF assemblies load cleanly from `C:\Program Files\OpenRCT` without polluting `<AdminConsole>\bin\`.
4. Both `OpenRCT.Icons.Duotone.dll` and `OpenRCT.Icons.Outline.dll` embed identical `OpenRCT.Icons.ConsoleIcons.resources` stream names and 39 icon keys; switching Icon Style simply deploys the chosen DLL to `<AdminConsole>\bin\OpenRCT.Icons.dll` with zero XML changes.

---

## Required Network Ports & Services

| Feature / Tool | Protocol / Service | Port(s) | Endpoint Requirement |
|---|---|---|---|
| **SMS Provider (Dashboards, Collections, DP Actions, WOL MACs, Status Messages)** | WMI / DCOM (`root\sms\site_<CODE>`) | TCP `135` + Dynamic RPC (`49152–65535`) | SMS Provider RBAC permissions for your admin account |
| **Client Actions, System Info, Remote Software Center, Remote Windows Security, Defender, Services, Processes** | WMI / DCOM | TCP `135` + Dynamic RPC (`49152–65535`) | Windows Firewall `Windows Management Instrumentation (WMI-In)` enabled; Local Admin on target |
| **Interactive PowerShell Prompt** | WinRM / WS-Man (HTTP) | TCP `5985` | `Enable-PSRemoting` on target endpoints |
| **Remote Registry Editor & Applications Tab** | Remote Registry (`RemoteRegistry` service) + SMB | TCP `445` | `RemoteRegistry` service running (or trigger-startable) on target |
| **Remote Event Viewer** | Windows Event Log RPC (`EventLogSession`) | TCP `135` + Dynamic RPC (`49152–65535`) | Windows Firewall `Remote Event Log Management` rules enabled |
| **Connect to `C$`, Client Log Folders, Optional PsExec** | SMB | TCP `445` | Admin shares enabled (`C$`, `ADMIN$`) |
| **Active Directory Tools, LAPS & BitLocker Keys** | LDAP / LDAPS (ADSI) | TCP `389` / `636` | Read/Write AD permissions (`msLAPS-Password`, `msLAPS-EncryptedPassword`, `ms-Mcs-AdmPwd`, `msFVE-RecoveryInformation`, Computer accounts) |
| **Ping System(s) & Fast Offline Pre-Check** | ICMP Echo + RPC/SMB TCP probe | ICMP + TCP `135` / `445` | Echo Request or RPC/SMB reachable |
| **Wake On LAN** | UDP Magic Packet (global broadcast `255.255.255.255`) | UDP `9` & `7` | Wake-on-LAN enabled in BIOS/NIC |
| **Manual Update Check *(optional)*** | HTTPS (`api.github.com`) | TCP `443` | Only when you manually click **Check for Updates** |
