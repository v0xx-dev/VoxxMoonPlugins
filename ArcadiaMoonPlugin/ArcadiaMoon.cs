using BepInEx;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

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

        private void Start()
        {
            // Spawn nests at the positions of child objects
            Debug.Log("Started nest prefab spawning routine!");
            foreach (Transform child in transform)
            {
                if (nestPrefab != null)
                {
                    GameObject nest = Instantiate(nestPrefab, child.position, Quaternion.identity);
                    spawnedNests.Add(nest);
                    if (nest.GetComponentInChildren<NetworkObject>())
                    {
                        nest.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
                        Debug.Log("Spawned an enemy nest prefab!");
                    }
                    else
                    {
                        Debug.LogError("Nest prefab does not have a NetworkObject component.");
                    }
                }
            }
        }

        private void Update()
        {
            if (TimeOfDay.Instance.normalizedTimeOfDay > timer)
            {
                // Destroy previously spawned nests
                foreach (GameObject nest in spawnedNests)
                {
                    Destroy(nest);
                }
                spawnedNests.Clear();
                Debug.Log("Destroyed all spawned enemy nest prefabs!");

                // Spawn enemies
                foreach (Transform child in transform)
                {
                    SpawnEnemyAtPosition(child.position);
                    Debug.Log("Spawned enemy in place of a nest prefab!");
                }
                enabled = false;
            }
        }

        private void SpawnEnemyAtPosition(Vector3 position)
        {
            Debug.Log($"Current enemy type for force spawn is {enemyType.enemyName}");
            RoundManager.Instance.SpawnEnemyGameObject(position, 0f, -1, enemyType);
        }
    }
}