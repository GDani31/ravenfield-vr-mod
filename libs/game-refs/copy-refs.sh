#!/bin/bash
# Copies the required reference assemblies from a local Ravenfield install
# into this directory for CI builds.
#
# Usage: Run from the ravenfield-vr-mod directory:
#   bash libs/game-refs/copy-refs.sh
#
# These DLLs are only used as compile-time references (not redistributed).

MANAGED="../ravenfield_Data/Managed"
DEST="libs/game-refs"

DLLS=(
    UnityEngine.dll
    UnityEngine.CoreModule.dll
    UnityEngine.IMGUIModule.dll
    UnityEngine.UIModule.dll
    UnityEngine.UI.dll
    UnityEngine.VRModule.dll
    UnityEngine.XRModule.dll
    UnityEngine.InputLegacyModule.dll
    UnityEngine.PhysicsModule.dll
    UnityEngine.AnimationModule.dll
    UnityEngine.TextRenderingModule.dll
    UnityEngine.ImageConversionModule.dll
    UnityEngine.SubsystemsModule.dll
    Assembly-CSharp.dll
    Assembly-CSharp-firstpass.dll
)

for dll in "${DLLS[@]}"; do
    if [ -f "$MANAGED/$dll" ]; then
        cp "$MANAGED/$dll" "$DEST/$dll"
        echo "Copied $dll"
    else
        echo "WARNING: $MANAGED/$dll not found"
    fi
done

echo "Done. Commit the DLLs in $DEST/ for CI builds."
