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

    public class HeatwaveZoneInteract : MonoBehaviour
    {
        public float timeInZoneMax = 10f; // Maximum time before maximum effects are applied
        public float resetDuration = 5f; // Duration over which to gradually reduce the heat severity
        public Volume exhaustionFilter; // Filter for visual effects

        private float timeInZone = 0f;
        private int colliderCount = 0; // Counter to track how many colliders are currently being triggered
        private Coroutine resetCoroutine;
        private PlayerControllerB playerController;

        private void Start()
        {
            playerController = FindObjectOfType<PlayerControllerB>();
            if (playerController == null)
            {
                Debug.LogError("Failed to obtain player controller!");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                // Increase the collider count
                colliderCount++;

                // If this is the first collider being triggered, stop any ongoing reset coroutine
                if (colliderCount == 1)
                {
                    Debug.Log("Player has entered a heatwave zone!");
                    if (resetCoroutine != null)
                    {
                        StopCoroutine(resetCoroutine);
                        resetCoroutine = null;
                    }

                    // Reset time in zone
                    timeInZone = PlayerHeatEffects.GetHeatSeverity() * timeInZoneMax; //recalculate timeInZone based on severity
                }
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                // Increment the time counter while the player is in the zone
                timeInZone += Time.deltaTime;

                // Adjust the severity of effects based on the time spent in the zone
                IncreaseEffects();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                // Decrease the collider count
                colliderCount--;

                // If there are no more colliders being triggered, start the reset coroutine
                if (colliderCount == 0)
                {
                    Debug.Log("Player has left a heatwave zone!");
                    resetCoroutine = StartCoroutine(GraduallyResetEffects());
                }
            }
        }

        private void IncreaseEffects()
        {
            // Calculate the severity based on the time spent in the zone
            float severity = Mathf.Clamp01(timeInZone / timeInZoneMax);
            // Check if the player's health is low and we are not already resetting the effect
            if (playerController.health <= 5 || playerController.beamOutParticle.isPlaying)
            {
                if (resetCoroutine == null)
                {
                    Debug.Log("Player close to death or teleporting, removing heatstroke!");
                    resetCoroutine = StartCoroutine(GraduallyResetEffects());
                }
            }
            else
            {
                // If player's health is not low and the resetCoroutine is running, stop it
                if (resetCoroutine != null)
                {
                    StopCoroutine(resetCoroutine);
                    resetCoroutine = null;
                }
                // Update the heat severity and the Volume weight
                PlayerHeatEffects.SetHeatSeverity(severity, exhaustionFilter);
            }
        }


        private IEnumerator GraduallyResetEffects()
        {
            float startSeverity = PlayerHeatEffects.GetHeatSeverity();
            float elapsedTime = 0f;

            while (elapsedTime < resetDuration && startSeverity > 0)
            {
                elapsedTime += Time.deltaTime;
                float newSeverity = Mathf.Lerp(startSeverity, 0f, elapsedTime / resetDuration);
                PlayerHeatEffects.SetHeatSeverity(newSeverity, exhaustionFilter);

                yield return null;
            }

            PlayerHeatEffects.SetHeatSeverity(0f, exhaustionFilter);
        }
    }

    // Class for managing heat severity
    public static class PlayerHeatEffects
    {
        private static float heatSeverity = 0f;

        public static void SetHeatSeverity(float severity, Volume volume)
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
    }


    public class EnemySpawner : MonoBehaviour
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
                Debug.Log("EnemyType and prefab loaded successfully!");
            }
            else
            {
                Debug.LogError("Failed to load EnemyType!");

            }
        }

        private void Start()
        {
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
                        Debug.Log("Spawned an enemy nest prefab!");
                    }
                    else
                    {
                        Debug.LogError("Nest prefab does not have a NetworkObject component. Desync possible!");
                    }
                }
            }
        }

        private void Update()
        {
            if (TimeOfDay.Instance.normalizedTimeOfDay > timer && TimeOfDay.Instance.timeHasStarted)
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
                        Debug.Log("Spawned enemy in place of a nest prefab!");
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