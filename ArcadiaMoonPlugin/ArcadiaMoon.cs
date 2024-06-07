using BepInEx;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using HarmonyLib;
using ArcadiaMoonPlugin.Patches;

namespace ArcadiaMoonPlugin
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class ArcadiaMoon : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            harmony.PatchAll(typeof(PlayerControllerBPatch));
        }

        private void OnDestroy()
        {
            // Unpatch all Harmony patches when the plugin is destroyed
            harmony.UnpatchSelf();
        }
    }

    public class TimeAnimSyncronizer : MonoBehaviour
    {
        private Animator timeSyncAnimator;

        private void Start()
        {
            timeSyncAnimator = GetComponent<Animator>();
            if (timeSyncAnimator != null)
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
        private float timeInZone = 0f;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                timeInZone = 0f;
                Debug.Log("The player has entered a heatwave zone!");
                // Start tracking time in zone
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                timeInZone += Time.deltaTime;
                AdjustEffects(other.gameObject);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                Debug.Log($"The player has left a heatwave zone after {timeInZone} seconds!");
                timeInZone = 0f;
                ResetEffects(other.gameObject);
            }
        }

        private void AdjustEffects(GameObject player)
        {
            float severity = Mathf.Clamp01(timeInZone / timeInZoneMax);
            // Apply severity effects (for example, changing color intensity)
            //Renderer playerRenderer = player.GetComponent<Renderer>();
            //if (playerRenderer != null)
            //{
            //    playerRenderer.material.color = new Color(severity, 0, 0);
            //}

            // Set the static field to be used in the Harmony patch
            PlayerHeatEffects.SetHeatSeverity(severity);
        }

        private void ResetEffects(GameObject player)
        {
            // Reset the player's color when exiting the zone
            //Renderer playerRenderer = player.GetComponent<Renderer>();
            //if (playerRenderer != null)
            //{
            //    playerRenderer.material.color = Color.white;
            //}

            // Reset the static field
            PlayerHeatEffects.SetHeatSeverity(0f);
        }
    }


    public static class PlayerHeatEffects
    {
        private static float heatSeverity = 0f;

        public static void SetHeatSeverity(float severity)
        {
            heatSeverity = severity;
        }

        public static float GetHeatSeverity()
        {
            return heatSeverity;
        }
    }

    public class EnemySpawner : MonoBehaviour
    {
        public EnemyType enemyType;
        public GameObject nestPrefab;
        public float timer = 0.5f; // Normalized time of day to start spawning enemies
        private List<GameObject> spawnedNests = new List<GameObject>();
        private System.Random random = new System.Random(StartOfRound.Instance.randomMapSeed + 42);

        private void Start()
        {
            // Spawn nests at the positions of child objects
            Debug.Log("Started nest prefab spawning routine!");
            foreach (Transform child in transform)
            {
                if (nestPrefab != null)
                {
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
            if (TimeOfDay.Instance.normalizedTimeOfDay > timer)
            {
                // Destroy previously spawned nests and spawn enemies in their place
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

                enabled = false;
            }
        }

        private void SpawnEnemyAtPosition(Vector3 position, float yRot = 0f)
        {
            Debug.Log($"Current enemy type for force spawn is {enemyType.enemyName}");
            RoundManager.Instance.SpawnEnemyGameObject(position, yRot, -1, enemyType);
        }
    }
}

namespace ArcadiaMoonPlugin.Patches
{

}