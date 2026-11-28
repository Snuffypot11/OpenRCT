# Installation & Upgrading

OpenRCT can be installed using the **WiX v4 MSI installer** (recommended for workstation/enterprise deployment) or the **Portable ZIP + PowerShell script**.

---

## System Requirements

- **OS:** Windows 10 / Windows 11 / Windows Server 2016+ (x64)
- **Runtime:** .NET Framework 4.8 *(already installed on Windows 10 1903+, Windows 11, and required by the MECM Admin Console)*
- **MECM Admin Console:** Optional for standalone use; required for embedded console dashboards and right-click menus.
- **Permissions:** Local Administrator rights on the workstation running the MECM Console during installation (to write to `Program Files\OpenRCT` and `<AdminConsole>\XmlStorage\Extensions` + `<AdminConsole>\bin`).

---

## Prerequisite: MECM 2103+ Console Extensions Setting

!!! warning "Required for Configuration Manager 2103 and Newer"
    Microsoft Configuration Manager 2103 introduced a hierarchy-wide security setting that blocks locally installed `XmlStorage\Extensions` add-ons (including open-source extensions like OpenRCT) unless disabled.

Before launching OpenRCT inside the MECM Admin Console:

1. Open the **Configuration Manager Console**.
2. Navigate to **Administration → Site Configuration → Sites**.
3. Select **Hierarchy Settings** in the top ribbon.
4. On the **General** tab, **uncheck**:
   > **`Only allow console extensions that are approved for the hierarchy`**
5. Click **OK**.
6. Install OpenRCT and **restart the Configuration Manager Console**.

---

## Option 1: MSI Installer (Recommended)

Download `OpenRCT.msi` from the repository's **GitHub Releases** page.

### Interactive Install
Close the Configuration Manager Console (recommended) and double-click `OpenRCT.msi`.

### Silent / Enterprise Deployment (`msiexec`)
```powershell
# Standard silent install
msiexec /i OpenRCT.msi /qn

# Silent install with optional Sysinternals PsExec path pre-configured
msiexec /i OpenRCT.msi /qn PSEXECPATH="C:\Tools\PsExec64.exe"

# Silent install with explicit MECM Admin Console path or Executable fallback mode
msiexec /i OpenRCT.msi /qn CONSOLEROOT="D:\Program Files\Microsoft Configuration Manager\AdminConsole" INSTALLMODE="Assembly"
```

#### Supported MSI Properties

| Property | Default | Description |
|---|---|---|
| `PSEXECPATH` | *(blank)* | Optional path to `PsExec.exe` or `PsExec64.exe` passed to `Install-OpenRCT.ps1`. *(Note: the per-machine MSI custom action runs as `LocalSystem`, so `PSEXECPATH` writes under the SYSTEM profile's AppData; interactive users can also set PsExec directly in **OpenRCT Settings** or via `Install-OpenRCT.ps1 -PsExecPath`.)* |
| `CONSOLEROOT` | *(auto-detected)* | Explicit path to the MECM `AdminConsole` folder if installed in a non-standard location |
| `INSTALLMODE` | `Assembly` | `Assembly` (native in-process MECM integration + embedded dashboards) or `Executable` (external CLI + Named-Pipe multi-select fallback) |

---

## Option 2: Portable ZIP + PowerShell Installer

1. Download `OpenRCT-portable.zip` from **GitHub Releases** and extract it to a temporary folder.
2. Open an **elevated PowerShell** prompt in the extracted folder.
3. Run `Install-OpenRCT.ps1`:

```powershell
# Standard install (auto-detects MECM Console, binds native extension, installs to C:\Program Files\OpenRCT)
.\scripts\Install-OpenRCT.ps1

# Custom install directory, PsExec path, and explicit Crisp Monochrome Outline icon style
.\scripts\Install-OpenRCT.ps1 `
    -InstallDir "C:\Program Files\OpenRCT" `
    -PsExecPath "C:\Tools\PsExec64.exe" `
    -IconStyle Outline
```

### `Install-OpenRCT.ps1` Parameters

| Parameter | Default | Description |
|---|---|---|
| `-InstallDir` | `C:\Program Files\OpenRCT` | Target installation folder |
| `-ConsoleRoot` | *(auto-detected)* | Explicit path to `...\AdminConsole` |
| `-Mode` | `Assembly` | `Assembly` (recommended) or `Executable` |
| `-PsExecPath` | *(blank)* | Optional pre-configured PsExec executable path (written to `%AppData%\OpenRCT\settings.json` on first run if no settings file exists yet) |
| `-IconStyle` | *(auto-detect from `settings.json`, default `Duotone`)* | `Duotone` or `Outline` — selects which MECM Console icon assembly is deployed to `<AdminConsole>\bin\OpenRCT.Icons.dll` *(MSI per-machine installs run as `LocalSystem` and default to `Duotone`; re-sync anytime from Settings → Apply to MECM Console)* |
| `-ApplyIconStyleOnly` | `$false` | Fast-path switch that only swaps `<AdminConsole>\bin\OpenRCT.Icons.dll` without recompiling `OpenRCT.ConsoleExtension.dll` or rewriting XML manifests |
| `-SkipBinaryCopy` | `$false` | Used internally by the MSI custom action after WiX copies files |

---

## What the Installer Does

1. Installs OpenRCT binaries, both icon resource assemblies (`OpenRCT.Icons.Duotone.dll` and `OpenRCT.Icons.Outline.dll`), installer scripts, XML templates, and C# console bridge source into `C:\Program Files\OpenRCT`.
2. Records `InstallDir` in `HKLM:\SOFTWARE\OpenRCT`.
3. Auto-detects the local MECM Admin Console (`SMS_ADMIN_UI_PATH`, ConfigMgr registry keys, and standard `Program Files` paths).
4. Compiles `OpenRCT.ConsoleExtension.dll` directly against your workstation's exact installed MECM Console assemblies using `.NET Framework 4.8` `csc.exe` (no proprietary Microsoft SDK DLLs are bundled in OpenRCT).
5. Deploys `OpenRCT.ConsoleExtension.dll`, `OpenRCT.installdir.txt`, and the selected `OpenRCT.Icons.dll` into `<AdminConsole>\bin\`, rotating locked files cleanly if the console is still open.
6. Deploys console extension XML manifests for **All Devices** and **Devices in Collection** (and, in `Assembly` mode when bound cleanly, **Device Collections** and **Assets and Compliance → Overview → OpenRCT**; `Executable` fallback mode deploys a reduced 11-tool device menu plus OpenRCT Settings).
7. Writes a full diagnostic log to **`C:\ProgramData\OpenRCT\install.log`**.

---

## Upgrading

- **MSI:** Run the newer `OpenRCT.msi` directly — WiX performs an in-place major upgrade and preserves your user settings in `%AppData%\OpenRCT\settings.json`.
- **Portable ZIP:** Extract the new release and re-run `.\scripts\Install-OpenRCT.ps1`.
- Restart the Configuration Manager Console after upgrading so the updated assemblies and icons are loaded.

---

## Uninstalling

- **MSI:** Uninstall **OpenRCT** from **Windows Settings → Apps → Installed Apps** (or `msiexec /x OpenRCT.msi /qn`).
- **Portable ZIP:** Run from an elevated PowerShell prompt:
  ```powershell
  & "C:\Program Files\OpenRCT\scripts\Uninstall-OpenRCT.ps1"
  ```
- Uninstalling removes all `OpenRCT_*.xml` files, `OpenRCT.ConsoleExtension.dll`, `OpenRCT.Icons.dll`, `OpenRCT.installdir.txt`, Start Menu shortcuts, and `HKLM:\SOFTWARE\OpenRCT`. User preferences under `%AppData%\OpenRCT\settings.json` are preserved unless deleted manually.
