# Unity Asset Sharing (Large Files Kept Out of Git)

This repository intentionally excludes large binary assets (for example: `.fbx`, `.glb`, `.exr`, `.zip`) so pushes to GitHub stay reliable.

## What is in GitHub

- Unity project setup and dependencies (`Packages`, `ProjectSettings`)
- Scripts (`Assets/Scripts`)
- Scenes (`Assets/Scenes`)
- Settings and editor scripts (`Assets/Settings`, `Assets/Editor`)

## What is shared separately

Large art/model/HDR assets are shared outside GitHub (Drive/Dropbox/OneDrive/USB).

## Teammate setup

1. Clone this repository.
2. Download the external asset pack from your team share.
3. Copy the provided assets into the expected Unity `Assets/...` paths.
4. Open `Unity/AIMAP` in Unity.
5. If materials show missing references, reimport the copied asset folders.

## Suggested team process

- Keep source code and project settings in GitHub.
- Keep large binaries in your shared drive asset pack.
- When replacing assets, update the shared pack and notify teammates.
