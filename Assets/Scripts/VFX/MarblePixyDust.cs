using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

[DisallowMultipleComponent]
public sealed class MarblePixyDust : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private MarbleSupportGraph supportGraph;
    [SerializeField] private MarbleBasketsAndPool marblePool;

    [Header("Velocity Response")]
    [SerializeField, Min(0f)] private float minimumTrailSpeed = 0.5f;
    [SerializeField, Min(0.1f)] private float maximumTrailSpeed = 14f;

    [Header("Particle Rate")]
    [SerializeField, Min(0f)] private float minimumSpawnRate = 8f;
    [SerializeField, Min(0f)] private float maximumSpawnRate = 110f;

    [Header("Trail Length")]
    [SerializeField, Min(0f)] private float minimumTrailLength = 0.08f;
    [SerializeField, Min(0f)] private float maximumTrailLength = 0.75f;

    [Header("Particle Size")]
    [SerializeField, Min(0.001f)] private float minimumParticleSize = 0.012f;
    [SerializeField, Min(0.001f)] private float maximumParticleSize = 0.055f;

    [Header("Impact Sparkles")]
    [SerializeField, Min(0f)] private float highImpactSpeed = 4.5f;
    [SerializeField, Min(0.01f)] private float maximumImpactSpeed = 18f;
    [SerializeField, Range(0f, 1f)] private float minimumImpactIntensity = 0.25f;
    [SerializeField, Range(0f, 3f)] private float maximumImpactIntensity = 1.8f;

    public static readonly Color Crimson = new Color(0.92f, 0.012f, 0f, 1f);
    public static readonly Color Amber = new Color(1f, 0.30f, 0f, 1f);
    public static readonly Color Gold = new Color(1f, 0.68f, 0f, 1f);

    public float MinimumTrailSpeed => minimumTrailSpeed;
    public float MaximumTrailSpeed => maximumTrailSpeed;
    public float MinimumSpawnRate => minimumSpawnRate;
    public float MaximumSpawnRate => maximumSpawnRate;
    public float MinimumTrailLength => minimumTrailLength;
    public float MaximumTrailLength => maximumTrailLength;
    public float MinimumParticleSize => minimumParticleSize;
    public float MaximumParticleSize => maximumParticleSize;
    public float HighImpactSpeed => highImpactSpeed;
    public float MaximumImpactSpeed => maximumImpactSpeed;
    public float MinimumImpactIntensity => minimumImpactIntensity;
    public float MaximumImpactIntensity => maximumImpactIntensity;

    private readonly HashSet<MarblePixyDustEmitter> activeEmitters = new HashSet<MarblePixyDustEmitter>();

    private void OnEnable()
    {
        if (supportGraph != null)
        {
            supportGraph.MarbleReleased += OnMarbleReleased;
            supportGraph.MarbleMatched += OnMarbleMatched;
        }
        if (marblePool != null)
        {
            marblePool.MarbleReturnedToPool += OnReturnedToPool;
            marblePool.MarbleRentedFromPool += OnRentedFromPool;
        }
    }

    private void OnDisable()
    {
        if (supportGraph != null)
        {
            supportGraph.MarbleReleased -= OnMarbleReleased;
            supportGraph.MarbleMatched -= OnMarbleMatched;
        }
        if (marblePool != null)
        {
            marblePool.MarbleReturnedToPool -= OnReturnedToPool;
            marblePool.MarbleRentedFromPool -= OnRentedFromPool;
        }

        foreach (MarblePixyDustEmitter emitter in activeEmitters)
            if (emitter != null) emitter.StopAndClearImmediate();
        activeEmitters.Clear();
    }

    private void OnMarbleReleased(Rigidbody body)
    {
        if (body == null) return;
        MarblePixyDustEmitter emitter = body.GetComponent<MarblePixyDustEmitter>();
        if (emitter == null) emitter = body.gameObject.AddComponent<MarblePixyDustEmitter>();
        emitter.Configure(this, body);
        emitter.BeginTrail();
        activeEmitters.Add(emitter);
    }

    private void OnMarbleMatched(Rigidbody body) => StopEffects(body);
    private void OnReturnedToPool(Rigidbody body) => StopEffects(body);
    private void OnRentedFromPool(Rigidbody body) => StopEffects(body);

    private void StopEffects(Rigidbody body)
    {
        if (body == null) return;
        MarblePixyDustEmitter emitter = body.GetComponent<MarblePixyDustEmitter>();
        if (emitter == null) return;
        emitter.StopAndClearImmediate();
        activeEmitters.Remove(emitter);
    }
}

[DisallowMultipleComponent]
public sealed class MarblePixyDustEmitter : MonoBehaviour
{
    private static readonly int SpawnRateId = Shader.PropertyToID("SpawnRate");
    private static readonly int TrailLengthId = Shader.PropertyToID("TrailLength");
    private static readonly int ParticleSizeId = Shader.PropertyToID("ParticleSize");
    private static readonly int CrimsonColorId = Shader.PropertyToID("CrimsonColor");
    private static readonly int AmberColorId = Shader.PropertyToID("AmberColor");
    private static readonly int GoldColorId = Shader.PropertyToID("GoldColor");
    private static readonly int ImpactPositionId = Shader.PropertyToID("ImpactPosition");
    private static readonly int ImpactSpeedId = Shader.PropertyToID("ImpactSpeed");
    private static readonly int ImpactIntensityId = Shader.PropertyToID("ImpactIntensity");
    private static readonly int TrailStartEventId = Shader.PropertyToID("OnTrailStart");
    private static readonly int TrailStopEventId = Shader.PropertyToID("OnTrailStop");
    private static readonly int ImpactEventId = Shader.PropertyToID("OnImpact");

    private MarblePixyDust controller;
    private Rigidbody body;
    private VisualEffect vfx;
    private bool activeTrail;

    public void Configure(MarblePixyDust settings, Rigidbody rigidbody)
    {
        controller = settings;
        body = rigidbody != null ? rigidbody : GetComponent<Rigidbody>();
        if (vfx == null) vfx = GetComponentInChildren<VisualEffect>(true);
        ApplyPalette();
    }

    public void BeginTrail()
    {
        if (controller == null || body == null) return;
        if (vfx == null) vfx = GetComponentInChildren<VisualEffect>(true);
        if (vfx == null)
        {
            Debug.LogWarning($"{name}: no VisualEffect found for pixy dust.", this);
            return;
        }

        vfx.enabled = true;
        vfx.Reinit();
        ApplyPalette();
        vfx.SetFloat(SpawnRateId, controller.MinimumSpawnRate);
        vfx.SetFloat(TrailLengthId, controller.MinimumTrailLength);
        vfx.SetFloat(ParticleSizeId, controller.MinimumParticleSize);
        vfx.Play();
        vfx.SendEvent(TrailStartEventId);
        activeTrail = true;
    }

    private void Update()
    {
        if (!activeTrail || controller == null || body == null || vfx == null) return;

        float speed = body.velocity.magnitude;
        float t = Mathf.Clamp01(Mathf.InverseLerp(controller.MinimumTrailSpeed, controller.MaximumTrailSpeed, speed));
        float curved = t * t;
        float rate = speed < controller.MinimumTrailSpeed ? 0f : Mathf.Lerp(controller.MinimumSpawnRate, controller.MaximumSpawnRate, curved);

        vfx.SetFloat(SpawnRateId, rate);
        vfx.SetFloat(TrailLengthId, Mathf.Lerp(controller.MinimumTrailLength, controller.MaximumTrailLength, t));
        vfx.SetFloat(ParticleSizeId, Mathf.Lerp(controller.MinimumParticleSize, controller.MaximumParticleSize, curved));
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!activeTrail || controller == null || vfx == null || collision == null) return;
        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < controller.HighImpactSpeed) return;

        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        float t = Mathf.Clamp01(Mathf.InverseLerp(controller.HighImpactSpeed, controller.MaximumImpactSpeed, impactSpeed));
        float intensity = Mathf.Lerp(controller.MinimumImpactIntensity, controller.MaximumImpactIntensity, t);

        vfx.SetVector3(ImpactPositionId, point);
        vfx.SetFloat(ImpactSpeedId, impactSpeed);
        vfx.SetFloat(ImpactIntensityId, intensity);
        vfx.SendEvent(ImpactEventId);
    }

    public void StopAndClearImmediate()
    {
        activeTrail = false;
        if (vfx == null) vfx = GetComponentInChildren<VisualEffect>(true);
        if (vfx == null) return;
        vfx.SetFloat(SpawnRateId, 0f);
        vfx.SendEvent(TrailStopEventId);
        vfx.Stop();
        vfx.Reinit();
        vfx.Stop();
        vfx.enabled = false;
    }

    private void ApplyPalette()
    {
        if (vfx == null) return;
        vfx.SetVector4(CrimsonColorId, (Vector4)MarblePixyDust.Crimson);
        vfx.SetVector4(AmberColorId, (Vector4)MarblePixyDust.Amber);
        vfx.SetVector4(GoldColorId, (Vector4)MarblePixyDust.Gold);
    }
}
