using System;
using System.Collections.Generic;
using UnityEngine;

namespace RavenfieldVRMod
{
    /// <summary>
    /// IK for the weapon's first-person arm rig (Weapon.arms).
    /// Off-hand arm follows the off-hand cube, dominant hand keeps its weapon
    /// animation, both arms are re-rooted at body-relative shoulders.
    /// Runs from VRControllers.LateUpdate after the weapon has been placed.
    /// </summary>
    public static class VRArmIK
    {
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("vr_arm_ik", 1) == 1;
            set { PlayerPrefs.SetInt("vr_arm_ik", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // Free off-hand tuning (F3-F8), persisted
        private static float OffhandForwardCm
        {
            get => PlayerPrefs.GetFloat("vr_arm_ik_off_fwd", -3f);
            set { PlayerPrefs.SetFloat("vr_arm_ik_off_fwd", value); PlayerPrefs.Save(); }
        }
        private static float FreeHandRollDeg
        {
            get => PlayerPrefs.GetFloat("vr_arm_ik_free_roll", -160f);
            set { PlayerPrefs.SetFloat("vr_arm_ik_free_roll", value); PlayerPrefs.Save(); }
        }
        private static float FreeHandYawDeg
        {
            get => PlayerPrefs.GetFloat("vr_arm_ik_free_yaw", 10f);
            set { PlayerPrefs.SetFloat("vr_arm_ik_free_yaw", value); PlayerPrefs.Save(); }
        }

        private static readonly Vector3 SHOULDER_OFFSET = new Vector3(0.18f, -0.25f, -0.08f); // from head, body yaw frame, x mirrored
        private static readonly Vector3 POLE_OFFSET = new Vector3(0.35f, -0.6f, -0.25f);     // elbow hint, same frame
        private const float BLEND_SPEED = 8f;
        private const float MIN_BONE_LENGTH = 0.05f;

        private class ArmChain
        {
            public Transform root, upper, lower, hand; // root = topmost bone exclusive to this arm
            public bool modelLeft;
            public float weight;
            public Quaternion handToGrip = Quaternion.identity; // animated hand rotation relative to the weapon
            public bool haveHandToGrip;
            public BoneMemory[] memory;
        }

        // The weapon animator doesn't evaluate every rendered frame and clips rarely key
        // positions — restore the previous animated pose on bones still holding our write.
        private class BoneMemory
        {
            public Transform bone;
            private Vector3 prePos, lastPos;
            private Quaternion preRot, lastRot;
            private bool wrote;

            public BoneMemory(Transform t) { bone = t; prePos = t.localPosition; preRot = t.localRotation; }

            public void RestoreIfUntouched()
            {
                if (wrote)
                {
                    wrote = false;
                    if ((bone.localPosition - lastPos).sqrMagnitude < 1e-10f) bone.localPosition = prePos;
                    if (Quaternion.Angle(bone.localRotation, lastRot) < 0.01f) bone.localRotation = preRot;
                }
                prePos = bone.localPosition;
                preRot = bone.localRotation;
            }

            public void Remember() { lastPos = bone.localPosition; lastRot = bone.localRotation; wrote = true; }
        }

        private static SkinnedMeshRenderer rigRenderer;
        private static ArmChain offArm, domArm;
        private static bool bonesLogged;

        public static void LateUpdate(GameObject offhandCube, bool twoHanded, Quaternion weaponRot)
        {
            var controller = FpsActorController.instance;
            Actor actor = controller != null ? controller.actor : null;
            Weapon weapon = actor != null ? actor.activeWeapon : null;
            SkinnedMeshRenderer arms = weapon != null ? weapon.arms : null;

            if (arms == null || !arms.enabled || !arms.gameObject.activeInHierarchy)
            {
                rigRenderer = null;
                offArm = domArm = null;
                return;
            }
            if (arms != rigRenderer) DiscoverRig(arms, weapon);
            if (offArm == null && domArm == null) return;

            if (offArm != null) foreach (var m in offArm.memory) m.RestoreIfUntouched();
            if (domArm != null) foreach (var m in domArm.memory) m.RestoreIfUntouched();

            bool alive = !actor.dead && !actor.fallenOver &&
                         (actor.ragdoll == null || actor.ragdoll.state == ActiveRaggy.State.Animate);
            bool active = Enabled && VRManager.IsVRActive && alive;

            Camera cam = controller.GetActiveCamera();
            Vector3 headPos = cam != null ? cam.transform.position : actor.transform.position + Vector3.up * 1.6f;
            Quaternion bodyYaw = Quaternion.Euler(0f, actor.transform.eulerAngles.y, 0f);

            // Classic reload: animation drives the off-hand. Gesture reload: real hand rules.
            bool classicReloading = weapon.reloading && !VRReload.Enabled;

            if (offArm != null)
            {
                bool cubeOk = offhandCube != null && offhandCube.activeInHierarchy && offhandCube.transform.position.x < 9000f;
                bool want = active && cubeOk && !classicReloading;
                offArm.weight = Mathf.MoveTowards(offArm.weight, want ? 1f : 0f, BLEND_SPEED * Time.deltaTime);

                if (!weapon.reloading)
                {
                    offArm.handToGrip = Quaternion.Inverse(weapon.transform.rotation) * offArm.hand.rotation;
                    offArm.haveHandToGrip = true;
                }

                if (offArm.weight > 0.001f && cubeOk)
                {
                    Transform cube = offhandCube.transform;
                    Vector3 shoulder = headPos + bodyYaw * Mirror(SHOULDER_OFFSET, offArm.modelLeft);
                    Vector3 ikPos = cube.position + cube.rotation * new Vector3(0f, 0f, OffhandForwardCm * 0.01f);

                    // Gripping: keep the foregrip pose on the two-handed weapon axis.
                    // Free: the grip pose around the cube, rolled from palm-up to holding a controller.
                    Quaternion grip;
                    Vector3 pole;
                    if (twoHanded)
                    {
                        grip = weaponRot;
                        pole = AnimatedElbowPole(offArm, shoulder) ?? GenericPole(offArm, shoulder, bodyYaw);
                    }
                    else
                    {
                        grip = cube.rotation * Quaternion.Euler(0f, FreeHandYawDeg, 0f)
                                             * Quaternion.AngleAxis(FreeHandRollDeg, Vector3.forward);
                        pole = GenericPole(offArm, shoulder, bodyYaw);
                    }
                    Quaternion ikRot = offArm.haveHandToGrip ? grip * offArm.handToGrip : offArm.hand.rotation;
                    SolveArm(offArm, shoulder, ikPos, ikRot, pole, offArm.weight);
                }
            }

            if (domArm != null)
            {
                domArm.weight = Mathf.MoveTowards(domArm.weight, active ? 1f : 0f, BLEND_SPEED * Time.deltaTime);
                if (domArm.weight > 0.001f)
                {
                    Vector3 shoulder = headPos + bodyYaw * Mirror(SHOULDER_OFFSET, domArm.modelLeft);
                    Vector3 pole = AnimatedElbowPole(domArm, shoulder) ?? GenericPole(domArm, shoulder, bodyYaw);
                    SolveArm(domArm, shoulder, domArm.hand.position, domArm.hand.rotation, pole, domArm.weight);
                }
            }
        }

        // Pole reproducing the animated elbow direction (null when the arm is straight)
        private static Vector3? AnimatedElbowPole(ArmChain arm, Vector3 shoulder)
        {
            Vector3 axis = arm.hand.position - arm.upper.position;
            if (axis.sqrMagnitude < 1e-6f) return null;
            Vector3 elbow = Vector3.ProjectOnPlane(arm.lower.position - arm.upper.position, axis.normalized);
            if (elbow.sqrMagnitude < 1e-4f) return null;
            return shoulder + elbow.normalized * 0.5f;
        }

        private static Vector3 GenericPole(ArmChain arm, Vector3 shoulder, Quaternion bodyYaw) =>
            shoulder + bodyYaw * Mirror(POLE_OFFSET, arm.modelLeft);

        private static Vector3 Mirror(Vector3 v, bool left) => left ? new Vector3(-v.x, v.y, v.z) : v;

        /// <summary>Two-bone IK in world space; blends from the animated pose by w.</summary>
        private static void SolveArm(ArmChain arm, Vector3 shoulderTarget, Vector3 handTarget, Quaternion handRot,
                                     Vector3 pole, float w)
        {
            Transform a = arm.upper, b = arm.lower, c = arm.hand;

            Vector3 animShoulder = a.position;
            Vector3 animHand = c.position;
            Quaternion animHandRot = c.rotation;
            Quaternion animHandLocal = c.localRotation;
            float la = (b.position - a.position).magnitude;
            float lb = (c.position - b.position).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;

            Vector3 shoulder = Vector3.Lerp(animShoulder, shoulderTarget, w);
            Vector3 target = Vector3.Lerp(animHand, handTarget, w);
            Quaternion finalHandRot = Quaternion.Slerp(animHandRot, handRot, w);

            // Out of reach: slide the shoulder toward the hand
            float reach = (la + lb) * 0.995f;
            Vector3 toT = target - shoulder;
            float dist = toT.magnitude;
            if (dist > reach && dist > 1e-4f)
            {
                shoulder = target - toT / dist * reach;
                toT = target - shoulder;
                dist = reach;
            }
            dist = Mathf.Max(dist, 1e-3f);

            // Move the whole arm root (clavicle if any) so shoulder vertices don't tear
            if (arm.root != null && arm.root != a)
                arm.root.position += shoulder - animShoulder;
            else
                a.position = shoulder;

            AimAt(a, c, target);

            // Elbow bend (law of cosines)
            float cosB = Mathf.Clamp((la * la + lb * lb - dist * dist) / (2f * la * lb), -1f, 1f);
            float wantAngle = Mathf.Acos(cosB) * Mathf.Rad2Deg;
            Vector3 ba = a.position - b.position;
            Vector3 bc = c.position - b.position;
            float curAngle = Vector3.Angle(ba, bc);
            Vector3 axis = Vector3.Cross(bc, ba);
            if (axis.sqrMagnitude < 1e-8f)
            {
                axis = Vector3.Cross(toT, pole - shoulder);
                if (axis.sqrMagnitude < 1e-8f) axis = Vector3.up;
            }
            axis.Normalize();
            float delta = wantAngle - curAngle;
            Quaternion before = b.rotation;
            b.rotation = Quaternion.AngleAxis(delta, axis) * before;
            float check = Vector3.Angle(a.position - b.position, c.position - b.position);
            if (Mathf.Abs(check - wantAngle) > Mathf.Abs(curAngle - wantAngle) + 0.01f)
                b.rotation = Quaternion.AngleAxis(-delta, axis) * before;

            AimAt(a, c, target);

            // Twist about shoulder→hand so the elbow points at the pole
            {
                Vector3 n = (target - a.position).normalized;
                Vector3 elbowDir = Vector3.ProjectOnPlane(b.position - a.position, n);
                Vector3 poleDir = Vector3.ProjectOnPlane(pole - a.position, n);
                if (elbowDir.sqrMagnitude > 1e-6f && poleDir.sqrMagnitude > 1e-6f)
                    a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(elbowDir, poleDir, n), n) * a.rotation;
            }

            // Forearm roll follows the hand (keeps the animated hand-in-forearm relation)
            {
                Vector3 forearmDir = c.position - b.position;
                if (forearmDir.sqrMagnitude > 1e-8f)
                {
                    forearmDir.Normalize();
                    Vector3 alongLocal = Quaternion.Inverse(b.rotation) * forearmDir;
                    Quaternion bDesired = finalHandRot * Quaternion.Inverse(animHandLocal);
                    b.rotation = Quaternion.FromToRotation(bDesired * alongLocal, forearmDir) * bDesired;
                }
            }

            c.rotation = finalHandRot;

            foreach (var m in arm.memory) m.Remember();
        }

        private static void AimAt(Transform upper, Transform hand, Vector3 target)
        {
            Vector3 cur = hand.position - upper.position;
            Vector3 want = target - upper.position;
            if (cur.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            upper.rotation = Quaternion.FromToRotation(cur, want) * upper.rotation;
        }

        /// <summary>
        /// Finds both arm chains in the rig. Hand = bone named like a hand (else a leaf),
        /// forearm = first ancestor a bone-length away, upper arm = ancestor ~a forearm
        /// length above the elbow (skips split/twist helpers). Vanilla rig:
        /// Arm.L > Arm.L.001 > Wrist.L (forearm) > Hand.L.
        /// </summary>
        private static void DiscoverRig(SkinnedMeshRenderer arms, Weapon weapon)
        {
            rigRenderer = arms;
            offArm = domArm = null;
            try
            {
                Transform[] bones = arms.bones;
                if (bones == null || bones.Length == 0) { LogRig(weapon, "no bones"); return; }

                var set = new HashSet<Transform>();
                foreach (var t in bones) if (t != null) set.Add(t);

                var hands = new List<Transform>();
                foreach (var t in set)
                    if (NameHas(t.name, "hand") || NameHas(t.name, "wrist") || NameHas(t.name, "palm"))
                        hands.Add(t);
                if (hands.Count < 2)
                {
                    hands.Clear();
                    foreach (var t in set)
                    {
                        bool hasChildBone = false;
                        foreach (var o in set) if (o != t && o.parent == t) { hasChildBone = true; break; }
                        if (!hasChildBone) hands.Add(t);
                    }
                }
                if (hands.Count > 2)
                {
                    var named = hands.FindAll(h => NameHas(h.name, "hand"));
                    if (named.Count == 2) hands = named;
                }

                var chains = new List<ArmChain>();
                foreach (var hand in hands)
                {
                    Transform lower = null;
                    for (Transform t = hand.parent; t != null && set.Contains(t); t = t.parent)
                        if ((t.position - hand.position).magnitude >= MIN_BONE_LENGTH) { lower = t; break; }
                    if (lower == null) continue;

                    float forearmLen = (hand.position - lower.position).magnitude;
                    Transform upper = null, topmost = null;
                    for (Transform t = lower.parent; t != null && set.Contains(t); t = t.parent)
                    {
                        topmost = t;
                        if ((t.position - lower.position).magnitude >= 0.7f * forearmLen) { upper = t; break; }
                    }
                    if (upper == null) upper = topmost;
                    if (upper == null || chains.Exists(c => c.upper == upper)) continue;

                    chains.Add(new ArmChain
                    {
                        upper = upper, lower = lower, hand = hand,
                        memory = new[] { new BoneMemory(upper), new BoneMemory(lower), new BoneMemory(hand) }
                    });
                }
                if (chains.Count < 1) { LogRig(weapon, $"no arm chains from {hands.Count} hand candidates"); return; }

                // Side: by name, else by position relative to the weapon
                Transform reference = weapon.transform;
                foreach (var ch in chains)
                {
                    int byName = SideFromName(ch.hand.name) + SideFromName(ch.lower.name) + SideFromName(ch.upper.name);
                    ch.modelLeft = byName != 0 ? byName < 0 : reference.InverseTransformPoint(ch.upper.position).x < 0f;
                }
                if (chains.Count >= 2 && chains[0].modelLeft == chains[1].modelLeft)
                {
                    float x0 = reference.InverseTransformPoint(chains[0].upper.position).x;
                    float x1 = reference.InverseTransformPoint(chains[1].upper.position).x;
                    chains[0].modelLeft = x0 < x1;
                    chains[1].modelLeft = !chains[0].modelLeft;
                }

                // Root: ancestors of the upper arm not shared with the other arm
                foreach (var ch in chains)
                {
                    ch.root = ch.upper;
                    Transform t = ch.upper.parent;
                    int hops = 0;
                    while (t != null && set.Contains(t) && hops++ < 4)
                    {
                        bool shared = false;
                        foreach (var other in chains)
                            if (other != ch && other.upper.IsChildOf(t)) { shared = true; break; }
                        if (shared) break;
                        ch.root = t;
                        t = t.parent;
                    }
                    if (ch.root != ch.upper)
                        ch.memory = new[] { new BoneMemory(ch.root), ch.memory[0], ch.memory[1], ch.memory[2] };
                }

                // Model's left arm = foregrip/off-hand arm (the mesh isn't mirrored for left-handed mode)
                foreach (var ch in chains)
                {
                    if (ch.modelLeft) offArm = ch; else domArm = ch;
                }
                LogRig(weapon, "ok");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"VR Arm IK: rig discovery failed for {weapon?.name}: {e.Message}");
                offArm = domArm = null;
            }
        }

        private static bool NameHas(string name, string token) =>
            name != null && name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        // -1 left, +1 right, 0 unknown ("Left", "L_", ".L", "LHand", ...)
        private static int SideFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string n = name.ToLowerInvariant();
            if (n.Contains("left")) return -1;
            if (n.Contains("right")) return 1;
            for (int i = 0; i < n.Length; i++)
            {
                char ch = n[i];
                if (ch != 'l' && ch != 'r') continue;
                bool startOk = i == 0 || !char.IsLetter(n[i - 1]);
                bool endOk = i == n.Length - 1 || !char.IsLetter(n[i + 1]);
                bool capsStyle = i == 0 && name.Length > 1 && char.IsUpper(name[0]) && char.IsUpper(name[1]);
                if ((startOk && endOk) || capsStyle) return ch == 'l' ? -1 : 1;
            }
            return 0;
        }

        private static void LogRig(Weapon weapon, string status)
        {
            string Desc(ArmChain c) => c == null ? "none" :
                $"{(c.root != c.upper ? c.root.name + " >> " : "")}{c.upper.name} > {c.lower.name} > {c.hand.name}";
            Plugin.Log.LogInfo($"VR Arm IK: rig for '{weapon?.name}' {status} — offhand arm: {Desc(offArm)}, dominant arm: {Desc(domArm)}");
            if (!bonesLogged || status != "ok")
            {
                bonesLogged = true;
                var sb = new System.Text.StringBuilder("VR Arm IK: bones:");
                foreach (var b in rigRenderer.bones) if (b != null) sb.Append(' ').Append(b.name);
                Plugin.Log.LogInfo(sb.ToString());
            }
        }

        // F3/F4 yaw, F5/F6 roll, F7/F8 forward offset of the free off-hand; F9 status + rig dump
        public static void HandleDebugKeys()
        {
            if (Input.GetKeyDown(KeyCode.F3)) { FreeHandYawDeg -= 5f; Plugin.Log.LogInfo($"VR Arm IK: free-hand yaw {FreeHandYawDeg}°"); }
            if (Input.GetKeyDown(KeyCode.F4)) { FreeHandYawDeg += 5f; Plugin.Log.LogInfo($"VR Arm IK: free-hand yaw {FreeHandYawDeg}°"); }
            if (Input.GetKeyDown(KeyCode.F5)) { FreeHandRollDeg -= 15f; Plugin.Log.LogInfo($"VR Arm IK: free-hand roll {FreeHandRollDeg}°"); }
            if (Input.GetKeyDown(KeyCode.F6)) { FreeHandRollDeg += 15f; Plugin.Log.LogInfo($"VR Arm IK: free-hand roll {FreeHandRollDeg}°"); }
            if (Input.GetKeyDown(KeyCode.F7)) { OffhandForwardCm -= 1f; Plugin.Log.LogInfo($"VR Arm IK: off-hand forward {OffhandForwardCm}cm"); }
            if (Input.GetKeyDown(KeyCode.F8)) { OffhandForwardCm += 1f; Plugin.Log.LogInfo($"VR Arm IK: off-hand forward {OffhandForwardCm}cm"); }
            if (Input.GetKeyDown(KeyCode.F9))
            {
                var a = FpsActorController.instance?.actor;
                Plugin.Log.LogInfo($"VR Arm IK: enabled={Enabled} rig={(rigRenderer != null ? rigRenderer.name : "none")} " +
                                   $"off={(offArm != null ? offArm.weight.ToString("F2") : "none")} dom={(domArm != null ? domArm.weight.ToString("F2") : "none")} " +
                                   $"dead={a?.dead} fallen={a?.fallenOver} ragdoll={a?.ragdoll?.state} reloading={a?.activeWeapon?.reloading}");
                bonesLogged = false;
                if (rigRenderer != null && a?.activeWeapon != null) LogRig(a.activeWeapon, "dump");
            }
        }
    }
}
