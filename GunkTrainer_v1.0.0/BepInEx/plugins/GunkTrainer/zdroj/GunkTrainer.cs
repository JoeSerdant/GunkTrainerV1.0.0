// Gunk Trainer - BepInEx 5 plugin pro hru Gunk (RyeGames, Unity 2022.3 / Mono)
// Pouze pro singleplayer. Hra nema multiplayer ani online zebricky.
//
// Preklad:  viz build.sh (mcs, reference na Gunk_Data/Managed + BepInEx/core)

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GunkTrainer
{
    // ------------------------------------------------------------------
    //  Sdileny stav cheatu
    // ------------------------------------------------------------------
    internal static class Cheats
    {
        public static bool MenuOpen;

        // hrac
        public static bool GodMode;
        public static float SpeedMult = 1f;
        public static float JumpMult = 1f;
        public static bool AirJump;

        // kameny
        public static bool OneHitKill;
        public static float DamageMult = 1f;
        public static bool NoCooldown;
        public static bool ExplosiveRocks;

        // level
        public static float XpMult = 1f;
        public static bool XpBypass;          // interni: "Level up" tlacitko se nenasobi

        // nepratele
        public static bool FreezeEnemies;
        public static bool NoSpawn;

        // hra
        public static float GameSpeed = 1f;

        // Steam achievementy: jakmile se v tomto spusteni hry pouzije cheat,
        // zustane Tainted = true az do restartu hry.
        public static bool Tainted;
        public static ConfigEntry<bool> BlockAchievements;

        public static ManualLogSource Log;
    }

    // ------------------------------------------------------------------
    //  Harmony patche (vsechny jsou "pruchozi", dokud neni cheat zapnuty)
    // ------------------------------------------------------------------
    internal static class Patches
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Vraci pocet uspesne nasazenych patchu. Kazdy patch je samostatne v try/catch,
        // aby pripadna zmena v nove verzi hry nevyradila cely trainer.
        public static int ApplyAll(Harmony harmony, Action<object> warn, Action<object> error)
        {
            int ok = 0;
            ok += Apply(harmony, typeof(PlayerHealth), "TakeDamage", "PlayerTakeDamage_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(Health), "TakeDamage", "HealthTakeDamage_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(LevelSystem), "AddExperience", "AddExperience_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(SteamAPIController), "UnlockAchievment", "UnlockAchievement_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(PlayerMovement), "OnGround", "OnGround_Postfix", true, warn, error);
            ok += Apply(harmony, typeof(Enemy), "MoveToTarget", "EnemyMove_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(GroundEnemy), "MoveToTarget", "EnemyMove_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(FlyingEnemy), "MoveToTarget", "EnemyMove_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(EnemyController), "SpawnEnemy", "SpawnEnemy_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(PlayerCameraController), "Update", "BlockWhenMenuOpen_Prefix", false, warn, error);
            ok += Apply(harmony, typeof(RockThrower), "Update", "BlockWhenMenuOpen_Prefix", false, warn, error);
            return ok;
        }

        private static int Apply(Harmony harmony, Type type, string method, string patch, bool postfix,
                                 Action<object> warn, Action<object> error)
        {
            try
            {
                MethodInfo original = type.GetMethod(method, AnyInstance);
                if (original == null)
                {
                    warn("Metoda nenalezena: " + type.Name + "." + method + " (jina verze hry?)");
                    return 0;
                }
                MethodInfo mine = typeof(Patches).GetMethod(patch, BindingFlags.Static | BindingFlags.Public);
                var hm = new HarmonyMethod(mine);
                if (postfix) harmony.Patch(original, null, hm);
                else harmony.Patch(original, hm);
                return 1;
            }
            catch (Exception e)
            {
                error("Patch " + type.Name + "." + method + " selhal: " + e);
                return 0;
            }
        }

        // PlayerHealth.TakeDamage - bez tohohle by se i pri nesmrtelnosti trasla kamera
        public static bool PlayerTakeDamage_Prefix()
        {
            return !Cheats.GodMode;
        }

        // Health.TakeDamage - spolecne pro hrace, nepratele i bosse
        public static bool HealthTakeDamage_Prefix(Health __instance, ref DamageInfo damageInfo)
        {
            if (__instance is PlayerHealth)
                return !Cheats.GodMode;

            if (__instance is EnemyHealth)
            {
                if (Cheats.DamageMult > 1.001f)
                    damageInfo.damageAmount *= Cheats.DamageMult;
                if (Cheats.OneHitKill && damageInfo.damageAmount < __instance.currentHealth)
                    damageInfo.damageAmount = __instance.currentHealth;
            }
            return true;
        }

        // LevelSystem.AddExperience
        public static void AddExperience_Prefix(ref int amount)
        {
            if (Cheats.XpBypass) return;
            if (Cheats.XpMult > 1.001f)
                amount = Mathf.Max(1, Mathf.RoundToInt(amount * Cheats.XpMult));
        }

        // SteamAPIController.UnlockAchievment - vsechny achievementy jdou pres tuhle metodu
        public static bool UnlockAchievement_Prefix()
        {
            if (Cheats.Tainted && Cheats.BlockAchievements != null && Cheats.BlockAchievements.Value)
                return false;
            return true;
        }

        // PlayerMovement.OnGround
        public static void OnGround_Postfix(ref bool __result)
        {
            if (Cheats.AirJump) __result = true;
        }

        // Enemy / GroundEnemy / FlyingEnemy . MoveToTarget
        public static bool EnemyMove_Prefix(Enemy __instance)
        {
            if (!Cheats.FreezeEnemies) return true;

            // Bez tohohle by nepritel jeste chvili klouzal setrvacnosti (letajici porad).
            // Svisla rychlost zustava, aby padajici nepratele normalne dopadli.
            Rigidbody rb = __instance.rb;
            if (rb && !rb.isKinematic)
            {
                Vector3 v = rb.velocity;
                rb.velocity = rb.useGravity ? new Vector3(0f, v.y, 0f) : Vector3.zero;
            }
            return false;
        }

        // EnemyController.SpawnEnemy
        public static bool SpawnEnemy_Prefix()
        {
            return !Cheats.NoSpawn;
        }

        // PlayerCameraController.Update + RockThrower.Update:
        // kdyz je otevrene menu traineru, mys nema otacet kamerou ani hazet kameny
        public static bool BlockWhenMenuOpen_Prefix()
        {
            return !Cheats.MenuOpen;
        }
    }

    // ------------------------------------------------------------------
    //  Plugin (vstupni bod pro BepInEx)
    // ------------------------------------------------------------------
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class TrainerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "gunk.trainer";
        public const string PluginName = "Gunk Trainer";
        public const string PluginVersion = "1.0.0";

        internal static ConfigEntry<KeyCode> KeyMenu, KeyGod, KeyOneHit, KeyNoCooldown, KeyExplosive,
            KeyLevelUp, KeyMaxUpgrades, KeyKillAll, KeyFreeze, KeyHeal;

        private Harmony harmony;

        private void Awake()
        {
            Cheats.Log = Logger;

            KeyMenu        = Config.Bind("Klavesy", "Menu", KeyCode.F1, "Otevrit / zavrit menu traineru");
            KeyGod         = Config.Bind("Klavesy", "Nesmrtelnost", KeyCode.F2, "");
            KeyOneHit      = Config.Bind("Klavesy", "ZabitiJednouRanou", KeyCode.F3, "");
            KeyNoCooldown  = Config.Bind("Klavesy", "BezCooldownu", KeyCode.F4, "");
            KeyExplosive   = Config.Bind("Klavesy", "VybusneKameny", KeyCode.F5, "");
            KeyLevelUp     = Config.Bind("Klavesy", "LevelUp", KeyCode.F6, "");
            KeyMaxUpgrades = Config.Bind("Klavesy", "VymaxovatUpgrady", KeyCode.F7, "");
            KeyKillAll     = Config.Bind("Klavesy", "ZabitVsechnyNepratele", KeyCode.F8, "");
            KeyFreeze      = Config.Bind("Klavesy", "ZmrazitNepratele", KeyCode.F9, "");
            KeyHeal        = Config.Bind("Klavesy", "DoplnitZdravi", KeyCode.F10, "");

            Cheats.BlockAchievements = Config.Bind("Obecne", "BlokovatSteamAchievementy", true,
                "Kdyz je zapnuto, hra po prvnim pouziti cheatu az do restartu neodemyka Steam achievementy.");

            harmony = new Harmony(PluginGuid);
            Patches.ApplyAll(harmony, Logger.LogWarning, Logger.LogError);

            // Vlastni skryty objekt - prezije zmenu sceny i pripadne zniceni BepInEx manageru.
            var go = new GameObject("GunkTrainer");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<TrainerBehaviour>();

            Logger.LogInfo(PluginName + " " + PluginVersion + " nacten. Menu: " + KeyMenu.Value);
        }
    }

    // ------------------------------------------------------------------
    //  Menu, klavesove zkratky a cheaty, ktere bezi kazdy snimek
    // ------------------------------------------------------------------
    internal class TrainerBehaviour : MonoBehaviour
    {
        private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly FieldInfo fJumpForce = typeof(PlayerMovement).GetField("jumpForce", Priv);
        private static readonly FieldInfo fCanLook = typeof(PlayerCameraController).GetField("canLook", Priv);
        private static readonly FieldInfo fBossBarHealth = typeof(BossHealthBar).GetField("health", Priv);
        private static readonly MethodInfo mSetKillCount = GetKillCountSetter();

        private PlayerHealth playerHealth;
        private PlayerMovement movement;
        private RockThrower thrower;
        private PlayerCameraController cameraCtl;
        private float nextScan;

        private PlayerMovement trackedMovement;
        private float origWalk, origSprint, origJump;
        private bool movementTouched;
        private bool speedTouched;

        private Rect window = new Rect(20f, 20f, 420f, 850f);
        private Vector2 scroll;
        private GUIStyle headerStyle, toastStyle, smallStyle, windowStyle;
        private Texture2D windowBg, toastBg;
        private string toast = "";
        private float toastUntil;
        private readonly List<UpgradeInstance> upgradeBuffer = new List<UpgradeInstance>();
        private readonly HashSet<string> loggedErrors = new HashSet<string>();

        private static MethodInfo GetKillCountSetter()
        {
            PropertyInfo p = typeof(DamageDealer).GetProperty("killCount", Priv);
            return p != null ? p.GetSetMethod(true) : null;
        }

        private void Start()
        {
            Toast("Gunk Trainer načten  –  " + TrainerPlugin.KeyMenu.Value + " = menu", 8f);
        }

        // ---------------- kazdy snimek ----------------

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { LogOnce(e); }
        }

        private void LateUpdate()
        {
            if (Cheats.MenuOpen) ShowCursor();
        }

        private void Tick()
        {
            ScanScene();
            HandleHotkeys();
            ApplyMovement();
            ApplyPowerUps();
            ApplyGameSpeed();
            if (Cheats.MenuOpen) ShowCursor();
        }

        private void ScanScene()
        {
            if (playerHealth && movement && thrower && cameraCtl) return;
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;

            if (!playerHealth) playerHealth = FindObjectOfType<PlayerHealth>();
            if (!movement) movement = FindObjectOfType<PlayerMovement>();
            if (!thrower) thrower = FindObjectOfType<RockThrower>();
            if (!cameraCtl) cameraCtl = FindObjectOfType<PlayerCameraController>();
        }

        private bool InGame
        {
            get { return playerHealth && playerHealth.currentHealth > 0f; }
        }

        private void HandleHotkeys()
        {
            if (Down(TrainerPlugin.KeyMenu)) SetMenu(!Cheats.MenuOpen);

            if (Down(TrainerPlugin.KeyGod))
            {
                Cheats.GodMode = !Cheats.GodMode;
                Flag("Nesmrtelnost", Cheats.GodMode);
            }
            if (Down(TrainerPlugin.KeyOneHit))
            {
                Cheats.OneHitKill = !Cheats.OneHitKill;
                Flag("Zabití jednou ranou", Cheats.OneHitKill);
            }
            if (Down(TrainerPlugin.KeyNoCooldown))
            {
                Cheats.NoCooldown = !Cheats.NoCooldown;
                Flag("Házení bez cooldownu", Cheats.NoCooldown);
            }
            if (Down(TrainerPlugin.KeyExplosive))
            {
                Cheats.ExplosiveRocks = !Cheats.ExplosiveRocks;
                Flag("Výbušné kameny", Cheats.ExplosiveRocks);
            }
            if (Down(TrainerPlugin.KeyFreeze))
            {
                Cheats.FreezeEnemies = !Cheats.FreezeEnemies;
                Flag("Zmrazení nepřátel", Cheats.FreezeEnemies);
            }
            if (Down(TrainerPlugin.KeyLevelUp)) LevelUp();
            if (Down(TrainerPlugin.KeyMaxUpgrades)) MaxAllUpgrades();
            if (Down(TrainerPlugin.KeyKillAll)) KillEnemies(false);
            if (Down(TrainerPlugin.KeyHeal)) Heal();
        }

        private static bool Down(ConfigEntry<KeyCode> key)
        {
            return key != null && key.Value != KeyCode.None && Input.GetKeyDown(key.Value);
        }

        private void Flag(string name, bool on)
        {
            if (on) Cheats.Tainted = true;
            Toast(name + ": " + (on ? "ZAPNUTO" : "vypnuto"), 2f);
        }

        // ---------------- pohyb ----------------

        private void ApplyMovement()
        {
            if (!movement) { trackedMovement = null; return; }

            if (trackedMovement != movement)
            {
                // novy hrac (nova scena) -> zapamatovat puvodni hodnoty
                trackedMovement = movement;
                origWalk = movement.walkSpeed;
                origSprint = movement.sprintSpeed;
                origJump = fJumpForce != null ? (float)fJumpForce.GetValue(movement) : 0f;
                movementTouched = false;
            }

            bool wanted = Cheats.SpeedMult > 1.001f || Cheats.JumpMult > 1.001f;
            if (!wanted && !movementTouched) return;

            movement.walkSpeed = origWalk * Cheats.SpeedMult;
            movement.sprintSpeed = origSprint * Cheats.SpeedMult;
            if (fJumpForce != null) fJumpForce.SetValue(movement, origJump * Cheats.JumpMult);
            movementTouched = wanted;   // po vraceni na 1x se hodnoty jeste jednou zapisou a pak uz ne
        }

        // ---------------- power-upy ----------------

        private void ApplyPowerUps()
        {
            if (!InGame) return;
            if (Cheats.ExplosiveRocks) KeepPowerUp(PowerUpTypes.ExplosiveRocks);
            if (Cheats.NoCooldown) KeepPowerUp(PowerUpTypes.NoThrowCooldown);
        }

        private void KeepPowerUp(PowerUpTypes type)
        {
            PowerUpManager mgr = PowerUpManager.Instance;
            if (!mgr || mgr.powerUps == null) return;

            for (int i = 0; i < mgr.powerUps.Count; i++)
            {
                PowerUp p = mgr.powerUps[i];
                if (p == null || p.powerUpData == null || p.powerUpData.type != type) continue;

                if (!p.isActive)
                {
                    // pres hru, aby se ukazala i ikona power-upu v HUD
                    try { mgr.ActivatePowerUp(type); }
                    catch (Exception e) { LogOnce(e); }
                    p.isActive = true;
                }
                p.timer = p.powerUpData.duration > 0f ? p.powerUpData.duration : 10f;
            }
        }

        // ---------------- rychlost hry ----------------

        private void ApplyGameSpeed()
        {
            // jen behem hrani; po smrti / v hlavnim menu se rychlost vraci na 1x
            bool wanted = InGame && Mathf.Abs(Cheats.GameSpeed - 1f) > 0.001f;

            // timeScale == 0 znamena pauzu nebo menu upgradu - do toho nesahat
            if (wanted)
            {
                if (Time.timeScale > 0f && Mathf.Abs(Time.timeScale - Cheats.GameSpeed) > 0.001f)
                    Time.timeScale = Cheats.GameSpeed;
                speedTouched = true;
            }
            else if (speedTouched)
            {
                if (Time.timeScale > 0f) Time.timeScale = 1f;
                speedTouched = false;
            }
        }

        // ---------------- kurzor / menu ----------------

        private void SetMenu(bool open)
        {
            Cheats.MenuOpen = open;
            if (open)
            {
                ShowCursor();
            }
            else if (GameWantsLockedCursor())
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static void ShowCursor()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        // true = bezi normalni hrani (zadna pauza, menu upgradu, game over, hlavni menu)
        private bool GameWantsLockedCursor()
        {
            if (!cameraCtl) return false;
            if (Time.timeScale <= 0f) return false;
            if (MenuManager.instance && MenuManager.instance.cursorActive) return false;
            if (!InGame) return false;
            try
            {
                if (fCanLook != null && !(bool)fCanLook.GetValue(cameraCtl)) return false;
            }
            catch (Exception e) { LogOnce(e); }
            return true;
        }

        // ---------------- jednorazove akce ----------------

        private void Heal()
        {
            if (!playerHealth) { Toast("Nejsi ve hře", 2f); return; }
            Cheats.Tainted = true;
            playerHealth.currentHealth = playerHealth.maxHealth;
            try
            {
                if (playerHealth.OnRegeneration != null) playerHealth.OnRegeneration.Invoke(); // prekresli HUD
            }
            catch (Exception e) { LogOnce(e); }
            Toast("Zdraví doplněno", 2f);
        }

        private void LevelUp()
        {
            LevelSystem ls = LevelSystem.Instance;
            if (!ls || !InGame) { Toast("Nejsi ve hře", 2f); return; }

            LevelData d = ls.GetLevelData();
            if (d.isMaxLevel) { Toast("Už máš maximální level", 2f); return; }

            Cheats.Tainted = true;
            Cheats.XpBypass = true;
            try { ls.AddExperience(Mathf.Max(1, d.experienceToLevelUp - d.experience)); }
            catch (Exception e) { LogOnce(e); }
            finally { Cheats.XpBypass = false; }
        }

        private void MaxAllUpgrades()
        {
            PlayerStats ps = PlayerStats.Instance;
            if (!ps || ps.upgradeInstances == null) { Toast("Nejsi ve hře", 2f); return; }

            Cheats.Tainted = true;
            var list = new List<UpgradeInstance>(ps.upgradeInstances.Values);
            int total = 0;
            foreach (UpgradeInstance u in list)
            {
                if (u == null || u.data == null) continue;
                int guard = 0;
                while (u.currentLevel < u.data.maxLevel && guard++ < 500)
                {
                    int before = u.currentLevel;
                    try { u.LevelUp(); }
                    catch (Exception e) { LogOnce(e); }
                    if (u.currentLevel == before) break;
                    total++;
                }
            }
            Toast("Upgrady vymaxovány (+" + total + " úrovní)", 2.5f);
        }

        private void UpgradeOnce(UpgradeInstance u)
        {
            Cheats.Tainted = true;
            try { u.LevelUp(); }
            catch (Exception e) { LogOnce(e); }
        }

        // Hra pri smrti nepritele saha na damageInfo.damageDealer, takze musi byt platny.
        private DamageDealer FindDealer()
        {
            if (thrower && thrower.rockPool != null)
            {
                foreach (GameObject rock in thrower.rockPool)
                {
                    if (!rock) continue;
                    DamageDealer d = rock.GetComponent<DamageDealer>();
                    if (d) return d;
                }
            }
            return FindObjectOfType<DamageDealer>();
        }

        // Boss vznika z prefabu EnemyController.bossGunk; pro jistotu se bere i to,
        // na co ukazuje BossHealthBar. (Na seznam EnemyController.enemies se spolehnout neda -
        // hra do nej kazdeho nepritele vklada dvakrat a bosse taky.)
        private HashSet<EnemyHealth> FindBosses(EnemyHealth[] all)
        {
            var set = new HashSet<EnemyHealth>();
            EnemyController ec = EnemyController.Instance;
            string bossName = ec && ec.bossGunk ? ec.bossGunk.name + "(Clone)" : null;
            if (bossName != null)
            {
                foreach (EnemyHealth h in all)
                    if (h && h.gameObject.name == bossName) set.Add(h);
            }
            if (fBossBarHealth != null)
            {
                foreach (BossHealthBar bar in FindObjectsOfType<BossHealthBar>())
                {
                    EnemyHealth h = fBossBarHealth.GetValue(bar) as EnemyHealth;
                    if (h) set.Add(h);
                }
            }
            return set;
        }

        private void KillEnemies(bool bosses)
        {
            if (!InGame) { Toast("Nejsi ve hře", 2f); return; }

            DamageDealer dealer = FindDealer();
            if (!dealer) { Toast("Nenašel jsem kámen, kterým zabíjet – zkus to za chvíli", 3f); return; }

            Cheats.Tainted = true;
            EnemyHealth[] all = FindObjectsOfType<EnemyHealth>();
            HashSet<EnemyHealth> bossSet = FindBosses(all);
            int savedKills = dealer.killCount;
            int killed = 0;

            foreach (EnemyHealth h in all)
            {
                if (!h || !h.isDamageable || h.currentHealth <= 0f) continue;
                if (bossSet.Contains(h) != bosses) continue;

                try
                {
                    h.TakeDamage(new DamageInfo(h.currentHealth + 1f, false, h.transform.position, dealer));
                    killed++;
                }
                catch (Exception e) { LogOnce(e); }
            }

            // vratit pocitadlo zabiti na kameni (hra z nej pocita "double kill")
            try
            {
                if (mSetKillCount != null && dealer) mSetKillCount.Invoke(dealer, new object[] { savedKills });
            }
            catch (Exception e) { LogOnce(e); }

            if (bosses) Toast(killed > 0 ? "Boss zabit" : "Žádný boss tu není", 2f);
            else Toast("Zabito nepřátel: " + killed, 2f);
        }

        private void AddScore(int amount)
        {
            if (!GameMode.IsEndlessMode || !EndlessGameManager.Instance || !InGame)
            {
                Toast("Skóre jde přidat jen v Endless módu", 2.5f);
                return;
            }
            Cheats.Tainted = true;
            try { EndlessGameManager.Instance.IncreaseScore(amount); }
            catch (Exception e) { LogOnce(e); }
        }

        private void UnlockEndless()
        {
            Cheats.Tainted = true;
            EndlessData.UnlockEndlessMode();
            Toast("Endless mód odemčen (tlačítko se objeví po návratu do hlavního menu)", 4f);
        }

        // ---------------- GUI ----------------

        private void OnGUI()
        {
            try { Draw(); }
            catch (Exception e) { LogOnce(e); }
        }

        private void Draw()
        {
            // textury vytvorene za behu muze Unity pri zmene sceny uklidit -> hlidat a pripadne vytvorit znovu
            if (headerStyle == null || !windowBg || !toastBg) BuildStyles();

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float sw = Screen.width / scale, sh = Screen.height / scale;

            if (Time.unscaledTime < toastUntil && toast.Length > 0)
            {
                float w = Mathf.Min(sw - 20f, toastStyle.CalcSize(new GUIContent(toast)).x + 30f);
                GUI.Box(new Rect((sw - w) / 2f, 12f, w, 28f), toast, toastStyle);
            }

            if (Cheats.MenuOpen)
            {
                window.height = Mathf.Min(850f, sh - 40f);   // na 1080p se vejde cele, na mensim rozliseni se posouva
                window = GUI.Window(0x47554E4B, window, DrawWindow, "Gunk Trainer " + TrainerPlugin.PluginVersion, windowStyle);
                window.x = Mathf.Clamp(window.x, 0f, Mathf.Max(0f, sw - window.width));
                window.y = Mathf.Clamp(window.y, 0f, Mathf.Max(0f, sh - window.height));
            }

            GUI.matrix = old;
        }

        private static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void BuildStyles()
        {
            if (!windowBg) windowBg = SolidTexture(new Color(0.07f, 0.08f, 0.10f, 0.96f));
            if (!toastBg) toastBg = SolidTexture(new Color(0f, 0f, 0f, 0.80f));

            // neprusvitne okno - vychozi IMGUI okno je pruhledne a HUD hry by pod nim rusil
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = windowBg;
            windowStyle.onNormal.background = windowBg;
            windowStyle.normal.textColor = Color.white;
            windowStyle.onNormal.textColor = Color.white;
            windowStyle.fontStyle = FontStyle.Bold;

            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = new Color(1f, 0.82f, 0.25f);

            toastStyle = new GUIStyle(GUI.skin.box);
            toastStyle.normal.background = toastBg;
            toastStyle.alignment = TextAnchor.MiddleCenter;
            toastStyle.fontStyle = FontStyle.Bold;
            toastStyle.normal.textColor = Color.white;
            toastStyle.wordWrap = false;

            smallStyle = new GUIStyle(GUI.skin.label);
            smallStyle.fontSize = 11;
            smallStyle.wordWrap = true;
            smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        }

        private void DrawWindow(int id)
        {
            // jen svisly posuvnik; obsah ma pevnou sirku, aby se nikdy neobjevil vodorovny
            scroll = GUILayout.BeginScrollView(scroll, false, true);
            GUILayout.BeginVertical(GUILayout.Width(window.width - 46f));

            // ---- HRÁČ ----
            GUILayout.Label("HRÁČ", headerStyle);
            Cheats.GodMode = Toggle(Cheats.GodMode, "Nesmrtelnost", TrainerPlugin.KeyGod);
            if (GUILayout.Button("Doplnit zdraví" + KeyHint(TrainerPlugin.KeyHeal))) Heal();
            Cheats.SpeedMult = Slider("Rychlost pohybu", Cheats.SpeedMult, 1f, 5f);
            Cheats.JumpMult = Slider("Výška skoku", Cheats.JumpMult, 1f, 5f);
            Cheats.AirJump = Toggle(Cheats.AirJump, "Skok i ve vzduchu (nekonečný skok)", null);

            // ---- KAMENY ----
            GUILayout.Space(6f);
            GUILayout.Label("KAMENY", headerStyle);
            Cheats.OneHitKill = Toggle(Cheats.OneHitKill, "Zabití jednou ranou (i boss)", TrainerPlugin.KeyOneHit);
            Cheats.DamageMult = Slider("Násobič poškození", Cheats.DamageMult, 1f, 50f);
            Cheats.NoCooldown = Toggle(Cheats.NoCooldown, "Házení bez cooldownu", TrainerPlugin.KeyNoCooldown);
            Cheats.ExplosiveRocks = Toggle(Cheats.ExplosiveRocks, "Výbušné kameny napořád", TrainerPlugin.KeyExplosive);

            // ---- LEVEL A UPGRADY ----
            GUILayout.Space(6f);
            GUILayout.Label("LEVEL A UPGRADY", headerStyle);
            if (GUILayout.Button("Level up" + KeyHint(TrainerPlugin.KeyLevelUp))) LevelUp();
            Cheats.XpMult = Slider("Násobič XP", Cheats.XpMult, 1f, 20f);
            if (GUILayout.Button("Vymaxovat všechny upgrady" + KeyHint(TrainerPlugin.KeyMaxUpgrades))) MaxAllUpgrades();
            DrawUpgrades();

            // ---- NEPŘÁTELÉ ----
            GUILayout.Space(6f);
            GUILayout.Label("NEPŘÁTELÉ", headerStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zabít všechny" + KeyHint(TrainerPlugin.KeyKillAll))) KillEnemies(false);
            if (GUILayout.Button("Zabít bosse")) KillEnemies(true);
            GUILayout.EndHorizontal();
            Cheats.FreezeEnemies = Toggle(Cheats.FreezeEnemies, "Zmrazit nepřátele", TrainerPlugin.KeyFreeze);
            Cheats.NoSpawn = Toggle(Cheats.NoSpawn, "Vypnout spawn nových nepřátel", null);

            // ---- HRA ----
            GUILayout.Space(6f);
            GUILayout.Label("HRA", headerStyle);
            Cheats.GameSpeed = Slider("Rychlost hry", Cheats.GameSpeed, 0.2f, 3f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Odemknout Endless mód")) UnlockEndless();
            if (GUILayout.Button("+10 000 skóre")) AddScore(10000);
            GUILayout.EndHorizontal();

            bool block = GUILayout.Toggle(Cheats.BlockAchievements.Value, " Po použití cheatu neodemykat Steam achievementy");
            if (block != Cheats.BlockAchievements.Value) Cheats.BlockAchievements.Value = block;

            string state;
            if (!Cheats.Tainted) state = "Zatím nebyl použit žádný cheat – achievementy fungují normálně.";
            else if (Cheats.BlockAchievements.Value) state = "Cheat použit – achievementy jsou do restartu hry blokované.";
            else state = "Cheat použit – achievementy se BUDOU odemykat i s cheaty.";
            GUILayout.Label(state, smallStyle);

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private void DrawUpgrades()
        {
            PlayerStats ps = PlayerStats.Instance;
            if (!ps || ps.upgradeInstances == null)
            {
                GUILayout.Label("(upgrady se zobrazí až ve hře)", smallStyle);
                return;
            }

            upgradeBuffer.Clear();
            upgradeBuffer.AddRange(ps.upgradeInstances.Values);

            foreach (UpgradeInstance u in upgradeBuffer)
            {
                if (u == null || u.data == null) continue;
                string name = string.IsNullOrEmpty(u.data.upgradeName) ? u.data.upgradeType.ToString() : u.data.upgradeName;

                GUILayout.BeginHorizontal();
                GUILayout.Label(name + "  –  lvl " + u.currentLevel + "/" + u.data.maxLevel
                                + "  (" + u.currentValue.ToString("0.##") + ")");
                bool was = GUI.enabled;
                GUI.enabled = was && u.currentLevel < u.data.maxLevel;
                if (GUILayout.Button("+1", GUILayout.Width(40f))) UpgradeOnce(u);
                GUI.enabled = was;
                GUILayout.EndHorizontal();
            }
        }

        // ---------------- pomocne GUI prvky ----------------

        private static string KeyHint(ConfigEntry<KeyCode> key)
        {
            return key != null && key.Value != KeyCode.None ? "  [" + key.Value + "]" : "";
        }

        private static bool Toggle(bool value, string label, ConfigEntry<KeyCode> key)
        {
            bool now = GUILayout.Toggle(value, " " + label + KeyHint(key));
            if (now && !value) Cheats.Tainted = true;
            return now;
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + value.ToString("0.0") + "×", GUILayout.Width(190f));
            GUILayout.BeginVertical();
            GUILayout.Space(8f);
            float now = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndVertical();
            if (GUILayout.Button("1×", GUILayout.Width(36f))) now = 1f;
            GUILayout.EndHorizontal();

            now = Mathf.Clamp(Mathf.Round(now * 10f) / 10f, min, max);
            if (Mathf.Abs(now - 1f) > 0.001f) Cheats.Tainted = true;
            return now;
        }

        private void Toast(string text, float seconds)
        {
            toast = text ?? "";
            toastUntil = Time.unscaledTime + seconds;
        }

        private void LogOnce(Exception e)
        {
            string key = e.GetType().Name + ":" + e.Message + ":" + (e.TargetSite != null ? e.TargetSite.Name : "");
            if (loggedErrors.Add(key) && Cheats.Log != null)
                Cheats.Log.LogError(e);
        }
    }
}
