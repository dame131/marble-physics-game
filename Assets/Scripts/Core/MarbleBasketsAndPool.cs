using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MarbleBasketsAndPool : MonoBehaviour
{
    public enum BasketId { Left, Center, Right }

    [Serializable]
    public sealed class BasketConfig
    {
        public BasketId basketId;
        public Collider triggerCollider;
        public AudioSource hitAudioSource;
        public AudioClip hitClip;
        public ParticleSystem scoreBurstParticles;
    }

    private sealed class WaveState
    {
        public int active;
        public int caught;
        public float created;
    }

    [Header("Graph")]
    [SerializeField] private MarbleSupportGraph supportGraph;

    [Header("Baskets")]
    [SerializeField] private BasketConfig leftBasket = new BasketConfig { basketId = BasketId.Left };
    [SerializeField] private BasketConfig centerBasket = new BasketConfig { basketId = BasketId.Center };
    [SerializeField] private BasketConfig rightBasket = new BasketConfig { basketId = BasketId.Right };

    [Header("Scoring")]
    [SerializeField, Min(0)] private int sideBasketScore = 100;
    [SerializeField, Min(0)] private int centerBasketScore = 250;
    [SerializeField, Min(1)] private int maxComboMultiplier = 20;

    [Header("Audio")]
    [SerializeField, Range(0f, 0.25f)] private float pitchVariance = 0.06f;

    [Header("Burst FX")]
    [SerializeField, Min(1)] private int baseBurstCount = 12;
    [SerializeField, Min(0)] private int extraParticlesPerCombo = 3;
    [SerializeField, Min(1)] private int maxBurstCount = 60;

    [Header("Pool")]
    [SerializeField] private Rigidbody marblePrefab;
    [SerializeField, Min(0)] private int prewarmCount = 128;
    [SerializeField] private Transform poolRoot;

    public event Action<int> ScoreChanged;
    public event Action<BasketId, int, int, int> BasketScored;
    public event Action<int, int> ComboChanged;
    public event Action<Rigidbody> MarbleReturnedToPool;
    public event Action<Rigidbody> MarbleRentedFromPool;

    private readonly Queue<Rigidbody> pool = new Queue<Rigidbody>(256);
    private readonly HashSet<int> pooledIds = new HashSet<int>();
    private readonly Dictionary<Rigidbody, int> releasedWaveByBody = new Dictionary<Rigidbody, int>(512);
    private readonly Dictionary<int, WaveState> waves = new Dictionary<int, WaveState>(64);

    private int nextWaveId = 1;
    private int openWaveId = -1;
    private bool batchOpen;
    private int batchFrame = -1;
    private int totalScore;
    private int lastComboMultiplier = 1;

    public int TotalScore => totalScore;
    public int LastComboMultiplier => lastComboMultiplier;
    public int AvailablePoolCount => pool.Count;

    private void Awake()
    {
        if (poolRoot == null)
        {
            GameObject go = new GameObject("Marble Pool");
            go.transform.SetParent(transform, false);
            poolRoot = go.transform;
        }

        SetupBasket(leftBasket, BasketId.Left);
        SetupBasket(centerBasket, BasketId.Center);
        SetupBasket(rightBasket, BasketId.Right);
        Prewarm();
    }

    private void OnEnable()
    {
        if (supportGraph == null) return;
        supportGraph.MarbleReleased += OnMarbleReleased;
        supportGraph.MarbleMatched += OnMarbleMatched;
        supportGraph.UnsupportedMarblesReleased += OnReleaseBatchFinished;
    }

    private void OnDisable()
    {
        if (supportGraph == null) return;
        supportGraph.MarbleReleased -= OnMarbleReleased;
        supportGraph.MarbleMatched -= OnMarbleMatched;
        supportGraph.UnsupportedMarblesReleased -= OnReleaseBatchFinished;
    }

    private void LateUpdate()
    {
        if (batchOpen && Time.frameCount > batchFrame) CloseBatch();
    }

    private void SetupBasket(BasketConfig config, BasketId id)
    {
        if (config == null) return;
        config.basketId = id;
        if (config.triggerCollider != null)
        {
            config.triggerCollider.isTrigger = true;
            MarbleBasketTriggerRelay relay = config.triggerCollider.GetComponent<MarbleBasketTriggerRelay>();
            if (relay == null) relay = config.triggerCollider.gameObject.AddComponent<MarbleBasketTriggerRelay>();
            relay.Configure(this, id);
        }

        if (config.scoreBurstParticles != null)
        {
            ParticleSystem.MainModule main = config.scoreBurstParticles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            config.scoreBurstParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnMarbleReleased(Rigidbody marble)
    {
        if (marble == null || releasedWaveByBody.ContainsKey(marble)) return;
        if (!batchOpen || batchFrame != Time.frameCount)
        {
            if (batchOpen) CloseBatch();
            openWaveId = nextWaveId++;
            waves[openWaveId] = new WaveState { created = Time.unscaledTime };
            batchOpen = true;
            batchFrame = Time.frameCount;
        }

        releasedWaveByBody[marble] = openWaveId;
        waves[openWaveId].active++;
    }

    private void OnReleaseBatchFinished(int count) => CloseBatch();

    private void CloseBatch()
    {
        batchOpen = false;
        openWaveId = -1;
        batchFrame = -1;
    }

    private void OnMarbleMatched(Rigidbody marble)
    {
        if (marble != null) ReturnMarble(marble);
    }

    internal void HandleBasketTrigger(BasketId basketId, Collider other)
    {
        if (other == null) return;
        Rigidbody marble = other.attachedRigidbody;
        if (marble == null || !releasedWaveByBody.TryGetValue(marble, out int waveId)) return;
        if (!waves.TryGetValue(waveId, out WaveState wave)) return;
        if (pooledIds.Contains(marble.GetInstanceID())) return;

        wave.caught++;
        int multiplier = Mathf.Clamp(wave.caught, 1, maxComboMultiplier);
        lastComboMultiplier = multiplier;
        int baseScore = basketId == BasketId.Center ? centerBasketScore : sideBasketScore;
        int award = baseScore * multiplier;
        totalScore += award;

        BasketConfig config = GetConfig(basketId);
        PlayHit(config);
        EmitBurst(config, marble.worldCenterOfMass, multiplier);

        ScoreChanged?.Invoke(totalScore);
        ComboChanged?.Invoke(waveId, multiplier);
        BasketScored?.Invoke(basketId, baseScore, multiplier, award);
        ReturnMarble(marble);
    }

    private BasketConfig GetConfig(BasketId id)
    {
        switch (id)
        {
            case BasketId.Left: return leftBasket;
            case BasketId.Center: return centerBasket;
            default: return rightBasket;
        }
    }

    private void PlayHit(BasketConfig config)
    {
        if (config?.hitAudioSource == null) return;
        AudioClip clip = config.hitClip != null ? config.hitClip : config.hitAudioSource.clip;
        if (clip == null) return;
        config.hitAudioSource.pitch = UnityEngine.Random.Range(1f - pitchVariance, 1f + pitchVariance);
        config.hitAudioSource.PlayOneShot(clip);
    }

    private void EmitBurst(BasketConfig config, Vector3 position, int multiplier)
    {
        if (config?.scoreBurstParticles == null) return;
        config.scoreBurstParticles.transform.position = position;
        int count = Mathf.Clamp(baseBurstCount + (multiplier - 1) * extraParticlesPerCombo, 1, maxBurstCount);
        config.scoreBurstParticles.Emit(count);
    }

    private void Prewarm()
    {
        if (marblePrefab == null) return;
        for (int i = 0; i < prewarmCount; i++)
        {
            Rigidbody body = Instantiate(marblePrefab, poolRoot);
            PrepareForPool(body);
            Enqueue(body);
        }
    }

    public Rigidbody RentMarble(Vector3 position, Quaternion rotation, Transform parent = null)
    {
        Rigidbody body = null;
        while (pool.Count > 0 && body == null)
        {
            body = pool.Dequeue();
            if (body != null) pooledIds.Remove(body.GetInstanceID());
        }

        if (body == null)
        {
            if (marblePrefab == null) return null;
            body = Instantiate(marblePrefab, poolRoot);
        }

        body.transform.SetParent(parent, false);
        body.transform.SetPositionAndRotation(position, rotation);
        body.gameObject.SetActive(true);
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        body.detectCollisions = false;
        SetColliders(body, false);
        MarbleRentedFromPool?.Invoke(body);
        return body;
    }

    public void ReturnMarble(Rigidbody marble)
    {
        if (marble == null || pooledIds.Contains(marble.GetInstanceID())) return;

        if (releasedWaveByBody.TryGetValue(marble, out int waveId))
        {
            releasedWaveByBody.Remove(marble);
            if (waves.TryGetValue(waveId, out WaveState wave))
            {
                wave.active = Mathf.Max(0, wave.active - 1);
                if (wave.active == 0 && (!batchOpen || openWaveId != waveId)) waves.Remove(waveId);
            }
        }

        PrepareForPool(marble);
        Enqueue(marble);
        MarbleReturnedToPool?.Invoke(marble);
    }

    private void PrepareForPool(Rigidbody body)
    {
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        body.detectCollisions = false;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        SetColliders(body, false);

        ParticleSystem[] particles = body.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++) particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        TrailRenderer[] trails = body.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++) trails[i].Clear();

        body.transform.SetParent(poolRoot, false);
        body.gameObject.SetActive(false);
    }

    private void Enqueue(Rigidbody body)
    {
        if (body == null || !pooledIds.Add(body.GetInstanceID())) return;
        pool.Enqueue(body);
    }

    private static void SetColliders(Rigidbody body, bool enabled)
    {
        Collider[] colliders = body.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = enabled;
    }

    public void ResetScore()
    {
        totalScore = 0;
        lastComboMultiplier = 1;
        ScoreChanged?.Invoke(totalScore);
    }
}

[DisallowMultipleComponent]
public sealed class MarbleBasketTriggerRelay : MonoBehaviour
{
    private MarbleBasketsAndPool owner;
    private MarbleBasketsAndPool.BasketId basketId;

    public void Configure(MarbleBasketsAndPool controller, MarbleBasketsAndPool.BasketId id)
    {
        owner = controller;
        basketId = id;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (owner != null) owner.HandleBasketTrigger(basketId, other);
    }
}
