# OpenRCT — Open Source Right Click Tools for Microsoft Configuration Manager (MECM)

**OpenRCT** is a free, MIT-licensed, ad-free set of Right Click Tools and embedded console dashboards for **Microsoft Configuration Manager (MECM / SCCM)**. It allows administrators to manage **single devices, multiple selected devices simultaneously, or entire Device Collections** directly inside the MECM Admin Console or via the standalone launcher.

---

## Key Design Goals

- **100% Open Source & Ad-Free** — No trial prompts, licensing gates, or promotional banners.
- **Zero Extra Runtime Prerequisites** — Built on **.NET Framework 4.8 (C# / WPF)**, the exact runtime already required by the MECM Admin Console.
- **Zero Mandatory External Dependencies** — Uses native Windows/MECM interfaces (WMI/CIM, WinRM, SMB, LDAP/ADSI, SMS Provider, Wake-on-LAN).
- **Optional PsExec Support** — The only external tool optionally used is Sysinternals **PsExec** for *Console Tools → Interactive Command Prompt*. Users configure their own path to `PsExec.exe` or `PsExec64.exe` in **OpenRCT Settings**.
- **Single, Multi-Select & Collection Targeting** — Native MECM `AssemblyType` console extension plus a Named-Pipe multi-select aggregator fallback for `Executable` mode.

---

## Included Tools & Dashboards

### Right-Click Context Menu (`OpenRCT`)
- **Client Actions (12)**: Application Deployment Evaluation, Discovery Data Collection, File Collection, Hardware Inventory, Machine Policy Retrieval & Evaluation, Send Unsent State Messages, Software Inventory, Software Metering Usage Report, Software Updates Deployment Evaluation, Software Updates Scan, State Message Cache Cleanup, Windows Installer Source List Update.
- **Client Tools (13)**: Add Device(s) to Collection, Client Information (Details, Components, Cache Manager), Open Client Installation Log Folder, Open Client Log Folder, Refresh Updates Compliance State, Remote Software Center, Rerun Deployment, Restart SMS Agent Host Service, Run Client Check, Stop ConfigMgr Client Activities, Show Collections - Advanced (with Maintenance Windows), Repair Client, Uninstall SCCM Client.
- **Console Tools (21)**: AD Security Groups, Connect to `C$`, Delete/Disable/Enable Device(s) in Active Directory, Delete Device(s) from ConfigMgr, Group Policy Update, Interactive Command Prompt *(optional PsExec)*, Interactive PowerShell Prompt (WinRM), Manage Computer, Open Event Viewer, Open Regedit, Ping System(s), Running Processes (with Terminate), Send Notification, **System Information** (10 tabs: Operating Systems, Hardware, Disks, Applications with search/uninstall, Windows Updates, Services, Drivers, User Profiles, Local Group Members, Batteries), Wake On LAN, Cancel Pending Restart/Shutdown, Schedule Restart/Shutdown, Restart System(s), Shutdown System(s).
- **Security Tools (5)**: View LAPS Password *(Legacy & Windows LAPS)*, View BitLocker Recovery Key *(AD & ConfigMgr)*, Windows Defender Quick Scan, Full Scan, and Update Definitions.
- **Device Status Messages**: Filterable ConfigMgr status message viewer with CSV export.
- **OpenRCT Settings**: PsExec path browser & validator, concurrency/timeout limits, transport preference, theme (Light/Dark/System), and safety controls.

### Embedded Console Dashboards (*Assets and Compliance → Overview → OpenRCT*)
- **Phase 1 (Implemented)**:
  1. **Active Directory Cleanup Tool** — Compares AD OUs against ConfigMgr Collections with interactive pie chart (`ConfigMgr`, `Both`, `Active Directory`), 15-column grid, row cleanup actions, error drawer, and CSV export.
  2. **LAPS Dashboard** — Audits both Legacy Microsoft LAPS and Windows LAPS passwords & client install state with two interactive pie charts, 8-column grid, error drawer, and CSV export.
  3. **Software Updates Deployment Status** — Analyzes collection update compliance with deployed-only toggle, Software Update Group filter, age slider, 6 classification checkboxes, `Device Compliance Status` & `Missing Updates By Category` pie charts, remediation actions, and CSV export.
- **Phase 2 (Planned)**:
  4. **BitLocker Compliance**
  5. **Content Distribution Monitor**
  6. **Hardware and Firmware Audit**

---

## Installation

### Prerequisite (Required for MECM 2103 and newer)
Starting in Configuration Manager 2103, Microsoft introduced an **Only allow console extensions that are approved for the hierarchy** restriction that blocks locally installed `XmlStorage\Extensions` add-ons (including open-source extensions like OpenRCT).

Before installing or launching OpenRCT inside the MECM Admin Console:
1. Open the Configuration Manager Console and navigate to **Administration → Site Configuration → Sites**.
2. Click **Hierarchy Settings** in the top ribbon.
3. On the **General** tab, **uncheck** **`Only allow console extensions that are approved for the hierarchy`**.
4. Click **OK**, install OpenRCT, and **restart the Configuration Manager Console**.

### Option 1: MSI Installer (Recommended for Enterprise Deployment)
Download `OpenRCT.msi` from [GitHub Releases](../../releases) and run interactively, or silently:

```powershell
msiexec /i OpenRCT.msi /qn PSEXECPATH="C:\Tools\PsExec64.exe"
```

### Option 2: Portable ZIP + PowerShell Script
Extract `OpenRCT-portable.zip` and run from an elevated PowerShell prompt:

```powershell
.\scripts\Install-OpenRCT.ps1
# Optional: pre-set PsExec path or use Executable fallback mode
.\scripts\Install-OpenRCT.ps1 -PsExecPath "C:\Tools\PsExec64.exe" -Mode Assembly
```

Restart the Configuration Manager Console to load the new right-click menus and dashboard nodes under **Assets and Compliance → Overview → OpenRCT**. Full installer diagnostics are written to `C:\ProgramData\OpenRCT\install.log`.

### Uninstalling
Uninstall via **Add or Remove Programs** (MSI) or run:

```powershell
.\scripts\Uninstall-OpenRCT.ps1
```

---

## Building from Source

### On Windows (Full Build + WPF + WiX MSI)
Requires .NET 8 SDK (or Visual Studio 2022 with .NET desktop development workload):

```powershell
dotnet build OpenRCT.sln -c Release
dotnet test tests/OpenRCT.Tests/OpenRCT.Tests.csproj -c Release
dotnet build installer/OpenRCT.Installer/OpenRCT.Installer.wixproj -c Release
```

To compile `OpenRCT.ConsoleExtension` directly against a local MECM Admin Console:

```powershell
dotnet build OpenRCT.sln -c Release -p:ConfigMgrConsolePath="C:\Program Files (x86)\Microsoft Endpoint Manager\AdminConsole\bin"
```

### On Linux / macOS (Core, Connectors & Automated Tests)
```bash
dotnet build src/OpenRCT.Core/OpenRCT.Core.csproj -c Release
dotnet build src/OpenRCT.Connectors/OpenRCT.Connectors.csproj -c Release
dotnet test tests/OpenRCT.Tests/OpenRCT.Tests.csproj --framework net8.0 -c Release
```

---

## License

Licensed under the [MIT License](LICENSE).