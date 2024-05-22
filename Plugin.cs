﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AsksvinImproved
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class AsksvinImprovedPlugin : BaseUnityPlugin
    {
        internal const string ModName = "AsksvinImproved";
        internal const string ModVersion = "1.0.7";
        internal const string Author = "Azumatt";
        private const string ModGUID = $"{Author}.{ModName}";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource AsksvinImprovedLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        public void Awake()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
        }
    }

    /*[HarmonyPatch(typeof(Player), nameof(Player.FixedUpdate))]
    public static class FixedUpdatePatch
    {
        public static void Postfix(ref Player __instance)
        {
            if (__instance != Player.m_localPlayer || !__instance.TakeInput()) return;
            bool keyPressed = false;

            //Do the Get Creatures.
            List<Character> guysList = Character.GetAllCharacters();
            EnemyHud.instance.m_refPoint = __instance.transform.position;
            int guysNum = 0;


                foreach (Character character in guysList.Where(character => character is not Player && EnemyHud.instance.TestShow(character, false)))
                {
                    guysNum++;
                    EnemyHud.instance.ShowHud(character, false);
                    EnemyHud.instance.m_huds.TryGetValue(character, out EnemyHud.HudData hud);
                    if (hud == null) continue;
                    hud.m_hoverTimer = 0F;
                    hud.m_gui.SetActive(true);
                }


            //Update the enemy huds list if we found anything
            if (guysNum > 0)
            {
                Sadle? sadle = null;
                EnemyHud.instance.UpdateHuds(__instance, sadle, Time.deltaTime);
            }
        }
    }*/

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateEventPin))]
    public static class UpdateEventPinPatch
    {
        public static void Postfix(Minimap __instance)
        {
            if (PlayerStartDoodadControlPatch.LastHumanoidZDOID.IsNone()) return;
            //Populate the list of current HUD characters.
            List<Character> guysList =
                (from hud
                        in EnemyHud.instance.m_huds.Values
                    where hud.m_character != null
                          && hud.m_character.IsTamed()
                          && hud.m_character.GetZDOID() == PlayerStartDoodadControlPatch.LastHumanoidZDOID
                    select hud.m_character
                ).ToList();
            //Add minimap pins if they haven't been added already.
            foreach (Character character
                     in from character in guysList
                     where character is not Player
                     let flag = __instance.m_pins.Any(pin => pin.m_name.Equals($"$hud_tame {character.GetHoverName()} [Health: {character.GetHealth()}]"))
                     where !flag
                     select character)
            {
                __instance.AddPin(character.GetCenterPoint(), Minimap.PinType.None, $"$hud_tame {character.GetHoverName()} [Health: {character.GetHealth()}]", false, false);
                Sadle? sadle = null;
                EnemyHud.instance.UpdateHuds(Player.m_localPlayer, sadle, Time.deltaTime);
            }

            //Remove minimap pins which are not needed anymore.
            List<Minimap.PinData> removePins = new();

            foreach (Minimap.PinData pin in __instance.m_pins)
            {
                if (pin.m_type != Minimap.PinType.None) continue;
                bool flag = false;
                foreach (Character character in guysList.Where(character => pin.m_name.Equals($"$hud_tame {character.GetHoverName()} [Health: {character.GetHealth()}]")))
                {
                    pin.m_pos.x = character.GetCenterPoint().x;
                    pin.m_pos.y = character.GetCenterPoint().y;
                    pin.m_pos.z = character.GetCenterPoint().z;
                    flag = true;
                    break;
                }

                if (!flag)
                {
                    removePins.Add(pin);
                }
            }

            foreach (Minimap.PinData pin in removePins)
            {
                __instance.RemovePin(pin);
                AsksvinImprovedPlugin.AsksvinImprovedLogger.LogDebug("removing pin for " + pin.m_name);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StartDoodadControl))]
    static class PlayerStartDoodadControlPatch
    {
        public static bool RidingAsksvin;
        public static Humanoid RidingHumanoid = null!;
        public static ZDOID LastHumanoidZDOID;

        static void Postfix(Player __instance, IDoodadController shipControl)
        {
#if DEBUG
            AsksvinImprovedPlugin.AsksvinImprovedLogger.LogDebug($"PlayerIsRidingPatch: They are on {Utils.GetPrefabName(__instance.m_doodadController.GetControlledComponent().gameObject.name)}");
#endif
            if (Utils.GetPrefabName(shipControl.GetControlledComponent().gameObject.name) == "Asksvin")
            {
                RidingAsksvin = true;
                RidingHumanoid = shipControl.GetControlledComponent().transform.GetComponentInParent<Humanoid>();
                LastHumanoidZDOID = RidingHumanoid.GetZDOID();
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StopDoodadControl))]
    static class PlayerStopDoodadControlPatch
    {
        static bool Prefix(Player __instance)
        {
            if (__instance.m_doodadController == null || !__instance.m_doodadController.IsValid())
            {
                // Ensure dismount if the mount dies
                PlayerStartDoodadControlPatch.RidingAsksvin = false;
                PlayerStartDoodadControlPatch.RidingHumanoid = null!;
                return true;
            }


            return !PlayerStartDoodadControlPatch.RidingAsksvin;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    static class HumanoidStartAttackPatch
    {
        static bool Prefix(Humanoid __instance)
        {
            if (__instance != Player.m_localPlayer) return true;
            return !PlayerStartDoodadControlPatch.RidingAsksvin;
        }
    }


    [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
    static class PlayerAttachStopPatch
    {
        static bool Prefix(Player __instance)
        {
            return !PlayerStartDoodadControlPatch.RidingAsksvin;
        }
    }


    [HarmonyPatch(typeof(Player), nameof(Player.UpdateDoodadControls))]
    static class PlayerUpdateDoodadControlsPatch
    {
        static void Postfix(Player __instance)
        {
            if (__instance.m_doodadController == null || !__instance.m_doodadController.IsValid() || !PlayerStartDoodadControlPatch.RidingAsksvin)
                return;

            // Check if the mount is dead
            if (PlayerStartDoodadControlPatch.RidingHumanoid?.GetHealth() <= 0)
            {
                __instance.CustomAttachStop();
                return;
            }

            // Detect and handle jump input specifically for dismounting
            if (ZInput.GetButton("Jump") || ZInput.GetButtonDown("JoyJump"))
            {
                __instance.CustomAttachStop();
                return;
            }

            __instance.HandleInput();
        }
    }

    public static class PlayerExtensions
    {
        public static void CustomAttachStop(this Player p)
        {
            if (p.m_sleeping || !p.m_attached)
                return;
            if (p.m_attachPoint != null)
                p.transform.position = p.m_attachPoint.TransformPoint(p.m_detachOffset);
            if (p.m_attachColliders != null)
            {
                foreach (Collider attachCollider in p.m_attachColliders)
                {
                    if (attachCollider)
                        Physics.IgnoreCollision(p.m_collider, attachCollider, false);
                }

                p.m_attachColliders = null;
            }

            p.m_body.useGravity = true;
            p.m_attached = false;
            p.m_attachPoint = null;
            p.m_attachPointCamera = null;
            p.m_zanim.SetBool(p.m_attachAnimation, false);
            p.m_nview.GetZDO().Set(ZDOVars.s_inBed, false);
            p.ResetCloth();
            p.m_doodadController = null;
            p.StopDoodadControl();
        }

        public static void HandleInput(this Player player)
        {
            void ProcessInput(KeyCode key, int weaponIndex, string controllerButton = "")
            {
                bool buttonDown = false;
                // Check for Gamepad input not keyboard
                buttonDown = ZInput.IsGamepadActive() ? ZInput.GetButtonDown(controllerButton) : Input.GetKeyDown(key);
                if (!PlayerStartDoodadControlPatch.RidingHumanoid || !buttonDown || Menu.IsVisible() || !player.TakeInput()) return;
                if (PlayerStartDoodadControlPatch.RidingHumanoid.InAttack())
                    return;

                List<ItemDrop.ItemData> items = PlayerStartDoodadControlPatch.RidingHumanoid.m_inventory.GetAllItems().Where(i => i.IsWeapon()).ToList();

                if (items.Count <= weaponIndex) return;
                ItemDrop.ItemData weapon = items[weaponIndex];
                PlayerStartDoodadControlPatch.RidingHumanoid.EquipItem(weapon);
                float stamtoUse = weapon.m_shared.m_attack.m_attackStamina;
                Sadle? doodadController = player.GetDoodadController() as Sadle;
                if (doodadController == null) return;
                if (weapon.m_shared.m_attack.m_attackStamina <= 0f)
                {
                    stamtoUse = doodadController.GetMaxStamina() * 0.05f;
                }

                if (!doodadController.HaveStamina(stamtoUse)) return;
                doodadController.UseStamina(stamtoUse);
                PlayerStartDoodadControlPatch.RidingHumanoid.StartAttack(null, false);
            }

            ProcessInput(KeyCode.Mouse0, 0, "JoyAttack"); // Left click or Right Bumper
            ProcessInput(KeyCode.Mouse1, 1, "JoyBlock"); // Right click or Left Bumper
            ProcessInput(KeyCode.Mouse2, 2, "JoySecondaryAttack"); // Middle click or Right Trigger
        }
    }
}