# Third-party notices

PBIRelay is MIT-licensed (see [LICENSE.txt](LICENSE.txt)). **That licence
covers this project's own source only.** The released `.zip` and `.msi` are built
as a self-contained, single-file publish, so they also carry third-party components
that are licensed separately and are *not* MIT.

## Microsoft Analysis Services client libraries

| Component | Version | Licence |
|---|---|---|
| `Microsoft.AnalysisServices.NetCore.retail.amd64` (AMO) | 19.79.1.1 | Microsoft licence terms — https://go.microsoft.com/fwlink/?linkid=852989 |
| `Microsoft.AnalysisServices.AdomdClient.NetCore.retail.amd64` (ADOMD.NET) | 19.79.1.1 | Microsoft licence terms — https://go.microsoft.com/fwlink/?linkid=852989 |

© Microsoft Corporation. All rights reserved. These are proprietary Microsoft
libraries redistributed under Microsoft's terms, which permit distribution as part
of an application that uses them to connect to Analysis Services. They are not
covered by this project's MIT licence, and nothing here grants rights in them.

The app uses them for exactly that purpose: AMO connects to the local Analysis
Services engine that Power BI Desktop runs, relays XMLA to it, and renames a
database while it is served.

## .NET runtime and ASP.NET Core

The self-contained build embeds the .NET 8 runtime and ASP.NET Core shared
framework (Kestrel hosts the XMLA endpoint). MIT — © Microsoft Corporation.
https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

## Other packages

| Component | Version | Licence |
|---|---|---|
| `Newtonsoft.Json` | 13.0.4 | MIT — © James Newton-King |
| `System.Management` | 10.0.0 | MIT — © Microsoft Corporation |
| `Microsoft.Identity.Client` (transitive, via AMO) | 4.56.0 | MIT — © Microsoft Corporation |

## Not affiliated with Microsoft

Power BI, Power BI Desktop and Analysis Services are trademarks of Microsoft
Corporation. This is an unofficial tool, not affiliated with, endorsed by, or
supported by Microsoft. See the disclaimer in [README.md](README.md).
