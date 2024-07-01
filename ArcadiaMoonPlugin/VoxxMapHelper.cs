﻿using BepInEx;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using UnityEngine.Rendering;
using HarmonyLib;
using GameNetcodeStuff;
using System.Linq;
using System.Collections;
using BepInEx.Configuration;
using System;

namespace VoxxMapHelperPlugin
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class VoxxMapHelper : BaseUnityPlugin
    {
        private Harmony harmony;
        public static VoxxMapHelper instance;

        public static ConfigEntry<bool> ForceSpawnFlowerman { get; private set; }
        public static ConfigEntry<bool> ForceSpawnBaboon { get; private set; }
        public static ConfigEntry<bool> ForceSpawnRadMech { get; private set; }

        private void Awake()
        {
            instance = this;

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            // Configuration entries
            ForceSpawnFlowerman = Config.Bind("Spawning", "ForceSpawnFlowerman", true, "Enable custom deterministic spawner for Flowerman");
            ForceSpawnBaboon = Config.Bind("Spawning", "ForceSpawnBaboon", true, "Enable custom deterministic spawner for Baboon hawk");
            ForceSpawnRadMech = Config.Bind("Spawning", "ForceSpawnRadMech", true, "Enable custom deterministic spawner for Old Bird");

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
            if (timeSyncAnimator != null && TimeOfDay.Instance.timeHasStarted)
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

                if (playerController != null && playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    PlayerHeatEffects.OnPlayerEnterZone(timeInZoneMax);
                }
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                PlayerControllerB playerController = other.gameObject.GetComponent<PlayerControllerB>();

                if (playerController != null && playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    if (playerController.isPlayerDead || playerController.beamUpParticle.isPlaying)
                    {
                        PlayerHeatEffects.OnPlayerDeathOrTeleporting(exhaustionFilter, resetDuration);
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

                if (playerController != null && playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    PlayerHeatEffects.OnPlayerExitZone(exhaustionFilter, resetDuration);
                }
            }
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
        private static bool inZone = false;
        private static Coroutine resetCoroutine;

        internal static void OnPlayerEnterZone(float timeInZoneMax)
        {
            if (!inZone)
            {
                StopResetCoroutine();
                exhaustionTimer = timeInZoneMax * heatSeverity; //recalculate time based on current severety
                inZone = true;
                Debug.Log("Player has entered a heatwave zone!");
            }
        }

        internal static void OnPlayerDeathOrTeleporting(Volume exhaustionFilter, float resetDuration)
        {
            if (resetCoroutine == null)
            {
                resetCoroutine = Instance.StartCoroutine(GraduallyResetEffects(exhaustionFilter, resetDuration));
                inZone = false;
                Debug.Log("Player is dead or teleporting, removing heatstroke!");
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
            inZone = false;
            if (resetCoroutine == null)
            {
                resetCoroutine = Instance.StartCoroutine(GraduallyResetEffects(exhaustionFilter, resetDuration));
                Debug.Log("Player has left the heatwave zone!");
            }
        }

        internal static void OnZoneDestroy(Volume exhaustionFilter)
        {
            if (heatSeverity > 0f && resetCoroutine == null)
            {
                SetHeatSeverity(0f, exhaustionFilter);
                inZone = false;
                exhaustionTimer = 0f;
                Debug.Log("Heatwave zone object destroyed, removing heatstroke!");
            }
        }

        private static void StopResetCoroutine()
        {
            if (resetCoroutine != null)
            {
                Instance.StopCoroutine(resetCoroutine);
                resetCoroutine = null;
            }
        }

        private static IEnumerator GraduallyResetEffects(Volume exhaustionFilter, float resetDuration)
        {
            float startSeverity = heatSeverity;
            //recalculate elapsed time so it'd take less time for interm. values of severity
            float elapsedTime = resetDuration * (1f - startSeverity);
            while (elapsedTime < resetDuration && startSeverity > 0f)
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

        private static PlayerHeatEffects instance;
        public static PlayerHeatEffects Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new GameObject("PlayerHeatEffects").AddComponent<PlayerHeatEffects>();
                    //DontDestroyOnLoad(instance.gameObject);
                }
                return instance;
            }
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
                Debug.Log($"{enemyType.enemyName} and its prefab loaded successfully!");
            }
            else
            {
                Debug.LogError("Failed to load EnemyType!");

            }
        }

        private void Start()
        {
            //Don't spawn if not a host
            if (!GameNetworkManager.Instance.isHostingGame)
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
            if (TimeOfDay.Instance.normalizedTimeOfDay > timer && TimeOfDay.Instance.timeHasStarted && GameNetworkManager.Instance.isHostingGame)
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
                    return VoxxMapHelper.ForceSpawnFlowerman.Value;
                case "baboon hawk":
                    return VoxxMapHelper.ForceSpawnBaboon.Value;
                case "radmech":
                    return VoxxMapHelper.ForceSpawnRadMech.Value;
                default:
                    return true; // Default to true if the enemy type is not explicitly handled
            }
        }
    }

    public class RingPortalStormEvent : MonoBehaviour
    {
        public List<float> deliveryTimes = new List<float>();


        [SerializeField] private GameObject shipmentPositionsObject; // Assign in the inspector, contains positions where to drop shipments
        [SerializeField] private float maxRotationSpeed = 5f;
        [SerializeField] private float rotationSpeedChangeDuration = 10f;
        [SerializeField] private float shipmentFallDuration = 15f; //Time to wait before disabling physics on dropped objects
        [SerializeField] private float cooldownDuration = 5f;
        [SerializeField] private float movementDuration = 30f; // Duration of movement between positions
        [SerializeField] private AnimationCurve movementCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // For smooth movement
        [SerializeField] private float maxTiltAngle = 25f; // Maximum tilt angle in degrees
        [SerializeField] private float tiltChangeDuration = 30f;


        private Animator animator;
        private Vector3 targetRotation;
        private System.Random seededRandom;
        private List<GameObject> shipments = new List<GameObject>();
        private List<Transform> shipmentPositions = new List<Transform>();
        private int currentShipmentIndex = 0;
        private float timer = 0f;
        private bool isPortalOpenAnimationFinished = false;
        private bool isPortalCloseAnimationFinished = false;
        private bool isDelivering = false;


        private void Start()
        {
            Debug.Log("RingPortalStormEvent: Start method called");
            animator = GetComponent<Animator>();
            animator.SetBool("isPortalActive", false);
            animator.SetBool("isPortalOpenFinished", false);
            animator.SetBool("isPortalCloseFinished", false);
            seededRandom = new System.Random(StartOfRound.Instance.randomMapSeed + 42);
            InitializeShipments();
            InitializeShipmentPositions();
            if (deliveryTimes.Count != shipments.Count || deliveryTimes.Count != shipmentPositions.Count)
            {
                Debug.LogError("RingPortalStormEvent: Mismatch in number of shipments, delivery locations and times!");
            }
        }

        private void Update()
        {
            timer += Time.deltaTime;

            if (currentShipmentIndex < deliveryTimes.Count && timer >= deliveryTimes[currentShipmentIndex] && !isDelivering)
            {
                Debug.Log($"RingPortalStormEvent: Starting delivery sequence for shipment {currentShipmentIndex}");
                StartCoroutine(PerformDeliverySequence());
            }
        }

        private void InitializeShipments()
        {
            Debug.Log("RingPortalStormEvent: Initializing shipments");
            Transform shipmentsParent = transform.Find("Shipments");
            foreach (Transform shipment in shipmentsParent)
            {
                shipments.Add(shipment.gameObject);
                shipment.gameObject.SetActive(false);
            }
        }

        private void InitializeShipmentPositions()
        {
            Debug.Log("RingPortalStormEvent: Initializing shipment positions");
            if (shipmentPositionsObject == null)
            {
                Debug.LogError("RingPortalStormEvent: ShipmentPositions object is not assigned!");
                return;
            }

            shipmentPositions.Clear(); // Clear any existing positions

            // Iterate through all children of the shipmentPositionsObject
            foreach (Transform child in shipmentPositionsObject.transform)
            {
                shipmentPositions.Add(child);
                Debug.Log($"Added shipment position: {child.name} at {child.position}");
            }

            Debug.Log($"Total shipment positions: {shipmentPositions.Count}");
        }

        private IEnumerator PerformDeliverySequence()
        {
            Debug.Log("RingPortalStormEvent: Starting delivery sequence");
            isDelivering = true;
            // Move to next position
            Debug.Log("RingPortalStormEvent: Moving to next position");
            yield return StartCoroutine(MoveToNextPosition());

            // Increase rotation speed
            Debug.Log("RingPortalStormEvent: Increasing rotation speed");
            yield return StartCoroutine(IncreaseRotationSpeed());

            // Activate portal and wait for animation to finish
            Debug.Log("RingPortalStormEvent: Activating portal");
            animator.SetBool("isPortalActive", true);
            animator.SetBool("isPortalOpenFinished", false);

            Debug.Log("RingPortalStormEvent: Waiting for portal open animation to finish");
            yield return new WaitUntil(() => isPortalOpenAnimationFinished);
            Debug.Log("RingPortalStormEvent: Portal open animation finished");
            yield return StartCoroutine(DecreaseRotationSpeed());
            Debug.Log("RingPortalStormEvent: Spawning and dropping shipment");
            yield return StartCoroutine(SpawnAndDropShipment());

            Debug.Log("RingPortalStormEvent: Closing portal");
            animator.SetBool("isPortalActive", false);
            animator.SetBool("isPortalCloseFinished", false);

            Debug.Log("RingPortalStormEvent: Waiting for portal close animation to finish");
            yield return new WaitUntil(() => isPortalCloseAnimationFinished);
            Debug.Log("RingPortalStormEvent: Portal close animation finished");


            Debug.Log($"RingPortalStormEvent: Preparing for next delivery. Current index: {currentShipmentIndex}");
            currentShipmentIndex++;
            yield return StartCoroutine(SetRandomTilt());
            yield return new WaitForSeconds(cooldownDuration);

            Debug.Log("RingPortalStormEvent: Delivery sequence completed");
            isDelivering = false;
        }

        private IEnumerator IncreaseRotationSpeed()
        {
            Debug.Log("RingPortalStormEvent: Starting to increase rotation speed");
            float elapsedTime = 0f;

            while (elapsedTime < rotationSpeedChangeDuration)
            {
                float t = elapsedTime / rotationSpeedChangeDuration;
                float outerSpeed = Mathf.Lerp(1f, maxRotationSpeed, t);
                float innerSpeed = Mathf.Lerp(0.5f, maxRotationSpeed * 0.75f, t);

                animator.SetFloat("RotSpeedOuter", outerSpeed);
                animator.SetFloat("RotSpeedInner", innerSpeed);

                elapsedTime += Time.deltaTime;
                yield return null;
            }
            Debug.Log("RingPortalStormEvent: Finished increasing rotation speed");
        }

        private IEnumerator DecreaseRotationSpeed()
        {
            Debug.Log("RingPortalStormEvent: Starting to decrease rotation speed");

            float elapsedTime = 0f;

            while (elapsedTime < (rotationSpeedChangeDuration*0.2f))
            {
                float t = elapsedTime / (rotationSpeedChangeDuration*0.2f);
                float outerSpeed = Mathf.Lerp(maxRotationSpeed, 1f, t);
                float innerSpeed = Mathf.Lerp(maxRotationSpeed * 0.75f, 0.5f, t);

                animator.SetFloat("RotSpeedOuter", outerSpeed);
                animator.SetFloat("RotSpeedInner", innerSpeed);

                elapsedTime += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator SetRandomTilt()
        {
            Debug.Log("RingPortalStormEvent: tilting the station");

            // Choose random tilt angles
            float targetTiltX = (float)seededRandom.NextDouble() * maxTiltAngle;
            float targetTiltZ = (float)seededRandom.NextDouble() * maxTiltAngle;
            targetRotation = new Vector3(targetTiltX, transform.rotation.eulerAngles.y, targetTiltZ);

            float elapsedTime = 0f;
            Vector3 startRotation = transform.rotation.eulerAngles;

            while (elapsedTime < tiltChangeDuration)
            {
                float t = elapsedTime / tiltChangeDuration;

                // Gradually change rotation
                Vector3 newRotation = Vector3.Lerp(startRotation, targetRotation, t);
                transform.rotation = Quaternion.Euler(newRotation);

                elapsedTime += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator SpawnAndDropShipment()
        {
            List<GameObject> settledObjects = new List<GameObject>();
            Action<GameObject> onObjectSettled = (obj) =>
            {
                settledObjects.Add(obj);
            };

            ShipmentCollisionHandler.OnObjectSettled += onObjectSettled;

            Debug.Log($"RingPortalStormEvent: Spawning shipment {currentShipmentIndex % shipments.Count}");
            GameObject shipment = shipments[currentShipmentIndex % shipments.Count];
            Transform[] childObjects = shipment.GetComponentsInChildren<Transform>(true);
            shipment.SetActive(true);

            yield return new WaitForSeconds(shipmentFallDuration);

            // Wait until all objects have settled
            yield return new WaitUntil(() => settledObjects.Count == childObjects.Length - 1); // -1 to exclude the parent object itself

            foreach (GameObject obj in settledObjects)
            {
                obj.transform.SetParent(shipmentPositionsObject.transform);
            }

            Debug.Log("RingPortalStormEvent: Shipment dropped");

            ShipmentCollisionHandler.OnObjectSettled -= onObjectSettled; // Unsubscribe to prevent memory leaks
        }

        private IEnumerator MoveToNextPosition()
        {
            Debug.Log("RingPortalStormEvent: Starting movement to next position");
            int nextPositionIndex = (currentShipmentIndex + 1) % shipmentPositions.Count;
            Vector3 startPosition = transform.position;
            Vector3 targetPosition = shipmentPositions[nextPositionIndex].position;

            // Preserve the current Y coordinate
            targetPosition.y = startPosition.y;

            // Set target rotation to zero for X and Z
            Vector3 startRotation = transform.rotation.eulerAngles;
            Vector3 levelRotation = new Vector3(0f, startRotation.y, 0f);

            float elapsedTime = 0f;

            while (elapsedTime < movementDuration)
            {
                float t = elapsedTime / movementDuration;
                float curveValue = movementCurve.Evaluate(t);

                // Move
                Vector3 newPosition = Vector3.Lerp(startPosition, targetPosition, curveValue);
                transform.position = newPosition;

                // Rotate
                Vector3 newRotation = Vector3.Lerp(startRotation, levelRotation, curveValue);
                transform.rotation = Quaternion.Euler(newRotation);

                elapsedTime += Time.deltaTime;
                yield return null;
            }

            // Ensure we end up exactly at the target position and rotation
            transform.position = targetPosition;
            transform.rotation = Quaternion.Euler(levelRotation);

            Debug.Log("RingPortalStormEvent: Finished moving to next position");
        }

        public void OnPortalOpenAnimationFinished()
        {
            Debug.Log("RingPortalStormEvent: Portal open animation finished");
            animator.SetBool("isPortalOpenFinished", true);
            isPortalOpenAnimationFinished = true;
        }
        public void OnPortalCloseAnimationFinished()
        {
            Debug.Log("RingPortalStormEvent: Portal close animation finished");
            animator.SetBool("isPortalCloseFinished", true);
            isPortalCloseAnimationFinished = true;
        }
    }

    public class ShipmentCollisionHandler : MonoBehaviour
    {
        public static event Action<GameObject> OnObjectSettled;
        private bool hasCollided = false;
        private Rigidbody rb;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!hasCollided && (collision.gameObject.CompareTag("Grass") || collision.gameObject.CompareTag("Aluminum")))
            {
                hasCollided = true;
                ParticleSystem smokeExplosion = GetComponent<ParticleSystem>();
                smokeExplosion?.Play();
                StartCoroutine(CheckIfSettled());
            }
        }

        private IEnumerator CheckIfSettled()
        {
            yield return new WaitForSeconds(0.5f); // Wait for a short delay before starting to check

            while (rb.velocity.magnitude > 0.01f)
            {
                yield return new WaitForSeconds(0.1f); // Check every 0.1 seconds
            }

            rb.useGravity = false;
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            this.enabled = false;
            OnObjectSettled?.Invoke(gameObject);
        }
    }


    internal class ToxicFumesInteract : NetworkBehaviour
    {
        public float damageTime = 5f; // Cooldown before fumes start to damage the player
        private float damageTimer = 0f;
        public int damageAmount = 3;

        private void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                PlayerControllerB playerController = other.gameObject.GetComponent<PlayerControllerB>();

                if (playerController != null && playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    damageTimer += Time.deltaTime;
                    playerController.drunknessInertia = Mathf.Clamp(playerController.drunknessInertia + Time.deltaTime / 1.5f * playerController.drunknessSpeed, 0.1f, 10f);
                    playerController.increasingDrunknessThisFrame = true;
                    if (damageTimer >= damageTime)
                    {
                        if (IsOwner)
                        {
                            GameNetworkManager.Instance.localPlayerController.DamagePlayer(damageAmount, true, true, CauseOfDeath.Suffocation, 0, false, default(Vector3));
                            damageTimer = 0;
                        }
                    }
                }
            }
        }
    }
}

