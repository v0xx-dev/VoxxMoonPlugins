using BepInEx;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System;

namespace DerelictMoonPlugin
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class DerelictMoonPlugin : BaseUnityPlugin
    {
        public static DerelictMoonPlugin instance;

        private static void NetcodePatcher()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }

        private void Awake()
        {
            instance = this;

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            NetcodePatcher();
        }

    }



    public static class ListShuffler
    {
        public static void ShuffleInSync<T1, T2>(IList<T1> list1, IList<T2> list2, System.Random random)
        {
            if (list1.Count != list2.Count)
            {
                throw new System.ArgumentException("Lists must have the same length.");
            }

            int n = list1.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int j = random.Next(0, i + 1);

                // Swap elements in both lists
                (list1[i], list1[j]) = (list1[j], list1[i]);
                (list2[i], list2[j]) = (list2[j], list2[i]);
            }
        }
    }

    public class RingPortalStormEvent : NetworkBehaviour
    {
        public List<float> deliveryTimes = new List<float>();

        [SerializeField] private GameObject shipmentPositionsObject; // Assign in the inspector, contains positions where to drop shipments
        [SerializeField] private float maxRotationSpeed = 5f;
        [SerializeField] private float rotationSpeedChangeDuration = 10f;
        [SerializeField] private float cooldownDuration = 5f;
        [SerializeField] private float movementDuration = 30f; // Duration of movement between positions
        [SerializeField] private AnimationCurve movementCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // For smooth movement
        [SerializeField] private float maxTiltAngle = 25f; // Maximum tilt angle in degrees
        [SerializeField] private float tiltChangeDuration = 30f;
        [SerializeField] private AudioClip[] startMovingSounds;
        [SerializeField] private AudioClip[] ringMovementSounds;
        [SerializeField] private AudioClip startSpinningSound;
        [SerializeField] private float fadeOutDuration = 1f;

        private AudioSource audioSource;
        private Coroutine soundCoroutine;
        private Animator animator;
        private Vector3 targetRotation;
        private System.Random seededRandom;
        private List<GameObject> shipments = new List<GameObject>();
        private List<Transform> shipmentPositions = new List<Transform>();
        private float timer = 0f;
        private bool isPortalOpenAnimationFinished = false;
        private bool isPortalCloseAnimationFinished = false;
        private bool isDelivering = false;
        private bool shipmentSettledOnClient = false;

        private NetworkVariable<int> currentShipmentIndex = new NetworkVariable<int>(0);


        private void Start()
        {
            Debug.Log("RingPortalStormEvent: Start method called");
            animator = GetComponent<Animator>();
            seededRandom = new System.Random(StartOfRound.Instance.randomMapSeed + 42);
            InitializeShipments();
            InitializeShipmentPositions();

            // Shuffle the shipment positions and delivery times
            ListShuffler.ShuffleInSync(shipmentPositions, shipments, seededRandom);

            if (shipmentPositions.Count != shipments.Count)
            {
                Debug.LogError("RingPortalStormEvent: Mismatch in number of shipments and delivery locations!");
            }
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }



        private void Update()
        {

            if (currentShipmentIndex.Value >= deliveryTimes.Count)
            {
                Debug.Log("RingPortalStormEvent: All shipments delivered, disabling station");
                this.enabled = false;
            }

            if (!IsServer) return; // Only run on the server

            timer += Time.deltaTime;
            //float timer = TimeOfDay.Instance.normalizedTimeOfDay;

            if (currentShipmentIndex.Value < deliveryTimes.Count && timer >= deliveryTimes[currentShipmentIndex.Value] && !isDelivering)
            {
                Debug.Log($"RingPortalStormEvent: Starting delivery sequence for shipment {currentShipmentIndex.Value}");
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

        [ClientRpc]
        private void PlayMovementSoundsClientRpc()
        {
            if (soundCoroutine != null)
            {
                StopCoroutine(soundCoroutine);
            }
            soundCoroutine = StartCoroutine(MovementSoundSequence());
        }

        private IEnumerator MovementSoundSequence()
        {
            // Play random start moving sound
            if (startMovingSounds.Length > 0)
            {
                AudioClip randomStartSound = startMovingSounds[seededRandom.Next(startMovingSounds.Length)];
                audioSource.PlayOneShot(randomStartSound);
                yield return new WaitForSeconds(randomStartSound.length);
            }

            // Start looping movement sounds
            while (true)
            {
                if (ringMovementSounds.Length > 0)
                {
                    AudioClip randomClip = ringMovementSounds[seededRandom.Next(ringMovementSounds.Length)];
                    audioSource.clip = randomClip;
                    audioSource.Play();
                    yield return new WaitForSeconds(randomClip.length);
                }
                else
                {
                    yield return null;
                }
            }
        }

        [ClientRpc]
        private void StopMovementSoundsClientRpc()
        {
            if (soundCoroutine != null)
            {
                StopCoroutine(soundCoroutine);
                soundCoroutine = null;
            }
            StartCoroutine(FadeOutSound());

        }

        private IEnumerator FadeOutSound()
        {
            float startVolume = audioSource.volume;
            float deltaVolume = startVolume * Time.deltaTime / fadeOutDuration;

            while (audioSource.volume > 0)
            {
                audioSource.volume -= deltaVolume;
                yield return null;
            }

            audioSource.Stop();
            audioSource.volume = startVolume;
        }

        [ClientRpc]
        private void PlayStartSpinningSoundClientRpc()
        {
            if (startSpinningSound != null)
            {
                audioSource.clip = startSpinningSound;
                audioSource.Play();
            }
        }

        private IEnumerator PerformDeliverySequence()
        {

            Debug.Log("RingPortalStormEvent: Starting delivery sequence");
            isDelivering = true;
            shipmentSettledOnClient = false;

            animator.SetBool("isPortalActive", false);
            animator.SetBool("isPortalOpenFinished", false);
            animator.SetBool("isPortalCloseFinished", false);
            isPortalOpenAnimationFinished = false;
            isPortalCloseAnimationFinished = false;

            // Start playing movement sounds immediately
            PlayMovementSoundsClientRpc();

            // Move to next position
            Debug.Log("RingPortalStormEvent: Moving to next position");
            yield return StartCoroutine(MoveToNextPosition());

            // Stop movement sounds with fade out
            StopMovementSoundsClientRpc();

            // Wait for fade out to complete
            yield return new WaitForSeconds(fadeOutDuration + 0.5f);

            // Play start spinning sound
            PlayStartSpinningSoundClientRpc();

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

            yield return new WaitForSeconds(cooldownDuration);

            Debug.Log("RingPortalStormEvent: Spawning and dropping shipment");
            SpawnAndDropShipmentClientRpc();
            yield return new WaitUntil(() => shipmentSettledOnClient);

            yield return new WaitForSeconds(cooldownDuration);

            Debug.Log("RingPortalStormEvent: Closing portal");
            animator.SetBool("isPortalActive", false);
            animator.SetBool("isPortalCloseFinished", false);

            Debug.Log("RingPortalStormEvent: Waiting for portal close animation to finish");
            yield return new WaitUntil(() => isPortalCloseAnimationFinished);
            Debug.Log("RingPortalStormEvent: Portal close animation finished");

            Debug.Log($"RingPortalStormEvent: Preparing for next delivery. Current index: {currentShipmentIndex}");
            currentShipmentIndex.Value++;
            yield return StartCoroutine(SetRandomTilt());

            Debug.Log("RingPortalStormEvent: Delivery sequence completed");
            isDelivering = false;
        }

        private IEnumerator MoveToNextPosition()
        {
            int nextPositionIndex = currentShipmentIndex.Value % shipmentPositions.Count;
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

            while (elapsedTime < (rotationSpeedChangeDuration * 0.2f))
            {
                float t = elapsedTime / (rotationSpeedChangeDuration * 0.2f);
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

        [ClientRpc]
        private void SpawnAndDropShipmentClientRpc()
        {
            StartCoroutine(SpawnAndDropShipment());
        }

        [ClientRpc]
        private void NotifyShipmentSettledClientRpc()
        {
            if (IsServer)
            {
                shipmentSettledOnClient = true;
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

            Debug.Log($"RingPortalStormEvent: Spawning shipment {currentShipmentIndex.Value % shipments.Count}");
            GameObject shipment = shipments[currentShipmentIndex.Value % shipments.Count];
            Transform[] childObjects = shipment.GetComponentsInChildren<Transform>(true);
            shipment.SetActive(true);

            // Wait until all objects have settled
            yield return new WaitUntil(() => settledObjects.Count == childObjects.Length - 1); // -1 to exclude the parent object itself

            foreach (GameObject obj in settledObjects)
            {
                obj.transform.SetParent(shipmentPositionsObject.transform);
            }

            Debug.Log("RingPortalStormEvent: Shipment dropped");
            ShipmentCollisionHandler.OnObjectSettled -= onObjectSettled; // Unsubscribe to prevent memory leaks
            NotifyShipmentSettledClientRpc();
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
        private MeshCollider meshCollider;
        private Rigidbody rb;
        private KillPlayer killPlayerScript;
        private AudioSource impactSound;
        private NavMeshObstacle navMeshObstacle;

        [SerializeField] private float settlementThreshold = 0.01f;
        [SerializeField] private float initialCheckDelay = 0.5f;
        [SerializeField] private float checkInterval = 0.1f;
        [SerializeField] private float maxTimeToSettle = 15f;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            killPlayerScript = GetComponent<KillPlayer>();
            impactSound = GetComponent<AudioSource>();
            meshCollider = GetComponent<MeshCollider>();
            navMeshObstacle = GetComponent<NavMeshObstacle>();
            if (navMeshObstacle != null)
            {
                navMeshObstacle.carving = false;
            }
            if (meshCollider != null)
            {
                meshCollider.convex = true;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!hasCollided && (collision.gameObject.CompareTag("Grass") || collision.gameObject.CompareTag("Aluminum")))
            {
                hasCollided = true;

                // Play impact sound
                impactSound?.Play();

                // Play particle effect
                ParticleSystem smokeExplosion = GetComponent<ParticleSystem>();
                smokeExplosion?.Play();

                StartCoroutine(CheckIfSettled());
            }
        }

        private IEnumerator CheckIfSettled()
        {
            float elapsedTime = initialCheckDelay;
            yield return new WaitForSeconds(initialCheckDelay);

            while (rb.velocity.magnitude > settlementThreshold && elapsedTime < maxTimeToSettle)
            {
                yield return new WaitForSeconds(checkInterval);
                elapsedTime += checkInterval;
            }

            // Object has settled
            rb.useGravity = false;
            rb.isKinematic = true;
            //rb.velocity = Vector3.zero;

            // Disable kill script
            if (killPlayerScript != null)
            {
                killPlayerScript.enabled = false;
            }

            //Switch to a proper mesh collider
            if (meshCollider != null)
            {
                meshCollider.convex = false;
            }

            //Change NavMesh
            if (navMeshObstacle != null)
            {
                navMeshObstacle.carving = true;
            }

            OnObjectSettled?.Invoke(gameObject);

            // Disable this script
            this.enabled = false;
        }
    }

    public class KillPlayer : MonoBehaviour
    {
        [SerializeField] private float killVelocityThreshold = 0f;
        private CauseOfDeath causeOfDeath = CauseOfDeath.Crushing;
        private int deathAnimation = 0;

        private Rigidbody rb;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.CompareTag("Player"))
            {
                PlayerControllerB playerController = collision.gameObject.GetComponent<PlayerControllerB>();

                if (playerController != null && playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    if (rb.velocity.magnitude > killVelocityThreshold)
                    {
                        playerController.KillPlayer(bodyVelocity: rb.velocity, spawnBody: true,
                                                    causeOfDeath: causeOfDeath, deathAnimation: deathAnimation);
                    }
                }
            }
        }

    }

    internal class ToxicFumesInteract : MonoBehaviour
    {
        [SerializeField] private float damageTime = 3f; // Cooldown before fumes start to damage the player
        [SerializeField] private int damageAmount = 5;
        [SerializeField] private float drunknessPower = 1.5f;
        private float damageTimer = 0f;

        private void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                PlayerControllerB playerController = other.gameObject.GetComponent<PlayerControllerB>();

                if (playerController != null && playerController == GameNetworkManager.Instance.localPlayerController)
                {
                    damageTimer += Time.deltaTime;
                    playerController.drunknessInertia = Mathf.Clamp(playerController.drunknessInertia + Time.deltaTime / drunknessPower * playerController.drunknessSpeed, 0.1f, 10f);
                    playerController.increasingDrunknessThisFrame = true;
                    if (damageTimer >= damageTime)
                    {
                        playerController.DamagePlayer(damageAmount, true, true, CauseOfDeath.Suffocation, 0, false, default(Vector3));
                        damageTimer = 0;
                    }
                }
            }
        }
    }
}

