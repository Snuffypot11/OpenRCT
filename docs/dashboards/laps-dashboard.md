# LAPS Dashboard

Track local administrator password management coverage across **both Legacy Microsoft LAPS and built-in Windows LAPS** without requiring any RSAT PowerShell modules.

![LAPS Dashboard](../assets/images/dashboard-laps.png)

---

## How to Use

1. Select an **Active Directory OU** (with optional **Include Sub-OUs**) or a **ConfigMgr Device Collection** and click **Scan**.
2. Inspect the two interactive pie charts:
   - **LAPS Password Coverage:** `Has LAPS Password` vs `No Password` (plus expired password visibility).
   - **LAPS Client / Type Breakdown:** `Windows Built-in LAPS`, `Legacy LAPS Installed`, `Not Installed`.
3. Click any pie slice to filter the 8-column results table (`Device Name`, `AD OU`, `LAPS Type`, `Has Password`, `Password Expiration`, `Client Installed`, `OS`, `Last Logon`).

---

## Row Actions

Right-click any device row to:

- **View LAPS Password** (masked reveal + copy with automatic clipboard clear)
- Trigger **Group Policy Update** (`gpupdate /force`) or **Hardware Inventory Cycle**
- Open **System Information** or **Export CSV**.
