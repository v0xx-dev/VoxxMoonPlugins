using BepInEx;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace ArcadiaMoonPlugin
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class ArcadiaMoon : BaseUnityPlugin
    {
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
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