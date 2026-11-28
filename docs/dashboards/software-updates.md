# Software Updates Deployment Status Dashboard

Analyze patch compliance across any **ConfigMgr Device Collection** with flexible filtering by deployment status, Software Update Group, update age, and classification.

!!! info "Data Source"
    This dashboard queries **MECM directly via the SMS Provider** (`SMS_SoftwareUpdate`, `SMS_AuthorizationList`, `SMS_CIRelation`, and batched collection-scoped `SMS_UpdateComplianceStatus` queries for Required/Installed states) — it does **not** connect to individual client devices during the scan.

![Software Updates Deployment Status Dashboard](../assets/images/dashboard-software-updates.png)

---

## Filters

- **Device Collection:** Target any ConfigMgr collection.
- **Limit to Deployed Updates *(Checked by default)*:** Pushes `IsDeployed = 1` filtering down to MECM SQL Server for fast production scans; uncheck and click **Start** to query the full non-expired catalog.
- **Software Update Group (SUG) Filter:** Filter compliance calculations to one or more specific SUGs.
- **Age Slider (`0`–`180` days, default `0`):** Filters update records by `DatePosted` to exclude brand-new releases (e.g., evaluate updates older than 7, 14, or 30 days).
- **6 Update Classification Checkboxes:** `Critical Updates`, `Definition Updates`, `Other Classifications`, `Security Updates`, `Update Rollups`, `Updates`.

---

## Interactive Charts, Remediation & Export

- **Device Compliance Status:** `Compliant`, `NonCompliant`, `Unknown`.
- **Missing Updates By Category:** Visual breakdown of missing updates by category (`Security`, `Critical`, `Update Rollups`, `Definition`, `Other`).

Click any chart slice to filter the device grid, then right-click rows to:

- **Trigger Software Updates Scan Cycle**
- **Trigger Software Updates Deployment Evaluation**
- **Open Remote Software Center** to inspect and install missing updates immediately

Click **Export to CSV** in the bottom bar for compliance reporting.
