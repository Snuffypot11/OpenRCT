# Software Updates Deployment Status Dashboard

Analyze patch compliance across any **ConfigMgr Device Collection** with flexible filtering by deployment status, Software Update Group, update age, and classification.

![Software Updates Deployment Status Dashboard](../assets/images/dashboard-software-updates.png)

---

## Filters

- **Device Collection:** Target any ConfigMgr collection.
- **Deployed Only Toggle:** Focus strictly on updates deployed to the collection or include all required/missing updates.
- **Software Update Group (SUG) Filter:** Filter compliance calculations to one or more specific SUGs.
- **Age Slider:** Filter out brand-new updates (e.g., show missing updates older than 7, 14, or 30 days).
- **6 Update Classification Checkboxes:** `Security Updates`, `Critical Updates`, `Update Rollups`, `Updates`, `Feature Packs`, `Service Packs`.

---

## Interactive Charts & Remediation

- **Device Compliance Status:** `Compliant`, `Non-Compliant`, `Unknown`.
- **Missing Updates by Category:** Visual breakdown of missing updates by classification.

Click any chart slice to filter the device grid, then right-click rows to:

- Open **Remote Software Center** to inspect and install missing updates immediately
- Trigger **Software Updates Scan Cycle**, **Software Updates Deployment Evaluation Cycle**, or **Refresh Updates Compliance State**
- **Schedule Restart** for devices pending reboot
- **Export CSV** for compliance reporting.
