# SokiSoko ERP Bridge (Windows agent)

An on-prem Windows service that mirrors ERP master data into SokiSoko — **SAP Business
One** or **Odoo**, picked in its setup console. Use it when the ERP sits behind the
customer's firewall and the SokiSoko server cannot reach it directly. (For ERPNext, use
the [`erpnext/sokisoko_sync`](../erpnext/sokisoko_sync) Frappe app instead — it pushes
from inside ERPNext.)

```
ERP (B1 Service Layer / Odoo JSON-RPC) ──(LAN)──▶ ERP Bridge ──(HTTPS, API key)──▶ SokiSoko
   Items / partners / prices / stock                 polls every N min            /admin/erp/connections/{id}/ingest
```

The bridge normalizes records exactly like the server-side `sap_b1` / `odoo` providers
and pushes them to the ingest endpoint, which applies them through the same funnel as
the built-in poller — products, stock, prices and customers land identically either way.

**Direction:** ERP → SokiSoko only. Outbound (web orders → ERP) stays on the server.

## What it syncs

| Entity | SAP Business One | Odoo |
|---|---|---|
| Products | `Items` (delta on UpdateDate+UpdateTime) | `product.product` where `sale_ok` (delta on write_date) |
| Customers | `BusinessPartners` (cCustomer) | `res.partner` where `customer_rank > 0` |
| Prices | base price list + `SpecialPrices` | `list_price` per product |
| Stock | `ItemWarehouseInfoCollection` per warehouse | `qty_available` per warehouse |

First run is a full backfill; after that, deltas every interval (default 5 min). Stock
is a full scan per run on both ERPs (no reliable stock delta); the server skips
unchanged rows.

## Installation

### 1. Get the exe

Download `SokiSoko.SapBridge.exe` from the release, or build it (any machine with the
.NET 8 SDK):

```powershell
cd agent
dotnet publish SokiSoko.SapBridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish\win-x64
```

The single-file exe is self-contained — **no .NET runtime needed** on the target machine.

### 2. Prepare SokiSoko (admin console)

1. **Settings → ERP sync** → create a connection for your ERP (*SAP Business One* or
   *Odoo*) and note its **connection id**. Leave the ERP credentials blank — the bridge
   holds them.
2. **Settings → API keys** → new key with the **`erp.manage`** scope. Copy the `tgk_…`
   key (shown once).
3. Optional: **Warehouses** on the connection — map ERP warehouse codes to your
   warehouses (unmapped codes fall back to the default warehouse).

### 3. Install on the Windows machine

On a machine **on the same network as the ERP** (Windows Server 2019+ / Windows 10+):

```powershell
mkdir C:\sokisoko-bridge
# copy SokiSoko.SapBridge.exe into C:\sokisoko-bridge

# smoke-test in a console first
cd C:\sokisoko-bridge
.\SokiSoko.SapBridge.exe
start http://127.0.0.1:8735
```

### 4. Configure via the setup console

In the browser at `http://127.0.0.1:8735`:

1. **ERP system** — pick *SAP Business One* or *Odoo*.
2. Fill the ERP section:
   - **B1**: Service Layer URL (`https://b1-host:50000`), company DB, integration user +
     password, base price list #, default currency. *Untrusted cert* = yes only for
     self-signed B1 certs.
   - **Odoo**: Odoo URL (`https://mycompany.odoo.com`), database, username, password or
     **API key** (recommended: Odoo → Preferences → Account Security → API keys),
     default currency, warehouse code (blank = all).
3. Press **Test ERP connection** — expect a green OK. The error messages tell you what
   to fix (wrong DB, missing license, firewall, …).
4. **SokiSoko section** — your SokiSoko URL, the `tgk_…` API key, the connection id.
   Press **Test SokiSoko connection**.
5. **Save settings** — the next sync run picks them up; no restart needed.

Secrets are **DPAPI-sealed** into `settings.json` next to the exe — readable only by the
Windows account that saved them, never stored in plaintext.

### 5. Run as a Windows service

Stop the console test (Ctrl+C), then in an **elevated** PowerShell:

```powershell
sc.exe create "SokiSokoSapBridge" binPath= "C:\sokisoko-bridge\SokiSoko.SapBridge.exe" start= auto
sc.exe description "SokiSokoSapBridge" "Mirrors ERP master data into SokiSoko"
sc.exe start "SokiSokoSapBridge"
```

**Important:** DPAPI secrets are sealed per Windows account. The service runs as
`LocalSystem` by default, so either re-save settings via the console **after** the
service starts (the console is then served by the service account), or run the service
as a dedicated account:

```powershell
sc.exe config "SokiSokoSapBridge" obj= ".\sapbridge" password= "<password>"
```

### 6. Verify

- The console status card shows `last run ok — N records` within one interval.
- SokiSoko → ERP sync → sync logs show inbound `agent` entries.
- Products/customers/stock/prices appear in SokiSoko.

## Operating

- **Console**: `http://127.0.0.1:8735` — status, last run, recent errors. Localhost
  only by design (no auth); reach it via RDP or
  `ssh -L 8735:127.0.0.1:8735 user@host`. **Do not** expose it through a reverse proxy
  or tunnel.
- **Health endpoint**: `GET /api/health` — JSON status for monitors (Uptime Kuma etc.).
- **Logs**: `logs\bridge-YYYYMMDD.log` next to the exe (14 days retained) plus the
  Windows Event Log.
- **Cursors**: `cursors.json` next to the exe — per-entity high-water marks. Delete it
  to force a full re-backfill.
- **Updating**: stop the service, replace the exe, start it. Settings and cursors
  carry over.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Test B1: 401 | Wrong company DB / user / password, or the user lacks a Service Layer license |
| Test B1: certificate error | Self-signed B1 cert — set *Untrusted cert* to yes |
| Test Odoo: rejected login | Wrong database name; use an API key, not the account password |
| Test SokiSoko: 401 | API key typo — keys are shown once; create a fresh one |
| Test SokiSoko: 403 | Key lacks the `erp.manage` scope |
| Test SokiSoko: 404 | Wrong connection id (Settings → ERP sync) |
| Test SokiSoko: 502/503 | The SokiSoko API behind the gateway is down |
| Service runs but no sync | Settings were saved as a different Windows account — re-save via the console running under the service account |

## Tests

```powershell
cd agent
dotnet test
```
