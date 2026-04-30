# SimpleVRCAScreener
Screenshots your VRCA files.
This program also checks for crashes, finds the file causing it and moves it to a different folder prevent future crashes.
# Requirements
- [.NET](https://dotnet.microsoft.com/download)
- [AssetViewer](https://raw.githubusercontent.com/Dean2k/SARS/32f09ffccd912554b15c458e333b99020d0d408a/SARS/Assets/NewestViewer.zip)
# Setup
- Install dotnet if not already done (check if dotnet is installed by opening your command prompt and typing `dotnet --version`).
- Install AssetViewer if not already done and extract the content of the zip in the Viewer folder.
- Download the SimpleVRCAScreener code and extract it.
# Using the Script
- Place your VRCAs in the VRCA folder.
- Open start.bat.

## Extra: Seleting a custom VRCA Folder
- Open the SimpleVRCAScreener folder.
- Right click in an empty space and click "Open in Terminal".
- Edit and Run this command:
```dotnet run -- --PathOverride="C:\Your\Custom\Path"```
Note: If a file gets moved to the Error folder, it means that AssetViewer can't open or handle it. I would recommand using [AssetRipper](https://github.com/AssetRipper/AssetRipper) to directly extract the data of the file.

This is my first ever csharp and public code, I do not know much about coding so I'm always looking for tips and fixes!

> AssetViewer is a program from Dean2k's [SARS](https://github.com/Dean2k/SARS) Public Archive, the download link directly redirects here. I do not host nor edit the code in any way, shape or form.
