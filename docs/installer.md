# Installer

PBI Port Wrapper ships as a Windows MSI (`PBIPortWrapper.msi`) in addition to the
portable ZIP. The installer:

- copies the application to `C:\Program Files (x86)\PBI Port Wrapper`,
- adds a **PBI Port Wrapper** entry to the Start Menu, and
- registers the app as a **Power BI Desktop External Tool** (it appears on the
  External Tools ribbon automatically — no manual JSON copying).

Configuration and logs are written to `%APPDATA%\PBIPortWrapper\`, so the app
never needs write access to its install folder.

> The MSI is **not code-signed**. Windows SmartScreen and Defender will warn on
> first run — see [Windows SmartScreen / Defender](#windows-smartscreen--defender).

---

## Installing (interactive)

1. Download `PBIPortWrapper.msi` from the release.
2. Double-click it. Because the package is unsigned, SmartScreen may show
   *"Windows protected your PC"* — click **More info → Run anyway** (see below).
3. Approve the User Account Control (elevation) prompt — a per-machine install
   into Program Files requires administrator rights.
4. Accept the licence and click **Install**.
5. Launch from the Start Menu, or from Power BI Desktop's **External Tools** tab.

To remove it: **Settings → Apps → Installed apps → PBI Port Wrapper → Uninstall**
(or `msiexec /x PBIPortWrapper.msi`). Uninstall removes the executable, the Start
Menu entry, and the External Tool registration. Your `%APPDATA%\PBIPortWrapper\`
configuration and logs are left in place.

## Installing (silent / unattended)

The MSI supports standard `msiexec` switches for scripted or enterprise
deployment. Run these from an **elevated** shell (the install is per-machine):

```bat
:: Silent install, no UI
msiexec /i PBIPortWrapper.msi /qn

:: Silent, no reboot, with a verbose log
msiexec /i PBIPortWrapper.msi /quiet /norestart /l*v install.log

:: Silent uninstall
msiexec /x PBIPortWrapper.msi /qn
```

Notes:

- **Do not override `INSTALLFOLDER`.** The External Tool manifest records an
  absolute path to the executable and expects the default location
  (`C:\Program Files (x86)\PBI Port Wrapper`). Installing elsewhere leaves the
  External Tools ribbon entry pointing at a path that does not exist.
- Group Policy / SCCM / Intune can deploy the MSI as-is. MST transforms and a
  dedicated enterprise configuration surface are out of scope for now
  (see issue #36).

---

## Building the installer

### Prerequisites

- **.NET SDK 8 or newer** (`dotnet` on `PATH`). The app targets `net8.0-windows`.
- **Internet access on first build** — the WiX Toolset (`WixToolset.Sdk` and the
  UI extension) is restored automatically from NuGet by
  `Installer/PBIPortWrapper.Installer.wixproj`. No separate WiX install is needed.

### Build

```powershell
powershell -File Installer\build_installer.ps1
```

The script:

1. publishes the app as a **single-file, self-contained win-x64** build into
   `Installer\Publish\` (the same convention the release ZIP uses), then
2. builds the MSI via the WiX SDK and stages it to
   `Installer\Output\PBIPortWrapper.msi`.

The MSI's `ProductVersion` is bound automatically from the executable's
`FileVersion`, so bumping `<Version>` in `PBIPortWrapper.csproj` is enough — there
is no version to maintain separately in the WiX sources.

### Layout

| File | Purpose |
|------|---------|
| `Installer/Package.wxs` | The WiX package: components, directories, shortcut, External Tool registration, UI. |
| `Installer/PBIPortWrapper.Installer.wixproj` | WiX SDK project (restores WiX + UI extension). |
| `Installer/build_installer.ps1` | Publish + build orchestration. |
| `Installer/License.rtf` | Licence shown in the installer UI. |

The package is intentionally built as **x86** (the WiX default). On 64-bit
Windows an x86 package resolves the Program Files and Common Files locations to
their `(x86)` paths — and the x86 Common Files path is exactly where Power BI
Desktop looks for External Tools manifests, on every edition. The 64-bit
application runs correctly from that location.

---

## Troubleshooting

### Windows SmartScreen / Defender

The installer and executable are **not code-signed**, so on first run Windows may
show *"Windows protected your PC"* (SmartScreen) or a Defender prompt. This is
expected for an unsigned app and does not indicate malware.

- SmartScreen dialog: click **More info**, then **Run anyway**.
- To verify what you downloaded, check the file against the release's published
  hash/size before running.

Code signing (an Authenticode certificate) would remove these prompts but is not
currently in place (see issue #35).

### The External Tools ribbon entry does not appear

1. Fully restart Power BI Desktop after installing — it enumerates External Tools
   only at startup.
2. Confirm the manifest exists at
   `C:\Program Files (x86)\Common Files\Microsoft Shared\Power BI Desktop\External Tools\pbiportwrapper.pbitool.json`.
3. Confirm the `path` inside that JSON points at the installed executable
   (`C:\Program Files (x86)\PBI Port Wrapper\PBIPortWrapper.exe`). A mismatch
   usually means the app was installed to a non-default folder — reinstall to the
   default location.

### Where are my settings / logs?

Both live under `%APPDATA%\PBIPortWrapper\` (`config.json` and `log.txt`), not in
the install folder. They survive uninstall and reinstall.
