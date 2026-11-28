# OpenRCT Testing VM Lab — Setup Guide & Automation Scripts

This guide walks you through building the **minimum viable isolated Hyper-V lab** on a **32 GB Windows 10/11 Pro/Enterprise host** to test **every OpenRCT feature** — all 10 Remote Tools, 12 Client Actions, 13 Client Tools, 6 Device and Power Tools, 5 Security Tools, 5 Active Directory and ConfigMgr Tools, and all 6 embedded MECM Console Dashboards — while keeping your host machine **completely excluded from the lab domain and lab network**.

---

## Architecture Overview

| VM Name | Role | Static / DHCP IP | vCPU | RAM (Min / Startup / Max) | Disks (Thin VHDX) |
| :--- | :--- | :--- | :---: | :--- | :--- |
| **`LAB-DC01`** | Active Directory (`lab.local`), DNS, DHCP, GPOs, LAPS Schema | `10.20.30.10` (Static) | 2 | 1 GB / 2 GB / 2 GB | `C:` 40 GB |
| **`LAB-CM01`** | MECM Primary Site (`LAB`), SQL Server, SMS Provider, MP, DP, SUP/WSUS, MECM Console, **OpenRCT** | `10.20.30.20` (Static) | 4 | 6 GB / 8 GB / 10 GB *(SQL capped at 4 GB)* | `C:` 80 GB<br>`E:` 120 GB (`MECM-Data`) |
| **`LAB-WS01`** | Win 11 Enterprise (vTPM), MECM Client, **Windows LAPS**, **BitLocker (`D:`)**, 7-Zip, 2 User Profiles | `10.20.30.100–150` (DHCP) | 2 | 2 GB / 4 GB / 4 GB | `C:` 60 GB<br>`D:` 2 GB (`BitLockerTest`) |
| **`LAB-WS02`** | Win 11 Enterprise (vTPM), MECM Client, **Legacy LAPS x64**, Multi-Select & Destructive Test Target | `10.20.30.100–150` (DHCP) | 2 | 2 GB / 3 GB / 4 GB | `C:` 60 GB |

- **Lab Domain:** `lab.local` (NetBIOS: `LAB`)
- **Isolated Subnet:** `10.20.30.0/24` on Hyper-V Internal vSwitch `OpenRCT-LabNet` with outbound-only WinNAT (`10.20.30.1`)
- **Host Isolation:** Your host OS has **no DNS** on `vEthernet (OpenRCT-LabNet)`, SMB/Client bindings disabled, Public network profile, and an inbound firewall block rule against `10.20.30.0/24`. Files are copied into VMs via **Hyper-V Guest Services** (`Copy-VMFile`) with zero host network shares.

### Included Automation Scripts (`scripts/`)

| Script | Where to Run | Purpose |
| :--- | :--- | :--- |
| [`00-Host-Copy-FilesToVM.ps1`](scripts/00-Host-Copy-FilesToVM.ps1) | Hyper-V Host | Copies files or folders into any running VM via Hyper-V Guest Services (`Copy-VMFile`) |
| [`01-Host-Setup-HyperVLab.ps1`](scripts/01-Host-Setup-HyperVLab.ps1) | Hyper-V Host | Creates isolated vSwitch, WinNAT, host firewall block rules, and all 4 Gen 2 VMs (also supports `-RemoveLab`) |
| [`02-DC01-Install-AD-DNS-DHCP.ps1`](scripts/02-DC01-Install-AD-DNS-DHCP.ps1) | `LAB-DC01` | Sets static IP, renames VM, installs AD DS / DNS / DHCP / GPMC, and promotes `lab.local` |
| [`03-DC01-Configure-Domain-GPO-LAPS.ps1`](scripts/03-DC01-Configure-Domain-GPO-LAPS.ps1) | `LAB-DC01` | Configures DNS forwarders, DHCP scope, OUs, test users/groups, `LAB-ORPHAN-AD`, `System Management` ACL, LAPS schema, and all 4 GPOs |
| [`04-CM01-Prepare-Prereqs.ps1`](scripts/04-CM01-Prepare-Prereqs.ps1) | `LAB-CM01` | Joins domain, formats `E:\`, creates `C:\NO_SMS_ON_DRIVE.SMS`, installs IIS/BITS/RDC/WSUS/RSAT, opens firewall ports, and creates starter MECM objects |
| [`05-WS-Configure-Workstation.ps1`](scripts/05-WS-Configure-Workstation.ps1) | `LAB-WS01` / `LAB-WS02` | Joins `OU=Workstations`, configures WinRM/RemoteRegistry/`AllowRemoteRPC`, encrypts `D:` with BitLocker AD backup + Windows LAPS (`LAB-WS01`), or installs Legacy LAPS (`LAB-WS02`) |
| [`06-CM01-Verify-LabHealth.ps1`](scripts/06-CM01-Verify-LabHealth.ps1) | `LAB-CM01` | Automated PASS/WARN/FAIL verifier checking every OpenRCT protocol, AD attribute, and SMS Provider WMI query |

---

## Before You Start — Software Download Checklist

Download these free Microsoft Evaluation ISOs and tools to a folder on your host (for example, `D:\LabMedia\`):

1. **Windows Server 2022 or 2025 Standard Evaluation ISO** — Microsoft Evaluation Center
2. **Windows 11 Enterprise Evaluation ISO** (23H2 or 24H2) — Microsoft Evaluation Center
3. **SQL Server 2022 Evaluation ISO** *(recommended for ConfigMgr 2509 baseline; SQL Server 2025 support starts in ConfigMgr 2603)* — Microsoft Evaluation Center *(MECM Primary Site requires Standard/Enterprise/Eval — not SQL Express)*
4. **SQL Server Management Studio (SSMS)** — Microsoft Learn
5. **Microsoft ODBC Driver 18 for SQL Server** (`msodbcsql.msi`) — **Use version `18.6.2.1` or `18.4.1.1` ONLY!** *(Do **NOT** install `18.7.1.1` or `18.6.1.1` on ConfigMgr 2603 or earlier — Microsoft documented a blocking crash in `msodbcsql18.dll` that breaks SMS Provider & `SMS_EXECUTIVE`.)*
6. **Microsoft Configuration Manager (Current Branch) Evaluation** (baseline media, e.g., 2509 / 2603 / 2609) — Microsoft Evaluation Center
7. **Windows ADK** (`adksetup.exe`) **+ Windows PE Add-on** (`adkwinpesetup.exe`) — Microsoft Learn
8. **Legacy Microsoft LAPS** (`LAPS.x64.msi`) — Microsoft Download Center *(optional, to test both Legacy LAPS and Windows LAPS side-by-side)*
9. **7-Zip MSI (64-bit x64)** (`7z*-x64.msi`) — 7-zip.org *(sample test application for Remote Software Center & Cache Manager)*
10. **Sysinternals PsExec** (`PsExec64.exe`) — Microsoft Sysinternals *(optional, for Remote Tools → Interactive Command Prompt)*

---

## Phase 0: Hyper-V Host Setup (Automated)

### 0.1 Enable Hyper-V (Manual — skip if already enabled)
Open an elevated PowerShell prompt on your host and run:
```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All
```
Reboot your host if prompted.

### 0.2 Provision the Isolated Network & All 4 VMs (Automated)
From an **elevated PowerShell** on your host (replace paths with your actual SSD and ISO paths):

```powershell
cd path\to\OpenRCT\docs\lab-setup\scripts

.\01-Host-Setup-HyperVLab.ps1 `
    -LabRoot "D:\HyperV\OpenRCT-Lab" `
    -ServerIsoPath "D:\LabMedia\WinServer2022_Eval.iso" `
    -WorkstationIsoPath "D:\LabMedia\Win11_Enterprise_Eval.iso" `
    -BlockHostLanSubnets @("192.168.0.0/16", "172.16.0.0/12")
```

> **What this isolates:**
> - Your host receives `10.20.30.1` strictly as a NAT gateway on `vEthernet (OpenRCT-LabNet)` with **no DNS**, **Public** profile, SMB/Client bindings disabled, and an **Inbound Block** rule against `10.20.30.0/24`.
> - `-BlockHostLanSubnets` also prevents lab VMs from reaching your home/office LAN while still letting them reach the internet for WSUS metadata, Defender updates, and MECM prerequisites.

---

## Phase 1: Domain Controller (`LAB-DC01`)

### 1.1 Install Windows Server on `LAB-DC01` (Manual)
1. Open **Hyper-V Manager**, right-click **`LAB-DC01` → Connect → Start**.
2. Press any key when prompted to boot from DVD.
3. Choose **Windows Server 2022/2025 Standard Evaluation (Desktop Experience)** *(recommended for easy GPO inspection; Server Core also works with 1 GB RAM)*.
4. Choose **Custom: Install Windows only**, select the 40 GB disk, and finish installation.
5. Set the local `Administrator` password and sign in.

### 1.2 Copy Scripts into `LAB-DC01` (Automated from Host)
On your **host** (elevated PowerShell):
```powershell
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-DC01 -SourcePath "." -DestinationPath "C:\LabScripts"
```
*(Optional: If testing Legacy LAPS on `LAB-WS02`, also copy `LAPS.x64.msi` into `LAB-DC01`:)*
```powershell
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-DC01 -SourcePath "D:\LabMedia\LAPS.x64.msi" -DestinationPath "C:\Install\LAPS.x64.msi"
```

### 1.3 Promote `LAB-DC01` to `lab.local` (Automated inside `LAB-DC01`)
Inside `LAB-DC01`, open an **elevated PowerShell**:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope LocalMachine -Force
cd C:\LabScripts

# First run: sets static IP 10.20.30.10/24, renames to LAB-DC01, and reboots
.\02-DC01-Install-AD-DNS-DHCP.ps1
```
After `LAB-DC01` reboots, sign back in and **re-run the exact same script**:
```powershell
# Second run: installs AD DS, DNS, DHCP, GPMC and promotes lab.local (reboots automatically)
C:\LabScripts\02-DC01-Install-AD-DNS-DHCP.ps1
```
Enter a DSRM password when prompted and wait for the automatic reboot.

### 1.4 Configure DNS, DHCP, OUs, Test Accounts, LAPS & GPOs (Automated inside `LAB-DC01`)
Sign back into `LAB-DC01` as **`LAB\Administrator`**.

*(Optional — only if you want Legacy LAPS on `LAB-WS02`)*: Before running script `03`, install just the Legacy LAPS PowerShell management tools on `LAB-DC01`:
```powershell
msiexec /i C:\Install\LAPS.x64.msi ADDLOCAL=Management.PS /qn
```

Now run script `03`:
```powershell
C:\LabScripts\03-DC01-Configure-Domain-GPO-LAPS.ps1
```
Enter a lab password when prompted (assigned to `LAB\cmadmin`, `LAB\cm_push`, `LAB\testuser1`, and `LAB\testuser2`).

> **Why Windows LAPS password encryption is disabled in `GPO-Windows-LAPS`:**
> `OpenRCT`'s `ActiveDirectoryClient.GetLapsPasswordAsync` currently reads the cleartext `msLAPS-Password` attribute. The script automatically configures `ADPasswordEncryptionEnabled = 0` so Windows LAPS password retrieval works out of the box.

### 1.5 Take Checkpoint #1 (Manual — Host)
In Hyper-V Manager, right-click **`LAB-DC01` → Checkpoint** and rename it **`DC-Ready`**.

---

## Phase 2: MECM Primary Site Server (`LAB-CM01`)

### 2.1 Install Windows Server on `LAB-CM01` (Manual)
1. Start **`LAB-CM01`** in Hyper-V Manager and install **Windows Server 2022/2025 Standard Evaluation (Desktop Experience)** onto the **80 GB** drive (leave the 120 GB drive unallocated — the script will format it as `E:\`).
2. Sign in as local Administrator.

### 2.2 Copy Scripts & Installers into `LAB-CM01` (Automated from Host)
From your **host**:
```powershell
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "." -DestinationPath "C:\LabScripts"
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "D:\LabMedia\msodbcsql.msi" -DestinationPath "C:\Install\msodbcsql.msi"
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "D:\LabMedia\SSMS-Setup-ENU.exe" -DestinationPath "C:\Install\SSMS-Setup-ENU.exe"
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "D:\LabMedia\adksetup.exe" -DestinationPath "C:\Install\adksetup.exe"
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "D:\LabMedia\adkwinpesetup.exe" -DestinationPath "C:\Install\adkwinpesetup.exe"
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "D:\LabMedia\7z-x64.msi" -DestinationPath "C:\Install\7z-x64.msi"
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "D:\LabMedia\PsExec64.exe" -DestinationPath "C:\Tools\PsExec64.exe"
```
*(For large ISOs like SQL Server and ConfigMgr, simply mount them via Hyper-V Manager → `LAB-CM01` → Settings → DVD Drive → Image file!)*

### 2.3 Join Domain & Install OS Prerequisites (Automated inside `LAB-CM01`)
Inside `LAB-CM01` (elevated PowerShell):
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope LocalMachine -Force

# Step A: Static IP 10.20.30.20/24, rename to LAB-CM01, join lab.local (OU=Servers), reboot
C:\LabScripts\04-CM01-Prepare-Prereqs.ps1 -Phase NetworkAndJoin
```
After reboot, sign in as **`LAB\cmadmin`** (not local Administrator!) and run:
```powershell
# Step B: Format E:\ (MECM-Data), create C:\NO_SMS_ON_DRIVE.SMS, install IIS/BITS/RDC/WSUS/RSAT & firewall rules
C:\LabScripts\04-CM01-Prepare-Prereqs.ps1 -Phase RolesAndDisks
```

### 2.4 Delegate `System Management` Container Permissions (Automated on `LAB-DC01`)
Switch to **`LAB-DC01`** and run:
```powershell
C:\LabScripts\03-DC01-Configure-Domain-GPO-LAPS.ps1 -GrantCm01SystemManagementAcl
```

### 2.5 Install SQL Server 2022/2025 Evaluation + SSMS + ODBC 18 (Manual on `LAB-CM01`)
1. Mount the **SQL Server Evaluation ISO** on `LAB-CM01` and run `setup.exe` as `LAB\cmadmin`.
2. Choose **Installation → New SQL Server stand-alone installation**:
   - **Product Key:** Evaluation (free).
   - **Feature Selection:** Check **Database Engine Services** only.
   - **Instance Configuration:** **Default instance** (`MSSQLSERVER`).
   - **Server Configuration → Service Accounts (CRITICAL):** Set **SQL Server Database Engine** and **SQL Server Agent** Startup Type to **Automatic**, and change **Account Name** from `NT Service\MSSQLSERVER` to **`NT AUTHORITY\SYSTEM`** (`Local System`) or **`NT AUTHORITY\NETWORK SERVICE`** *(MECM Prerequisite Check hard-fails if SQL runs under `NT SERVICE\<name>`)*.
   - **Server Configuration → Collation (CRITICAL):** Click **Customize** and verify collation is **`SQL_Latin1_General_CP1_CI_AS`**.
   - **Database Engine Configuration → Server Configuration:** Windows authentication mode; click **Add Current User** (`LAB\cmadmin`).
   - **Database Engine Configuration → Data Directories:** Point Data root/User DB directory to `E:\SQLData` and Log directory to `E:\SQLLogs`.
   - Complete the installation.
3. **Enable TCP/IP & Verify SQL Service Account in SQL Server Configuration Manager:**
   - Open **SQL Server Configuration Manager** → **SQL Server Network Configuration** → **Protocols for MSSQLSERVER** → Enable **TCP/IP** → Right-click **TCP/IP → Properties → IP Addresses** → under **IPAll**, clear TCP Dynamic Ports and set **TCP Port = `1433`**.
   - Click **SQL Server Services** → confirm **SQL Server (MSSQLSERVER)** is running as **`Local System`** (or **`Network Service`**) — if not, double-click it → **Log On** tab → **Built-in account** → **`Local System`** → click **Apply** (allows automatic SPN registration under `LAB-CM01$`).
   - Restart the **SQL Server (MSSQLSERVER)** service.
4. **Install SSMS** (`C:\Install\SSMS-Setup-ENU.exe`) and **ODBC Driver 18** (`C:\Install\msodbcsql.msi`).
5. **Cap SQL Memory at 4096 MB & Grant `LAB-CM01$` `sysadmin` (in SSMS):**
   - Connect to `LAB-CM01` in SSMS.
   - Right-click the server instance → **Properties → Memory**:
     - Minimum server memory (in MB): `2048`
     - Maximum server memory (in MB): **`4096`** *(critical on a 32 GB host!)*
   - Expand **Security → Logins** → Right-click **Logins → New Login**:
     - Login name: `LAB\LAB-CM01$` (Windows authentication)
     - **Server Roles:** Check **`sysadmin`** → Click **OK**.

### 2.6 Install Windows ADK + Windows PE Add-on (Manual on `LAB-CM01`)
1. Run `C:\Install\adksetup.exe`:
   - Select **Deployment Tools** and **User State Migration Tool (USMT)** (uncheck the rest to save space).
2. Run `C:\Install\adkwinpesetup.exe`:
   - Select **Windows Preinstallation Environment (Windows PE)** and install.

### 2.7 Configure WSUS in SQL Mode (One-Line Command on `LAB-CM01`)
Run in an elevated PowerShell on `LAB-CM01` (do **not** launch the WSUS GUI wizard yet):
```powershell
& "C:\Program Files\Update Services\Tools\wsusutil.exe" postinstall SQL_INSTANCE_NAME=LAB-CM01 CONTENT_DIR=E:\WSUS
```
Verify it prints `Post install is successfully finished.`

### 2.8 Extend Active Directory Schema for MECM & Reboot (Manual on `LAB-CM01`)
1. Mount/extract the **Microsoft Configuration Manager Evaluation** media on `LAB-CM01` (e.g., `D:\` or `E:\CMMedia\`).
2. Because `LAB\cmadmin` is a Schema Admin, open an elevated Command Prompt on `LAB-CM01` and run:
   ```cmd
   E:\CMMedia\SMSSETUP\BIN\X64\extadsch.exe
   ```
3. Open `C:\extadsch.log` and confirm the last line says **`Successfully extended the Active Directory schema.`**.
4. **Reboot `LAB-CM01`** now so SQL Server / ODBC / ADK pending restarts clear before running MECM Setup!

### 2.9 Run the MECM Primary Site Installation Wizard (Manual on `LAB-CM01`)
Run `splash.hta` (or `SMSSETUP\BIN\X64\setup.exe`) as `LAB\cmadmin` and click **Install**:

| Wizard Page | Setting to Choose |
| :--- | :--- |
| **Getting Started** | Install a Configuration Manager primary site *(uncheck "Use typical installation options")* |
| **Product Key** | Install the evaluation edition of this product |
| **Prerequisite Downloads** | Download required files → Path: `E:\CMPrereqs` |
| **Site and Installation Settings** | Site Code: **`LAB`**<br>Site Name: **`OpenRCT Lab Primary`**<br>Installation folder: **`E:\Program Files\Microsoft Configuration Manager`** |
| **Primary Site Installation** | Install the primary site as a stand-alone site |
| **Database Information** | Server name: **`LAB-CM01.lab.local`**<br>Database name: **`CM_LAB`**<br>Service Broker Port: `4022` |
| **Database Information (Paths)** | Data file: `E:\SQLData`<br>Log file: `E:\SQLLogs` |
| **SMS Provider Settings** | `LAB-CM01.lab.local` |
| **Client Computer Communication** | Configure the communication method on each site system role *(next screen: select **HTTP** and check **Use Configuration Manager-generated certificates / Enhanced HTTP** if prompted)* |
| **Site System Roles** | Install Management Point (`LAB-CM01.lab.local`, HTTP) and Distribution Point (`LAB-CM01.lab.local`, HTTP) |
| **Diagnostic and Usage Data** | Next |
| **Service Connection Point** | Yes, let's get connected (`LAB-CM01.lab.local`) |
| **Prerequisite Check** | Resolve any **Failed** items; the 3 lab **Warnings** below are safe to ignore, then click **Begin Install** *(takes ~25–35 minutes)* |

> **Expected Harmless Lab Warnings on ConfigMgr 2509+ / Windows Server 2025:**
> - **`SQL Server Native Client version` (Warning):** Safe to ignore — ConfigMgr uses **ODBC Driver 18** + OLE DB Driver automatically; the legacy SQL 2012 Native Client is deprecated.
> - **`SQL Server security mode` (Warning):** Known false positive when SQL Server hasn't been rebooted or on newer SQL Server builds even when SSMS shows **Windows Authentication mode** (`LoginMode = 1`). Safe to ignore once confirmed in SSMS.
> - **`SQL Server process memory allocation` (Warning):** Expected because we intentionally capped SQL at `2048–4096 MB` for a 32 GB host instead of the production 8 GB minimum. Do **not** raise it to 8 GB on a 10 GB VM.


### 2.10 Post-Install MECM Configuration (Automated + Manual)

#### A. Auto-Create Boundary, Boundary Group, Collections, Maintenance Window & Test Package (Automated)
Once MECM setup finishes, open a **new** elevated PowerShell on `LAB-CM01` as `LAB\cmadmin` and run:
```powershell
C:\LabScripts\04-CM01-Prepare-Prereqs.ps1 -Phase PostMecmObjects -SiteCode LAB
```

#### B. Configure Enhanced HTTP, Discovery, Client Push, SUP & Sample App (Manual in MECM Console)
Open the **Configuration Manager Console** on `LAB-CM01`:

1. **Enable Enhanced HTTP (eHTTP):**
   - Go to **Administration → Site Configuration → Sites → `LAB` → Properties → Communication Security** tab.
   - Select **HTTPS or HTTP** and check **Use Configuration Manager-generated certificates for HTTP site systems** → Click **OK**.
2. **Enable Active Directory System Discovery:**
   - Go to **Administration → Hierarchy Configuration → Discovery Methods → Active Directory System Discovery → Properties**:
     - Check **Enable Active Directory System Discovery**.
     - Click the starburst icon and add `LDAP://OU=Workstations,DC=lab,DC=local` (check *Recursive*).
     - On the **Options** tab, check **Only discover computers that have logged on to a domain in a given period of time** (90 days). *(Tip: This keeps `LAB-ORPHAN-AD` out of ConfigMgr so it cleanly tests the "Active Directory Only" slice in OpenRCT's AD Cleanup Tool!)*
     - Click **OK** and choose **Yes** to run discovery now.
3. **Configure Client Push Installation:**
   - Go to **Administration → Site Configuration → Sites → `LAB` → Client Installation Settings → Client Push Installation**:
     - **General** tab: Check **Enable automatic site-wide client push installation** (Workstations).
     - **Accounts** tab: Add **`LAB\cm_push`** with its lab password → Click **OK**.
4. **Add Software Update Point (SUP) Role (for OpenRCT Software Updates Dashboard):**
   - Go to **Administration → Site Configuration → Servers and Site System Roles → `LAB-CM01.lab.local` → Add Site System Roles**:
     - Select **Software update point**.
     - **WSUS Port Settings:** HTTP `8530`, HTTPS `8531`.
     - **Sync Source:** Synchronize from Microsoft Update.
     - **Sync Schedule:** Enable synchronization (Daily).
     - **Classifications:** Check **only** `Critical Updates` and `Security Updates` *(keeps WSUS fast and small!)*.
     - **Products:** Uncheck everything, then check **only** `Windows 11` (and optionally `Microsoft Defender Antivirus`).
     - **Languages:** Check **English** only → Finish wizard.
   - Wait ~10 minutes, then go to **Software Library → Software Updates → All Software Updates → Synchronize Software Updates** (monitor `E:\Program Files\Microsoft Configuration Manager\Logs\wsyncmgr.log`).
   - Once synced, select **2–3 recent Windows 11 updates → Create Software Update Group** named **`Lab-Win11-Updates`** → Right-click **`Lab-Win11-Updates` → Deploy** to **`All Lab Workstations`** (Deployment Package stored in `E:\Sources\Updates`, Available or Required).
5. **Create & Deploy Sample 7-Zip Application (for Remote Software Center, Cache Manager & Uninstall):**
   - Copy `C:\Install\7z-x64.msi` into `E:\Sources\Apps\7Zip\`.
   - Share `E:\Sources` as `\\LAB-CM01\Sources` (Read permission for `Everyone` or `Domain Computers`, Full Control for `LAB\cmadmin`).
   - In MECM Console: **Software Library → Application Management → Applications → Create Application**:
     - Type: Windows Installer (`.msi`), Location: `\\LAB-CM01\Sources\Apps\7Zip\7z-x64.msi`.
     - Finish wizard, then right-click **7-Zip → Distribute Content** → Add Distribution Point `LAB-CM01.lab.local`.
     - Right-click **7-Zip → Deploy** → Collection: **`All Lab Workstations`**, Purpose: **Available**.

### 2.11 Take Checkpoint #2 (Manual — Host)
In Hyper-V Manager, right-click **`LAB-CM01` → Checkpoint** and rename it **`CM-Clean-Before-OpenRCT`**.

---

## Phase 3: Test Workstations (`LAB-WS01` & `LAB-WS02`)

### 3.1 Install Windows 11 Enterprise on Both VMs (Manual)
1. Start **`LAB-WS01`** and **`LAB-WS02`** in Hyper-V Manager and install **Windows 11 Enterprise Evaluation** onto each 60 GB disk *(leave the 2 GB disk on `LAB-WS01` unallocated — script `05` formats and BitLocker-encrypts it automatically)*.
2. During Windows 11 OOBE:
   - Choose **Set up for work or school → Sign-in options → Domain join instead** (creates a temporary local admin account like `lablocal`).
   - *(If the "Domain join instead" option is hidden on your ISO build, press `Shift+F10`, run `OOBE\BYPASSNRO`, reboot, and pick "I don't have internet".)*

### 3.2 Copy Scripts (and optional Legacy LAPS MSI) into Workstations (Automated from Host)
From your **host**:
```powershell
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-WS01 -SourcePath "." -DestinationPath "C:\LabScripts"
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-WS02 -SourcePath "." -DestinationPath "C:\LabScripts"

# Optional: Copy Legacy LAPS MSI to LAB-WS02
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-WS02 -SourcePath "D:\LabMedia\LAPS.x64.msi" -DestinationPath "C:\Install\LAPS.x64.msi"
```

### 3.3 Join Domain (Automated inside Each Workstation)
- On **`LAB-WS01`** (elevated PowerShell):
  ```powershell
  Set-ExecutionPolicy RemoteSigned -Scope LocalMachine -Force
  C:\LabScripts\05-WS-Configure-Workstation.ps1 -Role Primary -Phase JoinDomain
  ```
- On **`LAB-WS02`** (elevated PowerShell):
  ```powershell
  Set-ExecutionPolicy RemoteSigned -Scope LocalMachine -Force
  C:\LabScripts\05-WS-Configure-Workstation.ps1 -Role Secondary -Phase JoinDomain
  ```

### 3.4 Scope Windows LAPS vs Legacy LAPS GPOs (Automated on `LAB-DC01`)
After both workstations reboot into `lab.local`, switch to **`LAB-DC01`** and run:
```powershell
C:\LabScripts\03-DC01-Configure-Domain-GPO-LAPS.ps1 -ApplyGpoSecurityFiltering
```

### 3.5 Configure Endpoint Features, BitLocker & LAPS (Automated inside Each Workstation)
Sign into each workstation as **`LAB\cmadmin`**:
- On **`LAB-WS01`** (elevated PowerShell):
  ```powershell
  C:\LabScripts\05-WS-Configure-Workstation.ps1 -Role Primary -Phase ConfigureEndpoint
  ```
- On **`LAB-WS02`** (elevated PowerShell):
  ```powershell
  C:\LabScripts\05-WS-Configure-Workstation.ps1 -Role Secondary -Phase ConfigureEndpoint -LegacyLapsMsiPath "C:\Install\LAPS.x64.msi"
  ```

### 3.6 Finalize Client Enrollment & Test User State (Manual)
1. On **`LAB-CM01`** (MECM Console):
   - Go to **Assets and Compliance → Devices**, wait for `LAB-WS01` and `LAB-WS02` to appear (or trigger **Active Directory System Discovery**).
   - Right-click `LAB-WS01` & `LAB-WS02` → **Install Client** (or wait for automatic Client Push). Verify `Client = Yes` after ~5 minutes.
   - Add `LAB-WS01` to **`Lab Maintenance Window Test`** (or re-run `04-CM01-Prepare-Prereqs.ps1 -Phase PostMecmObjects -SiteCode LAB`).
2. On **`LAB-WS01`**:
   - Open **Control Panel → Configuration Manager → Actions** and run **Machine Policy Retrieval & Evaluation Cycle**, **Hardware Inventory Cycle**, and **Software Updates Scan Cycle**.
   - Open **Software Center** and install **7-Zip** *(populates `C:\Windows\ccmcache` for Client Cache Manager and System Info → Applications)*.
   - Sign in **once** as **`LAB\testuser2`**, then **Sign out** *(leaves an unloaded profile to test System Info → User Profiles → Delete)*.
   - Sign in as **`LAB\testuser1`** and leave the desktop open *(tests `msg.exe` Send Notification and `SkipIfUserLoggedOn`)*.
3. **Take Checkpoint #3 (Manual — Host):**
   - In Hyper-V Manager, take a checkpoint named **`WS-Baseline`** on both **`LAB-WS01`** and **`LAB-WS02`**. *(Whenever you test destructive OpenRCT actions like Uninstall SCCM Client, Delete from AD, or Shutdown, revert `LAB-WS02` to `WS-Baseline` in seconds!)*

---

## Phase 4: Install OpenRCT & Verify Everything

### 4.1 Copy & Install OpenRCT on `LAB-CM01`
From your **host**, copy your built `OpenRCT.msi` (or portable ZIP) into `LAB-CM01`:
```powershell
.\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "path\to\OpenRCT.msi" -DestinationPath "C:\Install\OpenRCT.msi"
```
Inside **`LAB-CM01`** (as `LAB\cmadmin`, close the MECM Console first):
```powershell
msiexec /i C:\Install\OpenRCT.msi PSEXECPATH="C:\Tools\PsExec64.exe"
```
*(Or if using the portable ZIP, extract it and run `.\scripts\Install-OpenRCT.ps1 -PsExecPath "C:\Tools\PsExec64.exe" -Mode Assembly`.)*

### 4.2 Run the Automated Lab Health Check (on `LAB-CM01`)
```powershell
C:\LabScripts\06-CM01-Verify-LabHealth.ps1 -SiteCode LAB
```
Confirm every check reports **`PASS`** (any `WARN` tells you the exact item still pending, such as Hardware Inventory or WSUS sync).

### 4.3 Full OpenRCT Feature Test Checklist

Re-open the **Configuration Manager Console** on `LAB-CM01` as `LAB\cmadmin` and validate:

- [ ] **Remote Tools (10):**
  - *System Information* on `LAB-WS01`: verify all 10 tabs (Hardware shows vTPM 2.0; Applications loads via Remote Registry and silently uninstalls 7-Zip; Services restarts `Spooler`; User Profiles deletes unloaded `LAB\testuser2`; Local Group Members adds/removes `LAB\testuser1`).
  - *Remote Software Center* on `LAB-WS02`: verify 7-Zip (App), `OpenRCT Test Package` (Package), Windows 11 Updates, and Compliance Baselines; remotely trigger Install on 7-Zip.
  - *Remote Windows Security* on `LAB-WS01`: verify BitLocker, TPM, Firmware (Secure Boot / 2023 CA), VBS, Defender, Exclusions, Windows Firewall, and Firewall Rules.
  - *Remote Registry Editor* & *Remote Event Viewer* on `LAB-WS01`.
  - *Running Processes*: launch `notepad.exe` on `LAB-WS01`, terminate it from OpenRCT.
  - *Connect to `C$`*, *Interactive PowerShell Prompt* (WinRM), *Interactive Command Prompt* (PsExec64), and *Manage Computer*.
- [ ] **Client Actions (12):** Multi-select `LAB-WS01` + `LAB-WS02` → `OpenRCT → Client Actions` → test Machine Policy, Hardware Inventory, Software Updates Scan; also right-click `All Lab Workstations` collection and run a collection-wide Client Action.
- [ ] **Client Tools (13):**
  - *Client Information* on `LAB-WS01`: verify Details, Components, and Cache (delete cached 7-Zip item).
  - *Show Collections - Advanced* on `LAB-WS01`: verify `Lab Maintenance Window Test` shows the weekly Maintenance Window.
  - *Add Device(s) to Collection* & *Device Status Messages* on `LAB-WS01`.
  - *Open Client Log File Folder* / *Open Client Installation Log File Folder* / *Restart SMS Agent Host Service* / *Run Client Check* / *Refresh Updates Compliance State* / *Rerun Deployment*.
  - *Stop ConfigMgr Client Activities*, *Repair Client*, and *Uninstall SCCM Client* on `LAB-WS02` → revert `LAB-WS02` to `WS-Baseline` afterward.
- [ ] **Device and Power Tools (6):**
  - *Ping System(s)*, *Group Policy Update*, and *Send Notification* to `LAB-WS01` (popup appears on `LAB\testuser1` desktop).
  - *Wake On LAN*: verify magic packet succeeds using inventoried MAC *(optional: capture UDP 7/9 on `LAB-WS01` with `pktmon`)*.
  - *Schedule Restart or Shutdown* (60s) → *Cancel Pending Restart or Shutdown*.
- [ ] **Security Tools (5):**
  - *View LAPS Password*: `LAB-WS01` returns **Windows LAPS** password (or **`Windows LAPS (Encrypted)`** / **`Encrypted`** if encryption is enabled); `LAB-WS02` returns **Legacy Microsoft LAPS** password.
  - *View BitLocker Recovery Key*: `LAB-WS01` returns the 48-digit AD recovery key for `D:`.
  - *Windows Defender*: run Quick Scan, Full Scan, and Update Definitions (via outbound WinNAT).
- [ ] **Active Directory and ConfigMgr (5):**
  - *AD Security Groups*: add/remove `LAB-WS01` to `SG-Lab-TestGroup1`.
  - *Disable/Enable/Delete Device(s) in Active Directory* & *Delete Device(s) From ConfigMgr* on `LAB-WS02` → revert `WS-Baseline`.
- [ ] **Embedded Console Dashboards (*Assets and Compliance → Overview → OpenRCT* — 6 Dashboards):**
  - **Active Directory Cleanup Tool:** Select `OU=Workstations` vs `All Lab Workstations` → verify `Both` (`LAB-WS01`, `LAB-WS02`) and `Active Directory` (`LAB-ORPHAN-AD`).
  - **BitLocker Compliance:** Select `OU=Workstations` → verify `BitLocker Recovery Key Storage` and `Unified Compliance` charts for `LAB-WS01`.
  - **Content Distribution Monitor:** Verify `LAB-CM01.lab.local` DP bar chart, properties panel, Content Status dialog, and Redistribute/Validate actions.
  - **Hardware and Firmware Audit:** Select `All Lab Workstations` → verify `Select Charts` and `Nested Filtering` across `Chassis`, `Manufacturer`, `Model`, `UEFI Status`, `Secure Boot Status`, `TPM Status`, `BIOS Version`, and `OS Version`.
  - **LAPS Dashboard:** Select `OU=Workstations` → verify pie charts split across `Computers with LAPS Password` (2) / `Computers without LAPS Password` (1) and `Computers with Windows Built-in LAPS` / `No LAPS Client Installed`.
  - **Software Updates Deployment Status:** Select `All Lab Workstations` → verify compliance charts, SUG filter (`Lab-Win11-Updates`), deployed-only toggle, and CSV export.

---

## Troubleshooting & Lab Teardown

- **MECM Console cannot connect / `smsprov.log` shows `Unknown SQL Error! Failed to connect to the SQL Server, connection type: SMS ACCESS` (and `SMS_EXECUTIVE` is missing):** Caused by **Microsoft ODBC Driver 18 version `18.7.1.1` (or `18.6.1.1`)**, which has a known Microsoft bug on ConfigMgr 2603 and earlier. Uninstall ODBC Driver 18 in *Apps & Features*, install the validated **`18.4.1.1`** redistributable already inside `E:\CMPrereqs\msodbcsql.msi` (or download **`18.6.2.1`**), set `ALTER DATABASE CM_LAB SET COMPATIBILITY_LEVEL = 150;` if using SQL Server 2025, and reboot `LAB-CM01`.
- **WinNAT conflict on host (`New-NetNat` fails):** Windows Hyper-V only permits one WinNAT instance at a time. Check existing NATs with `Get-NetNat`. If Docker/WSL/another lab uses WinNAT, either remove the old unused NAT or adjust the lab subnet to match your existing NAT prefix.
- **`msg.exe` returns Access Denied (Error 5):** Confirm `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\AllowRemoteRPC` is `1` on the target workstation and a user session is actively logged on.
- **`View LAPS Password` shows "None" on `LAB-WS01`:** Verify `ADPasswordEncryptionEnabled` is `0` under `HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\LAPS`, run `Invoke-LapsPolicyProcessing`, and check Event Viewer → `Microsoft-Windows-LAPS/Operational` (Event ID `10018`).
- **WSUS Application Pool crashes (503 Service Unavailable):** In IIS Manager on `LAB-CM01` → Application Pools → `WsusPool` → Advanced Settings → set **Private Memory Limit (KB)** to `0` (unlimited) or `4194304` and recycle the pool.
- **Complete Lab Teardown (Host):**
  ```powershell
  .\01-Host-Setup-HyperVLab.ps1 -LabRoot "D:\HyperV\OpenRCT-Lab" -RemoveLab
  ```
