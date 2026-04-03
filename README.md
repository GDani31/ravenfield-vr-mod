# Ravenfield VR Mod

A Mod that adds VR support for Ravenfield.
Should work with any SteamVR/OpenVR compatible vr headset.
i made and tested the mod wiht a quest 3.

## Features

- Full head tracking with 6DOF
- Tracked VR controllers with laser pointers
- Two-handed Pavlov-style weapon grip (hold left grip)
- Vehicle controls (car/heli/plane) via analog sticks and triggers
- VR-adapted UI: menus as floating WorldSpace panels, HUD as head-locked overlay
- Laser pointer interaction for all menus and map selection
- Snap turning (right thumbstick)
- Player scale adjustment to match game character height

## Controls

| Action | Binding |
|---|---|
| Fire | Right Trigger |
| Reload | Right B |
| Use / Enter Vehicle | Right A |
| Open Loadout | Left A |
| Jump | Left Trigger |
| Sprint | Right Grip |
| Two-handed grip | Left Grip |
| Next Weapon | Left B |
| Snap Turn | Right Stick Left/Right |
| Move | Left Stick |
| Pause Menu | Left Stick Click |
| Recenter VR | F11 / Joystick Click (both sticks) |
| Pick up magazine / shell / rocket (manual reload) | Offhand Trigger |

On Left Hand Mode all control bindings are mirrored between left and right hands.

### Manual Reloading

There is a setting to enable manual reloading. When enabled, you physically pick up magazines, shells, and rockets with the offhand trigger to reload your weapon. Some workshop weapons may have problems with manual reloading and will default back to automatic reloading.

## Requirements

- [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) for Unity Mono (x64)
- SteamVR installed over Steam
- .NET SDK 6.0+ (for building from source)

## Installation (prebuilt)

1. Install BepInEx 5 into your Ravenfield folder (`<Steam>/steamapps/common/Ravenfield/`).
2. Download all files from the [Releases](https://github.com/GDani31/ravenfield-vr-mod/releases) tab and place them:
   - `RavenfieldVRMod.dll` → `BepInEx/plugins/`
   - `actions.json` → `BepInEx/plugins/`
   - `bindings_oculus_touch.json` → `BepInEx/plugins/`
   - `bindings_knuckles.json` → `BepInEx/plugins/`
   - `bindings_vive_controller.json` → `BepInEx/plugins/`
   - `Unity.XR.Management.dll` → `ravenfield_Data/Managed/`
   - `Unity.XR.OpenVR.dll` → `ravenfield_Data/Managed/`
   - `XRSDKOpenVR.dll` → `ravenfield_Data/Plugins/x86_64/`
   - `openvr_api.dll` → `ravenfield_Data/Plugins/x86_64/`
   - `UnitySubsystemsManifest.json` → `ravenfield_Data/UnitySubsystems/XRSDKOpenVR/` (create the folders if they don't exist)
3. Launch Ravenfield with SteamVR running.

## Building from source

The project expects to live inside the Ravenfield game folder so it can reference game and BepInEx assemblies via relative paths.

```
steamapps/common/Ravenfield/
├── BepInEx/
├── ravenfield_Data/
│   └── Managed/          ← Unity + game DLLs
└── ravenfield-vr-mod/    ← this repo
    └── RavenfieldVRMod.csproj
```

1. Clone this repo into your Ravenfield install directory:
   ```
   cd "<Steam>/steamapps/common/Ravenfield"
   git clone https://github.com/GDani31/ravenfield-vr-mod.git
   ```

2. Build:
   ```
   cd ravenfield-vr-mod
   dotnet build -c Release
   ```

   The first build automatically downloads and compiles the required VR dependencies (Unity XR Management, OpenVR XR Plugin). Subsequent builds skip this step.

   The post-build step automatically copies the DLL to `BepInEx/plugins/`.

3. Launch Ravenfield.

## Video tutorial

https://youtu.be/OqkZFleVvqk
