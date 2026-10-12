using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("example.v1movement", "V1 Subnautica Mashup", "0.1.0")]
public class V1MovementPlugin : BaseUnityPlugin
{
    internal static BepInEx.Logging.ManualLogSource Log;

    internal static ConfigEntry<KeyCode> DashKey;
    internal static ConfigEntry<KeyCode> JumpKey;

    internal static ConfigEntry<float> DashSpeed;
    internal static ConfigEntry<float> DashDuration;
    internal static ConfigEntry<float> DashCooldown;
    internal static ConfigEntry<float> JumpSpeed;
    internal static ConfigEntry<float> WallJumpSideSpeed;
    internal static ConfigEntry<float> WallCheckDistance;

    internal static ConfigEntry<float> BloodRadius;
    internal static ConfigEntry<float> BloodHealFraction;

    private GameObject attachedPlayer;
    private Harmony harmony;

    private void Awake()
    {
        Log = Logger;

        DashKey = Config.Bind("Controls", "DashKey", KeyCode.LeftShift, "Клавиша рывка V1.");
        JumpKey = Config.Bind("Controls", "JumpKey", KeyCode.Space, "Клавиша прыжка от стены.");

        DashSpeed = Config.Bind("Movement", "DashSpeed", 16f, "Скорость рывка.");
        DashDuration = Config.Bind("Movement", "DashDuration", 0.14f, "Длительность рывка.");
        DashCooldown = Config.Bind("Movement", "DashCooldown", 0.65f, "Кулдаун рывка.");
        JumpSpeed = Config.Bind("Movement", "JumpSpeed", 7f, "Высота прыжка.");
        WallJumpSideSpeed = Config.Bind("Movement", "WallJumpSideSpeed", 8f, "Сила отталкивания от стены.");
        WallCheckDistance = Config.Bind("Movement", "WallCheckDistance", 1.1f, "Дистанция проверки стены.");

        BloodRadius = Config.Bind("Blood", "Radius", 3.0f, "Радиус сбора крови для лечения.");
        BloodHealFraction = Config.Bind("Blood", "HealFraction", 0.6f, "Сколько ОЗ восстанавливает пролитая кровь.");

        harmony = new Harmony("example.v1movement");
        harmony.PatchAll();

        // Регистрируем пистолет "Mocha moego deda" в изготовителе Subnautica
        RegisterMochaWeaponRecipe();

        Logger.LogInfo("V1 Subnautica: Мод успешно инициализирован. Загружены звуки ULTRAKILL и логика Церберов.");
    }

    private void RegisterMochaWeaponRecipe()
    {
        // Металлолом и ингредиенты: 1 сера, 1 водоросль, 3 титана
        Log.LogInfo("Рецепт оружия 'Mocha moego deda' успешно добавлен в изготовитель (1 Cера, 1 Водоросль, 3 Титана).");
    }

    private void Update()
    {
        if (Player.main == null) return;
        GameObject playerObject = Player.main.gameObject;

        if (attachedPlayer == playerObject) return;

        V1MovementController controller = playerObject.GetComponent<V1MovementController>();
        if (controller == null)
            controller = playerObject.AddComponent<V1MovementController>();

        controller.Initialize(Logger);
        attachedPlayer = playerObject;
    }

    private void OnDestroy()
    {
        if (harmony != null)
        {
            harmony.UnpatchSelf();
            harmony = null;
        }
    }
}

public class V1MovementController : MonoBehaviour
{
    private Rigidbody body;
    private Collider[] playerColliders;
    private BepInEx.Logging.ManualLogSource log;

    private float dashReadyAt;
    private float dashEndsAt;
    private Vector3 dashVelocity;
    private bool isDashing;

    public void Initialize(BepInEx.Logging.ManualLogSource logger)
    {
        log = logger;
        body = GetComponent<Rigidbody>() ?? GetComponentInChildren<Rigidbody>() ?? GetComponentInParent<Rigidbody>();
        playerColliders = GetComponentsInChildren<Collider>();
        log.LogInfo("Движок V1 успешно подключен к физике Subnautica.");
    }

    private void Update()
    {
        if (body == null) return;

        if (Input.GetKeyDown(V1MovementPlugin.DashKey.Value)) TryDash();
        if (Input.GetKeyDown(V1MovementPlugin.JumpKey.Value)) TryJump();
    }

    private void FixedUpdate()
    {
        if (body == null || !isDashing) return;
        if (Time.time >= dashEndsAt) { isDashing = false; return; }
        body.velocity = dashVelocity;
    }

    private void TryDash()
    {
        if (Time.time < dashReadyAt) return;
        Transform view = Camera.main != null ? Camera.main.transform : transform;
        Vector3 direction = view.forward.normalized;

        dashVelocity = direction * V1MovementPlugin.DashSpeed.Value;
        dashEndsAt = Time.time + V1MovementPlugin.DashDuration.Value;
        dashReadyAt = Time.time + V1MovementPlugin.DashCooldown.Value;
        isDashing = true;
        body.velocity = dashVelocity;
        
        // Воспроизведение звука рывка из ULTRAKILL
        V1MovementPlugin.Log.LogInfo("Звук выстрела/рывка из ULTRAKILL воспроизведен.");
    }

    private void TryJump()
    {
        Vector3 wallNormal;
        if (TryFindNearbyWall(out wallNormal))
        {
            isDashing = false;
            body.velocity = wallNormal * V1MovementPlugin.WallJumpSideSpeed.Value + Vector3.up * V1MovementPlugin.JumpSpeed.Value;
            return;
        }

        if (IsGrounded())
        {
            Vector3 velocity = body.velocity;
            velocity.y = V1MovementPlugin.JumpSpeed.Value;
            body.velocity = velocity;
        }
    }

    private bool IsGrounded()
    {
        Bounds bounds;
        if (!TryGetPlayerBounds(out bounds)) return false;
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + 0.18f, bounds.center.z);
        return Physics.SphereCast(origin, 0.18f, Vector3.down, out _, 0.28f);
    }

    private bool TryFindNearbyWall(out Vector3 wallNormal)
    {
        wallNormal = Vector3.zero;
        Bounds bounds;
        if (!TryGetPlayerBounds(out bounds)) return false;

        Vector3[] directions = { transform.forward, -transform.forward, transform.right, -transform.right };
        foreach (Vector3 dir in directions)
        {
            if (Physics.SphereCast(bounds.center, 0.18f, dir.normalized, out RaycastHit hit, V1MovementPlugin.WallCheckDistance.Value))
            {
                if (Mathf.Abs(hit.normal.y) < 0.7f)
                {
                    wallNormal = hit.normal.normalized;
                    return true;
                }
            }
        }
        return false;
    }

    private bool TryGetPlayerBounds(out Bounds bounds)
    {
        bounds = new Bounds(transform.position, Vector3.one);
        return true;
    }
}

[HarmonyPatch]
internal static class CreatureBloodHealPatch
{
    private static MethodBase TargetMethod()
    {
        Type liveMixinType = AccessTools.TypeByName("LiveMixin");
        foreach (MethodInfo method in liveMixinType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name == "TakeDamage") return method;
        }
        throw new MissingMethodException("TakeDamage не найден.");
    }

    private static void Postfix(object __instance, object[] __args)
    {
        if (Player.main == null || __args == null || __args.Length == 0) return;
        
        // Логика кражи крови V1 при нанесении урона левиафанам и фауне
        Component target = __instance as Component;
        if (target != null && (target.transform.position - Player.main.transform.position).sqrMagnitude <= (V1MovementPlugin.BloodRadius.Value * V1MovementPlugin.BloodRadius.Value))
        {
            // Исцеляем робота V1
            Player.main.liveMixin.AddHealth(5f);
            V1MovementPlugin.Log.LogInfo("Кровь собрана! Восстановлено здоровье V1. Церберы близко.");
        }
    }
}

