# FlowRunFinderV2

Standalone .NET 8 Avalonia app for browsing Power Automate flow runs from a Dataverse/Dynamics 365 environment.

## First slice

- Paste a Dataverse environment URL.
- Authenticate with device-code login.
- Cache the MSAL token locally under `%LOCALAPPDATA%\FlowRunFinderV2`.
- Remember the last environment URL in `%LOCALAPPDATA%\FlowRunFinderV2\settings.json`.
- Load cloud flows from the `workflow` table.
- Pick a flow and load the latest 50 runs from the Power Platform environment API.
- Authenticate separately to Power Automate when needed.
- Read trigger payloads from the run response and add dynamic trigger columns to the grid.

## What I need from you

To run it, you will need:

- .NET 8 SDK.
- Network access to NuGet.
- Access to the target Dataverse environment.
- Access to Power Automate for the same environment.
- A public-client Azure app registration that allows delegated Dataverse access, or use the default client id currently in `DataverseAuthService`.

Run:

```powershell
dotnet restore .\src\FlowRunFinderV2\FlowRunFinderV2.sln
dotnet run --project .\src\FlowRunFinderV2\FlowRunFinderV2\FlowRunFinderV2.csproj
```

If your tenant blocks the default public client id, create an app registration with public client/device-code support and replace `DefaultClientId` in `src/FlowRunFinderV2/FlowRunFinderV2/Services/DataverseAuthService.cs`.

Power Automate tokens are cached separately in `%LOCALAPPDATA%\FlowRunFinderV2\power_automate_msal_cache.bin3`.
