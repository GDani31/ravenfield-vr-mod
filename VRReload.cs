using System.Reflection;
using UnityEngine;
using Valve.VR;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Gesture-based VR reloading using actual weapon magazine meshes.
    ///
    /// Flow:
    ///   1. Press B (dominant) → mag hides on weapon, ammo drops to 0/1, clone drops
    ///   2. Off-hand trigger → new mag clone appears in hand
    ///   3. Hold trigger → mag follows off-hand
    ///   4. Release near dominant hand → mag reattaches, ammo refilled, NO animation
    ///   5. Release elsewhere → mag drops, grab again
    /// </summary>
    public class VRReload : MonoBehaviour
    {
        public static VRReload Instance { get; private set; }

        public enum ReloadState
        {
            Idle,
            MagEjected,     // Mag hidden on weapon, ammo at 0/1
            Holding,        // Mag in hand (trigger held)
            Cooldown        // Brief cooldown after insert
        }

        public ReloadState State { get; private set; } = ReloadState.Idle;

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("vr_gesture_reload", 0) == 1;
            set { PlayerPrefs.SetInt("vr_gesture_reload", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // No longer used for triggering game reload — we set ammo directly
        public static bool TriggerReload => false;
        public static bool FallbackButtonReload { get; private set; }

        public static bool SuppressOffhandTrigger =>
            Instance != null && Enabled && Instance.State != ReloadState.Idle;

        // Insert check: distance from off-hand to DOMINANT hand (not bone)
        private const float INSERT_RADIUS = 0.30f;
        private const float TIMEOUT_SECONDS = 10f;

        // Weapon magazine state
        private Transform weaponMagTransform;
        private Renderer[] weaponMagRenderers;
        private int savedAmmo;          // ammo count before eject
        private int savedSpare;         // spare ammo before eject
        private object savedWeaponRef;  // weapon reference for ammo ops

        // Clones
        private GameObject ejectedClone;
        private Vector3 ejectedVelocity;
        private GameObject heldClone;
        private GameObject droppedClone;
        private Vector3 droppedVelocity;

        // Guide
        private GameObject guideObj;

        // State
        private float stateTimer;
        private bool prevOffTrigger;

        // Shell-fed detection (shotguns — no B press needed)
        private bool isShellFed;

        // Reflection
        private static bool reflectionReady;
        private static FieldInfo actorField;
        private static FieldInfo activeWeaponField;
        private static MethodInfo instantReloadMethod;

        // ──────────────────────────────────────────────
        //  MAIN UPDATE
        // ──────────────────────────────────────────────

        public void UpdateReload(GameObject dominantHand, GameObject offHand,
                                 uint offHandIndex, uint dominantIndex)
        {
            FallbackButtonReload = false;

            if (!Enabled || !VRManager.IsVRActive)
            {
                if (State != ReloadState.Idle) RestoreAndReset();
                return;
            }
            if (FpsActorController.instance == null) return;
            Transform wp = FpsActorController.instance.weaponParent;
            if (wp == null) return;

            bool offOk = offHand != null && offHand.transform.position.x < 9000f;
            bool domOk = dominantHand != null && dominantHand.transform.position.x < 9000f;

            bool offTrigger = VRManager.LeftHanded ? VRInput.RightTrigger : VRInput.LeftTrigger;
            bool offTriggerDown = offTrigger && !prevOffTrigger;
            bool offTriggerUp = !offTrigger && prevOffTrigger;
            prevOffTrigger = offTrigger;

            stateTimer += Time.deltaTime;

            switch (State)
            {
                case ReloadState.Idle:
                    // Magazine weapons: B to eject
                    // Shell weapons: off-hand trigger to grab shell directly
                    HandleIdle(wp, dominantHand, offHand, domOk, offOk,
                               dominantIndex, offHandIndex, offTriggerDown);
                    break;
                case ReloadState.MagEjected:
                    HandleMagEjected(offHand, offOk, offTriggerDown, offHandIndex);
                    break;
                case ReloadState.Holding:
                    HandleHolding(dominantHand, offHand, domOk, offOk,
                                  offTrigger, offTriggerUp, offHandIndex);
                    break;
                case ReloadState.Cooldown:
                    if (stateTimer > 0.3f) ResetState();
                    break;
            }

            UpdateFallingClone(ref ejectedClone, ref ejectedVelocity);
            UpdateFallingClone(ref droppedClone, ref droppedVelocity);
        }

        // ──────────────────────────────────────────────
        //  STATE HANDLERS
        // ──────────────────────────────────────────────

        private void HandleIdle(Transform wp, GameObject dominantHand, GameObject offHand,
                                bool domOk, bool offOk,
                                uint domIndex, uint offIndex, bool offTriggerDown)
        {
            bool bDown = VRManager.LeftHanded ? VRInput.LeftBDown : VRInput.RightBDown;

            // Detect weapon type on B press or off-hand trigger
            if (!bDown && !offTriggerDown) return;
            if (bDown && !domOk) return;

            object weapon = GetActiveWeapon();
            if (weapon == null)
            {
                if (bDown) FallbackButtonReload = true;
                return;
            }

            string typeName = weapon.GetType().Name;
            if (typeName.Contains("Melee") || typeName.Contains("Throwable"))
            { if (bDown) FallbackButtonReload = true; return; }

            Transform weaponTf = (weapon is Component c) ? c.transform : null;
            if (weaponTf == null) { if (bDown) FallbackButtonReload = true; return; }

            weaponMagTransform = FindMagTransform(weaponTf);
            if (weaponMagTransform == null)
            {
                if (bDown) FallbackButtonReload = true;
                return;
            }

            savedWeaponRef = weapon;
            weaponMagRenderers = weaponMagTransform.GetComponentsInChildren<Renderer>(true);

            // Detect shell-fed weapons (shotgun, grenade launcher)
            string magName = weaponMagTransform.name.ToLowerInvariant();
            isShellFed = magName.Contains("shell") || magName.Contains("guage") ||
                         magName.Contains("gauge") || magName.Contains("loading") ||
                         magName.Contains("chamber") || magName.Contains("breach") ||
                         magName.Contains("breech") || magName.Contains("round");

            if (isShellFed)
            {
                // SHOTGUN: off-hand trigger → grab shell → go straight to Holding
                if (!offTriggerDown) return; // only respond to trigger, not B
                Plugin.Log.LogInfo($"VR Reload: Shell weapon '{weaponMagTransform.name}', grabbing shell");
                SpawnHeldClone(offHand.transform);
                State = ReloadState.Holding;
                stateTimer = 0f;
                Haptic(offIndex, 2000);
            }
            else
            {
                // MAGAZINE: B → eject mag → MagEjected state
                if (!bDown) return; // only respond to B, not trigger
                Plugin.Log.LogInfo($"VR Reload: Ejecting '{weaponMagTransform.name}' from {weaponTf.name}");
                EjectAmmo(weapon);
                SetWeaponMagVisible(false);
                SpawnEjectedClone();
                State = ReloadState.MagEjected;
                stateTimer = 0f;
                Haptic(domIndex, 2000);
            }
        }

        private void HandleMagEjected(GameObject offHand, bool offOk, bool triggerDown, uint offIndex)
        {
            if (stateTimer > TIMEOUT_SECONDS)
            {
                Plugin.Log.LogInfo("VR Reload: Timeout, restoring");
                RestoreAmmo();
                RestoreAndReset();
                return;
            }

            if (!offOk) return;

            // Off-hand trigger → grab new mag (anywhere, no hip check)
            if (triggerDown)
            {
                SpawnHeldClone(offHand.transform);
                State = ReloadState.Holding;
                stateTimer = 0f;
                Haptic(offIndex, 3000);
                Plugin.Log.LogInfo("VR Reload: Mag grabbed");
            }
        }

        private void HandleHolding(GameObject dominantHand, GameObject offHand,
                                   bool domOk, bool offOk,
                                   bool triggerHeld, bool triggerUp, uint offIndex)
        {
            if (stateTimer > TIMEOUT_SECONDS)
            {
                Plugin.Log.LogInfo("VR Reload: Timeout holding, restoring");
                RestoreAmmo();
                RestoreAndReset();
                return;
            }

            if (offOk && heldClone != null)
            {
                heldClone.transform.position = offHand.transform.position;
                heldClone.transform.rotation = offHand.transform.rotation;
            }

            if (!offOk || !triggerUp) return;

            // Check distance from off-hand to DOMINANT HAND (not mag bone)
            // This works for all weapon types since the weapon is in the dominant hand
            float dist = domOk
                ? Vector3.Distance(offHand.transform.position, dominantHand.transform.position)
                : float.MaxValue;

            Plugin.Log.LogInfo($"VR Reload: Trigger released, dist to dominant hand={dist:F2}");

            if (dist < INSERT_RADIUS)
            {
                DestroyObj(ref heldClone);
                if (isShellFed)
                {
                    // Shell: add 1 round, go back to Idle (can load more)
                    InsertShell();
                    Haptic(offIndex, 3000);
                    Plugin.Log.LogInfo("VR Reload: Shell loaded");
                    ResetState();
                }
                else
                {
                    // Magazine: reattach, fill to max
                    SetWeaponMagVisible(true);
                    InsertMagazine();
                    Haptic(offIndex, 5000);
                    Plugin.Log.LogInfo("VR Reload: Mag inserted!");
                    State = ReloadState.Cooldown;
                    stateTimer = 0f;
                }
            }
            else
            {
                // DROP — can grab again
                Plugin.Log.LogInfo("VR Reload: Dropped");
                DropHeldClone();
                if (isShellFed)
                    ResetState(); // shell: back to idle, can grab another
                else
                {
                    State = ReloadState.MagEjected;
                    stateTimer = 0f;
                }
            }
        }

        // ──────────────────────────────────────────────
        //  AMMO MANAGEMENT
        // ──────────────────────────────────────────────

        // Config dump once
        private bool hasDumpedConfig;

        /// <summary>
        /// Gets maxAmmo from weapon.configuration (nested object).
        /// </summary>
        private int GetMaxAmmo(object weapon)
        {
            try
            {
                var wt = weapon.GetType();
                // Get the configuration sub-object
                var configField = wt.GetField("configuration",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (configField == null) return -1;

                object config = configField.GetValue(weapon);
                if (config == null) return -1;

                var ct = config.GetType();

                // One-time: dump configuration fields
                if (!hasDumpedConfig)
                {
                    hasDumpedConfig = true;
                    Plugin.Log.LogInfo("VR Reload: === Configuration fields ===");
                    foreach (var f in ct.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        try { Plugin.Log.LogInfo($"VR Reload:   {f.FieldType.Name} {f.Name} = {f.GetValue(config)}"); }
                        catch { Plugin.Log.LogInfo($"VR Reload:   {f.FieldType.Name} {f.Name} = <err>"); }
                    }
                    Plugin.Log.LogInfo("VR Reload: === End config ===");
                }

                // Try various field names for max ammo
                string[] names = { "maxAmmo", "ammo", "magazineSize", "clipSize", "maxAmmoCount" };
                foreach (string name in names)
                {
                    int val = GetInt(config, ct, name);
                    if (val > 0)
                    {
                        Plugin.Log.LogInfo($"VR Reload: maxAmmo from config.{name} = {val}");
                        return val;
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"VR Reload: GetMaxAmmo failed: {e.Message}");
            }
            return -1;
        }

        private void EjectAmmo(object weapon)
        {
            var wt = weapon.GetType();
            savedAmmo = GetInt(weapon, wt, "ammo");
            savedSpare = GetInt(weapon, wt, "spareAmmo");
            int chambered = savedAmmo > 0 ? 1 : 0;
            SetInt(weapon, wt, "ammo", chambered);
            Plugin.Log.LogInfo($"VR Reload: Eject: was={savedAmmo} now={chambered} spare={savedSpare}");
        }

        /// <summary>
        /// Magazine insert: fill to maxAmmo, subtract from spareAmmo.
        /// </summary>
        private void InsertMagazine()
        {
            if (savedWeaponRef == null) return;
            try
            {
                var wt = savedWeaponRef.GetType();
                int maxAmmo = GetMaxAmmo(savedWeaponRef);
                int currentAmmo = GetInt(savedWeaponRef, wt, "ammo"); // 0 or 1 (chambered)
                int spare = GetInt(savedWeaponRef, wt, "spareAmmo");

                if (maxAmmo > 0)
                {
                    int needed = maxAmmo - currentAmmo;
                    int toLoad = (spare >= 0) ? Mathf.Min(needed, spare) : needed;
                    SetInt(savedWeaponRef, wt, "ammo", currentAmmo + toLoad);
                    if (spare >= 0)
                        SetInt(savedWeaponRef, wt, "spareAmmo", spare - toLoad);
                    Plugin.Log.LogInfo($"VR Reload: Mag insert: ammo={currentAmmo + toLoad} spare={spare - toLoad} (max={maxAmmo})");
                }
                else
                {
                    // Fallback: restore pre-eject ammo
                    SetInt(savedWeaponRef, wt, "ammo", savedAmmo);
                    Plugin.Log.LogInfo($"VR Reload: maxAmmo unknown, restored to {savedAmmo}");
                }
                SetBool(savedWeaponRef, wt, "reloading", false);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"VR Reload: InsertMagazine failed: {e.Message}");
            }
        }

        /// <summary>
        /// Shell insert: add 1 round, subtract 1 from spareAmmo.
        /// </summary>
        private void InsertShell()
        {
            if (savedWeaponRef == null) return;
            try
            {
                var wt = savedWeaponRef.GetType();
                int currentAmmo = GetInt(savedWeaponRef, wt, "ammo");
                int spare = GetInt(savedWeaponRef, wt, "spareAmmo");
                int maxAmmo = GetMaxAmmo(savedWeaponRef);

                // Don't overfill
                if (maxAmmo > 0 && currentAmmo >= maxAmmo)
                {
                    Plugin.Log.LogInfo("VR Reload: Already full");
                    return;
                }
                if (spare == 0)
                {
                    Plugin.Log.LogInfo("VR Reload: No spare ammo");
                    return;
                }

                SetInt(savedWeaponRef, wt, "ammo", currentAmmo + 1);
                if (spare > 0)
                    SetInt(savedWeaponRef, wt, "spareAmmo", spare - 1);
                Plugin.Log.LogInfo($"VR Reload: Shell loaded: ammo={currentAmmo + 1} spare={spare - 1}");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"VR Reload: InsertShell failed: {e.Message}");
            }
        }

        private void RestoreAmmo()
        {
            if (savedWeaponRef == null) return;
            try
            {
                var wt = savedWeaponRef.GetType();
                SetInt(savedWeaponRef, wt, "ammo", savedAmmo);
                Plugin.Log.LogInfo($"VR Reload: Timeout, restored ammo to {savedAmmo}");
            }
            catch { }
        }

        // ──────────────────────────────────────────────
        //  MAGAZINE FINDING
        // ──────────────────────────────────────────────

        private Transform FindMagTransform(Transform weaponRoot)
        {
            Transform best = null;
            int bestScore = int.MaxValue;
            FindMagRecursive(weaponRoot, ref best, ref bestScore, 0);
            return best;
        }

        private void FindMagRecursive(Transform parent, ref Transform best, ref int bestScore, int depth)
        {
            if (depth > 8) return;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                string name = child.name.ToLowerInvariant();
                int score = ScoreMagName(name);
                if (score < bestScore)
                {
                    if (child.GetComponent<Renderer>() != null ||
                        child.GetComponentInChildren<Renderer>() != null)
                    {
                        best = child;
                        bestScore = score;
                        Plugin.Log.LogInfo($"VR Reload: Candidate '{child.name}' score={score}");
                    }
                }
                FindMagRecursive(child, ref best, ref bestScore, depth + 1);
            }
        }

        private int ScoreMagName(string nameLower)
        {
            // Penalize animation/hand/arm meshes
            if (nameLower.Contains("reload") || nameLower.Contains("hand") ||
                nameLower.Contains("arm") || nameLower.Contains("muzzle") ||
                nameLower.Contains("particle") || nameLower.Contains("audio") ||
                nameLower.Contains("camera") || nameLower.Contains("lens"))
                return 9000;

            if (nameLower == "mag" || nameLower == "magazine" || nameLower == "clip") return 1;
            if (nameLower.StartsWith("mag") && !nameLower.Contains("reload")) return 5 + nameLower.Length;
            if (nameLower.StartsWith("magazine")) return 5 + nameLower.Length;
            if (nameLower.Contains("magazine")) return 20 + nameLower.Length;
            if (nameLower.Contains("_mag")) return 25 + nameLower.Length;
            if (nameLower.Contains("mag_")) return 26 + nameLower.Length;
            if (nameLower.Contains("mag")) return 30 + nameLower.Length;
            if (nameLower.Contains("clip") || nameLower.Contains("drum")) return 40;
            if (nameLower.Contains("rocket") || nameLower.Contains("missile")) return 50;
            if (nameLower.Contains("shell") || nameLower.Contains("guage") || nameLower.Contains("gauge")) return 50;
            if (nameLower.Contains("loading_gate") || nameLower.Contains("chamber")) return 55;
            if (nameLower.Contains("breach") || nameLower.Contains("breech")) return 55;
            if (nameLower.Contains("round") || nameLower.Contains("cartridge")) return 60;
            if (nameLower.Contains("ammo")) return 70;

            return int.MaxValue;
        }

        // ──────────────────────────────────────────────
        //  MAG VISIBILITY
        // ──────────────────────────────────────────────

        private void SetWeaponMagVisible(bool visible)
        {
            if (weaponMagRenderers == null) return;
            foreach (var r in weaponMagRenderers)
                if (r != null) r.enabled = visible;
        }

        // ──────────────────────────────────────────────
        //  CLONE MANAGEMENT
        // ──────────────────────────────────────────────

        private void SpawnEjectedClone()
        {
            DestroyObj(ref ejectedClone);
            if (weaponMagTransform == null) return;
            ejectedClone = CloneMagVisual(weaponMagTransform);
            if (ejectedClone == null) return;
            ejectedClone.name = "Ejected Mag";
            Transform wp = FpsActorController.instance.weaponParent;
            ejectedVelocity = wp.rotation * new Vector3(-0.3f, -1.5f, 0f);
        }

        private void SpawnHeldClone(Transform hand)
        {
            DestroyObj(ref heldClone);
            if (weaponMagTransform == null) return;
            heldClone = CloneMagVisual(weaponMagTransform);
            if (heldClone == null) return;
            heldClone.name = "Held Mag";
            heldClone.transform.position = hand.position;
            heldClone.transform.rotation = hand.rotation;
        }

        private void DropHeldClone()
        {
            if (heldClone == null) return;
            DestroyObj(ref droppedClone);
            droppedClone = heldClone;
            droppedClone.name = "Dropped Mag";
            heldClone = null;
            droppedVelocity = Vector3.down * 0.5f;
        }

        private GameObject CloneMagVisual(Transform source)
        {
            try
            {
                // Instantiate preserves the original active/inactive state of children
                // (so alternate mag models like drum mags stay hidden)
                var clone = Object.Instantiate(source.gameObject);
                clone.transform.SetParent(null);

                // Strip non-visual components
                foreach (var col in clone.GetComponentsInChildren<Collider>(true))
                    Object.Destroy(col);
                foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
                    Object.Destroy(mb);
                foreach (var anim in clone.GetComponentsInChildren<Animator>(true))
                    Object.Destroy(anim);

                // Re-enable renderers ONLY on the clone root and already-active children
                // (the source renderers were disabled by SetWeaponMagVisible(false))
                foreach (var r in clone.GetComponentsInChildren<Renderer>(false)) // false = only active objects
                    r.enabled = true;

                // Make the clone root active (it might be inactive)
                clone.SetActive(true);

                return clone;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"VR Reload: Clone failed: {e.Message}");
                return null;
            }
        }

        private void UpdateFallingClone(ref GameObject obj, ref Vector3 velocity)
        {
            if (obj == null) return;
            velocity += Vector3.down * 9.8f * Time.deltaTime;
            obj.transform.position += velocity * Time.deltaTime;
            obj.transform.Rotate(90f * Time.deltaTime, 30f * Time.deltaTime, 0f);
            Camera cam = Camera.main;
            if (cam != null && Vector3.Distance(obj.transform.position, cam.transform.position) > 8f)
                DestroyObj(ref obj);
        }

        // ──────────────────────────────────────────────
        //  REFLECTION
        // ──────────────────────────────────────────────

        private static void InitReflection()
        {
            if (reflectionReady) return;
            reflectionReady = true;
            try
            {
                var fpsType = typeof(FpsActorController);
                actorField = fpsType.GetField("actor",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (actorField != null)
                {
                    var actorType = actorField.FieldType;
                    activeWeaponField = actorType.GetField("activeWeapon",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    instantReloadMethod = actorType.GetMethod("InstantlyReloadCarriedWeapons",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Plugin.Log.LogInfo($"VR Reload: Reflection OK. " +
                        $"actor={actorField != null} weapon={activeWeaponField != null} " +
                        $"instantReload={instantReloadMethod != null}");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"VR Reload: Reflection failed: {e.Message}");
            }
        }

        private object GetActiveWeapon()
        {
            InitReflection();
            try
            {
                var fpa = FpsActorController.instance;
                if (fpa == null || actorField == null) return null;
                object actor = actorField.GetValue(fpa);
                if (actor == null || activeWeaponField == null) return null;
                return activeWeaponField.GetValue(actor);
            }
            catch { return null; }
        }

        private int GetInt(object obj, System.Type t, string name)
        {
            try
            {
                // Try field first
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (f != null && (f.FieldType == typeof(int) || f.FieldType == typeof(float)))
                    return System.Convert.ToInt32(f.GetValue(obj));
                // Try property (including inherited)
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (p != null) return System.Convert.ToInt32(p.GetValue(obj));
                // Try base types explicitly
                var bt = t.BaseType;
                while (bt != null && bt != typeof(object))
                {
                    f = bt.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null) return System.Convert.ToInt32(f.GetValue(obj));
                    p = bt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null) return System.Convert.ToInt32(p.GetValue(obj));
                    bt = bt.BaseType;
                }
            }
            catch { }
            return -1;
        }

        private void SetInt(object obj, System.Type t, string name, int value)
        {
            try
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (f != null && (f.FieldType == typeof(int) || f.FieldType == typeof(float)))
                { f.SetValue(obj, f.FieldType == typeof(float) ? (object)(float)value : (object)value); return; }
                var bt = t.BaseType;
                while (bt != null && bt != typeof(object))
                {
                    f = bt.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null) { f.SetValue(obj, f.FieldType == typeof(float) ? (object)(float)value : (object)value); return; }
                    bt = bt.BaseType;
                }
            }
            catch { }
        }

        private void SetBool(object obj, System.Type t, string name, bool value)
        {
            try
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (f != null && f.FieldType == typeof(bool)) { f.SetValue(obj, value); return; }
                var bt = t.BaseType;
                while (bt != null && bt != typeof(object))
                {
                    f = bt.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && f.FieldType == typeof(bool)) { f.SetValue(obj, value); return; }
                    bt = bt.BaseType;
                }
            }
            catch { }
        }

        // ──────────────────────────────────────────────
        //  CLEANUP
        // ──────────────────────────────────────────────

        private void RestoreAndReset()
        {
            SetWeaponMagVisible(true);
            ResetState();
        }

        private void ResetState()
        {
            State = ReloadState.Idle;
            stateTimer = 0f;
            FallbackButtonReload = false;
            weaponMagTransform = null;
            weaponMagRenderers = null;
            savedWeaponRef = null;
            DestroyObj(ref ejectedClone);
            DestroyObj(ref heldClone);
            DestroyObj(ref droppedClone);
            if (guideObj != null) guideObj.SetActive(false);
        }

        private void DestroyObj(ref GameObject obj)
        {
            if (obj != null) { Object.Destroy(obj); obj = null; }
        }

        private void Haptic(uint idx, ushort us)
        {
            if (idx != OpenVR.k_unTrackedDeviceIndexInvalid && OpenVR.System != null)
                OpenVR.System.TriggerHapticPulse(idx, 0, us);
        }

        private void OnDestroy()
        {
            SetWeaponMagVisible(true);
            DestroyObj(ref ejectedClone);
            DestroyObj(ref heldClone);
            DestroyObj(ref droppedClone);
            DestroyObj(ref guideObj);
        }

        public static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("VR Reload");
            Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<VRReload>();
            Plugin.Log.LogInfo("VR Reload system created.");
        }

        public static void DestroyInstance()
        {
            if (Instance != null) { Object.Destroy(Instance.gameObject); Instance = null; }
        }
    }
}
