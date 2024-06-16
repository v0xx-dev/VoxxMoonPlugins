using BepInEx;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using UnityEngine.Rendering;
using HarmonyLib;
using ArcadiaMoonPlugin.Patches;
using GameNetcodeStuff;
using System.Linq;
using System.Collections;
using UnityEditor.VersionControl;
using BepInEx.Configuration;
using UnityEngine.PlayerLoop;

namespace ArcadiaMoonPlugin
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class ArcadiaMoon : BaseUnityPlugin
    {
        private Harmony harmony;
        public static ArcadiaMoon instance;

        public static ConfigEntry<bool> ForceSpawnFlowerman { get; private set; }
        public static ConfigEntry<bool> ForceSpawnBaboon { get; private set; }
        public static ConfigEntry<bool> ForceSpawnRadMech { get; private set; }

        private void Awake()
        {
            instance = this;

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            // Configuration entries
            ForceSpawnFlowerman = Config.Bind("Spawning", "ForceSpawnFlowerman", true, "Enable forced spawning for Flowerman");
            ForceSpawnBaboon = Config.Bind("Spawning", "ForceSpawnBaboon", true, "Enable forced spawning for Baboon hawk");
            ForceSpawnRadMech = Config.Bind("Spawning", "ForceSpawnRadMech", true, "Enable forced spawning for Old Bird");

            //Apply Harmony patch
            this.harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            this.harmony.PatchAll();
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} patched PlayerControllerB!");
        }

    }

    public class TimeAnimSyncronizer : MonoBehaviour
    {
        private Animator timeSyncAnimator;

        private void Start()
        {
            timeSyncAnimator = GetComponent<Animator>();
            if (timeSyncAnimator == null)
            {
                Debug.LogError("There is no Animator component attached to this object!");
            }

        }

        private void Update()
        {
            if (timeSyncAnimator != null)
            {
                timeSyncAnimator.SetFloat("timeOfDay", Mathf.Clamp(TimeOfDay.Instance.normalizedTimeOfDay, 0f, 0.99f));
            }
        }
    }

    internal class HeatwaveZoneInteract : MonoBehaviour
    {
        public float timeInZoneMax = 10f; // Time before maximum effects are applied
        public float resetDuration = 5f; // Duration over which to gradually reduce the heat severity
        public Volume exhaustionFilter; // Filter for visual effects

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                PlayerControllerB playerController = other.gameObject.GetComponent<PlayerControllerB>();

                if (playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    PlayerHeatEffects.OnPlayerEnterZone();
                }
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                PlayerControllerB playerController = other.gameObject.GetComponent<PlayerControllerB>();

                if (playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    if (playerController.isPlayerDead || playerController.beamUpParticle.isPlaying)
                    {
                        PlayerHeatEffects.OnPlayerNearDeathOrTeleporting(exhaustionFilter, resetDuration);
                    }
                    else
                    {
                        PlayerHeatEffects.IncreaseEffects(timeInZoneMax, exhaustionFilter);
                    }
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                PlayerControllerB playerController = other.gameObject.GetComponent<PlayerControllerB>();

                if (playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    PlayerHeatEffects.OnPlayerExitZone(exhaustionFilter, resetDuration);
                }
            }
        }

        private void LateUpdate()
        {
            float severity = Mathf.Clamp01(PlayerHeatEffects.GetExhaustionTimer() / timeInZoneMax);
            float playerSeverity = PlayerHeatEffects.GetHeatSeverity();
            /*Debug.Log($"Calculated severity: {severity}, Player severity: {playerSeverity}," +
                $" timeInZone: {PlayerHeatEffects.GetExhaustionTimer()}, colliders #{PlayerHeatEffects.GetColliderCount()}");*/
        }

        private void OnDestroy()
        {
            PlayerHeatEffects.OnZoneDestroy(exhaustionFilter);
        }
    }

    internal class PlayerHeatEffects : MonoBehaviour
    {
        private static float heatSeverity = 0f;
        private static float exhaustionTimer = 0f;
        private static int colliderCount = 0;
        private static Coroutine resetCoroutine;

        internal static void OnPlayerEnterZone()
        {
            colliderCount++;
            if (colliderCount == 1 && resetCoroutine != null)
            {
                StopResetCoroutine();
            }
        }

        internal static void OnPlayerNearDeathOrTeleporting(Volume exhaustionFilter, float resetDuration)
        {
            if (resetCoroutine == null)
            {
                resetCoroutine = Instance.StartCoroutine(GraduallyResetEffects(exhaustionFilter, resetDuration));
                colliderCount = 0;
            }
        }

        internal static void IncreaseEffects(float timeInZoneMax, Volume exhaustionFilter)
        {
            exhaustionTimer += Time.deltaTime;
            float severity = Mathf.Clamp01(exhaustionTimer / timeInZoneMax);

            if (resetCoroutine != null)
            {
                StopResetCoroutine();
            }

            SetHeatSeverity(severity, exhaustionFilter);
        }

        internal static void OnPlayerExitZone(Volume exhaustionFilter, float resetDuration)
        {
            colliderCount = Mathf.Max(colliderCount - 1, 0);
            if (colliderCount == 0 && resetCoroutine == null)
            {
                resetCoroutine = Instance.StartCoroutine(GraduallyResetEffects(exhaustionFilter, resetDuration));
            }
        }

        internal static void OnZoneDestroy(Volume exhaustionFilter)
        {
            if (heatSeverity > 0f && resetCoroutine == null)
            {
                SetHeatSeverity(0f, exhaustionFilter);
                colliderCount = 0;
                exhaustionTimer = 0f;
            }
        }

        private static void StopResetCoroutine()
        {
            Instance.StopCoroutine(resetCoroutine);
            resetCoroutine = null;
        }

        private static IEnumerator GraduallyResetEffects(Volume exhaustionFilter, float resetDuration)
        {
            float startSeverity = heatSeverity;
            float elapsedTime = 0f;

            while (elapsedTime < resetDuration && startSeverity > 0)
            {
                elapsedTime += Time.deltaTime;
                float newSeverity = Mathf.Lerp(startSeverity, 0f, elapsedTime / resetDuration);
                SetHeatSeverity(newSeverity, exhaustionFilter);

                yield return null;
            }

            exhaustionTimer = 0f;
            SetHeatSeverity(0f, exhaustionFilter);
        }

        internal static void SetExhaustionTimer(float timeInZone)
        {
            exhaustionTimer = timeInZone;
        }

        public static float GetExhaustionTimer()
        {
            return exhaustionTimer;
        }

        internal static void SetHeatSeverity(float severity, Volume volume)
        {
            heatSeverity = severity;
            if (volume != null)
            {
                volume.weight = Mathf.Clamp01(severity); // Adjust intensity of the visual effect
            }
        }

        public static float GetHeatSeverity()
        {
            return heatSeverity;
        }

        public static int GetColliderCount()
        {
            return colliderCount;
        }

        private static PlayerHeatEffects instance;
        public static PlayerHeatEffects Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new GameObject("PlayerHeatEffects").AddComponent<PlayerHeatEffects>();
                }
                return instance;
            }
        }
    }


    public class EnemySpawner : NetworkBehaviour
    {
        public string enemyName = "RadMech";

        private EnemyType enemyType;
        private GameObject nestPrefab;

        public float timer = 0.5f; // Normalized time of day to start spawning enemies
        private List<GameObject> spawnedNests = new List<GameObject>();
        private System.Random random = new System.Random(StartOfRound.Instance.randomMapSeed + 42);

        private void LoadResources(string enemyName)
        {
            // Find all EnemyType assets
            var allEnemyTypes = Resources.FindObjectsOfTypeAll<EnemyType>().Distinct();

            // Find the specific EnemyType by name
            enemyType = allEnemyTypes.FirstOrDefault(e => e.enemyName == enemyName);

            if (enemyType != null)
            {
                nestPrefab = enemyType.nestSpawnPrefab;
                Debug.Log($"{enemyType.enemyName} and its prefab loaded successfully!");
            }
            else
            {
                Debug.LogError("Failed to load EnemyType!");

            }
        }

        private void Start()
        {
            if (!base.IsServer)
            {
                return;
            }
            LoadResources(enemyName);

            // Check if forced spawning is enabled for the current enemy type
            if (!IsSpawningEnabled())
            {
                Debug.Log($"Forced spawning for {enemyName} is disabled in the config.");
                enabled = false;
                return;
            }
            // Spawn nests at the positions of child objects
            foreach (Transform child in transform)
            {
                if (nestPrefab != null)
                {
                    Debug.Log("Started nest prefab spawning routine!");
                    Vector3 position = RoundManager.Instance.GetRandomNavMeshPositionInBoxPredictable(child.position, 10f, default(NavMeshHit), random,
                                                                                                      RoundManager.Instance.GetLayermaskForEnemySizeLimit(enemyType));
                    position = RoundManager.Instance.PositionEdgeCheck(position, enemyType.nestSpawnPrefabWidth);
                    GameObject nest = Instantiate(nestPrefab, position, Quaternion.identity);
                    nest.transform.Rotate(Vector3.up, random.Next(-180, 180), Space.World);
                    spawnedNests.Add(nest);
                    if (nest.GetComponentInChildren<NetworkObject>())
                    {
                        nest.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
                        Debug.Log($"Spawned an {enemyName} nest prefab!");
                    }
                    else
                    {
                        Debug.LogError($"Nest prefab of {enemyName} does not have a NetworkObject component. Desync possible!");
                    }
                }
            }
        }

        private void Update()
        {
            if (TimeOfDay.Instance.normalizedTimeOfDay > timer && TimeOfDay.Instance.timeHasStarted && base.IsServer)
            {
                // Destroy previously spawned nests and spawn enemies in their place
                if (nestPrefab != null)
                {
                    foreach (GameObject nest in spawnedNests)
                    {
                        Vector3 nest_position = nest.transform.position;
                        float nest_angle = nest.transform.rotation.eulerAngles.y;
                        Destroy(nest);
                        SpawnEnemyAtPosition(nest_position, nest_angle);
                        Debug.Log($"Spawned enemy {enemyName} in place of a nest prefab!");
                    }
                    spawnedNests.Clear();
                    Debug.Log($"Destroyed all spawned enemy nest prefabs of {enemyType.enemyName}!");
                }
                else
                {
                    foreach (Transform child in transform)
                    {
                        SpawnEnemyAtPosition(child.position, 0f);
                        Debug.Log("Force spawned an enemy!");
                    }
                }
                enabled = false;
            }
        }

        private void SpawnEnemyAtPosition(Vector3 position, float yRot = 0f)
        {
            Debug.Log($"Current enemy type for force spawn is {enemyType.enemyName}");
            if (enemyType.enemyPrefab == null)
            {
                Debug.LogError($"{enemyType.enemyName} does not have a valid enemy prefab to spawn.");
                return;
            }
            RoundManager.Instance.SpawnEnemyGameObject(position, yRot, -1, enemyType);
        }

        private bool IsSpawningEnabled()
        {
            switch (enemyName.ToLower())
            {
                case "flowerman":
                    return ArcadiaMoon.ForceSpawnFlowerman.Value;
                case "baboon hawk":
                    return ArcadiaMoon.ForceSpawnBaboon.Value;
                case "radmech":
                    return ArcadiaMoon.ForceSpawnRadMech.Value;
                default:
                    return true; // Default to true if the enemy type is not explicitly handled
            }
        }
    }
}