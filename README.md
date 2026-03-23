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
| Recenter VR | F12 |

## Requirements

- [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) for Unity Mono (x64)
- SteamVR installed over Steam
- .NET SDK 6.0+ (for building from source)

## Installation (prebuilt)

1. Install BepInEx 5 into your Ravenfield folder (`<Steam>/steamapps/common/Ravenfield/`).
2. Copy `RavenfieldVRMod.dll` into `BepInEx/plugins/`.
3. Launch Ravenfield with SteamVR running.

## Building from source

The project expects to live inside the Ravenfield game folder so it can reference game and BepInEx assemblies via relative paths.

```
steamapps/common/Ravenfield/
├── BepInEx/
├── ravenfield_Data/
│   └── Managed/          ← Unity + game DLLs
└── RavenfieldVRMod/      ← this repo
    └── RavenfieldVRMod.csproj
```

1. Clone this repo into your Ravenfield install directory:
   ```
   cd "<Steam>/steamapps/common/Ravenfield"
   git clone https://github.com/GDani31/ravenfield-vr-mod.git RavenfieldVRMod
   ```

2. Build:
   ```
   cd RavenfieldVRMod
   dotnet build -c Release
   ```

   The post-build step automatically copies the DLL to `BepInEx/plugins/`.

3. Launch Ravenfield.

## License

MIT
