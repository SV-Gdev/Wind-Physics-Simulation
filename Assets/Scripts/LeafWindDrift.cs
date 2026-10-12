using UnityEngine;


[RequireComponent(typeof(ParticleSystem))]
public class LeafWindDrift : MonoBehaviour
{
    [Header("Wind & Emission Linking")]
    [Tooltip("Minimum wind main strength required before leaves start spawning.")]
    public float minWindThreshold = 0.05f;
    [Tooltip("Base number of leaves spawned per second when wind is active.")]
    public float baseLeavesPerSecond = 3f;
    [Tooltip("How strongly the WindZone pushes the leaves.")]
    public float windInfluence = 8f;

    [Header("Falling")]
    [Tooltip("0 = leaves float in place, 0.1 = slow drift down, 1 = falls like a rock.")]
    public float gravity = 0.08f;

    [Header("Flutter & Tumble")]
    public float flutterStrength = 0.6f;
    public float flutterFrequency = 0.5f;
    [Tooltip("Max spin speed in degrees per second.")]
    public float spinDegreesPerSecond = 120f;

    [Header("Pool & Area")]
    public int maxLeaves = 40;
    [Tooltip("Size of the area around the tree crown where leaves start.")]
    public Vector3 canopySize = new Vector3(3f, 2f, 3f);
    public float leafLifetime = 8f;
    [Tooltip("Size of the falling leaf particles.")]
    public float leafSize = 1.0f;

    private ParticleSystem ps;
    private WindZone targetWindZone;

    void Awake()
    {
        Configure();
    }

    void Start()
    {
        targetWindZone = FindFirstObjectByType<WindZone>();
    }

    void Update()
    {
        if (ps == null) return;

        var emission = ps.emission;

        if (targetWindZone == null)
        {
            emission.rateOverTime = 0f;
            return;
        }

        if (targetWindZone.gameObject.activeInHierarchy && targetWindZone.windMain >= minWindThreshold)
        {
            emission.rateOverTime = baseLeavesPerSecond * Mathf.Max(1f, targetWindZone.windMain * 0.2f);

            if (!ps.isPlaying)
                ps.Play();
        }
        else
        {
            emission.rateOverTime = 0f;
        }
    }

    [ContextMenu("Apply Settings")]
    public void Configure()
    {
        ps = GetComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = leafLifetime;
        main.gravityModifier = gravity;
        main.maxParticles = maxLeaves;
        main.startSize = leafSize;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = canopySize;

       
        var external = ps.externalForces;
        external.enabled = true;
        external.multiplier = windInfluence;
        external.influenceFilter = ParticleSystemGameObjectFilter.LayerMask;
        external.influenceMask = -1;

       
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = flutterStrength;
        noise.frequency = flutterFrequency;
        noise.scrollSpeed = 0.3f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        
        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        float spin = spinDegreesPerSecond * Mathf.Deg2Rad;
        rotation.z = new ParticleSystem.MinMaxCurve(-spin, spin);
    }
}