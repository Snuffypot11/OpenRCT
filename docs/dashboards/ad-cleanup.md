# Active Directory Cleanup Tool

Reconcile **Active Directory** Organizational Units against **Configuration Manager** Device Collections to spot stale, decommissioned, or unmanaged devices at a glance.

![Active Directory Cleanup Tool Dashboard](../assets/images/dashboard-ad-cleanup.png)

---

## How to Use

1. Select an **Active Directory OU** (optionally check **Include Sub-OUs**) and a **ConfigMgr Device Collection** (e.g., `All Systems`).
2. Click **Scan** to reconcile computer accounts from both sources.
3. Click any slice of the interactive pie chart to filter the 15-column results table:
   - **Both:** Device exists in both Active Directory and Configuration Manager.
   - **Active Directory Only:** Computer object exists in AD but has no ConfigMgr record (often stale or missing the MECM client).
   - **ConfigMgr Only:** Resource exists in ConfigMgr but has no matching AD computer object in the selected OU.

---

## Built-In Cleanup Actions & Export

Right-click one or more rows in the grid to:

- **Disable in Active Directory** / **Enable in Active Directory**
- **Delete from Active Directory** *(with confirmation)*
- **Delete from ConfigMgr** *(with confirmation)*
- **Add to Collection**, **Ping Systems**, or open **System Information**
- Click **Export CSV** to save the reconciliation report for change-control review.
