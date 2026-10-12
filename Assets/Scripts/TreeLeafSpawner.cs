using System.Collections.Generic;
using UnityEngine;


public class TreeLeafSpawner : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Leave empty to auto-find the active Terrain.")]
    public Terrain terrain;
    [Tooltip("Your 'Leaves Particles' prefab (with the LeafWindDrift script on it).")]
    public GameObject leafParticlesPrefab;
    [Tooltip("Terrain tree prototype indexes to add leaves to (Pine_A, B, C, D). Empty = ALL trees. " +
             "Press Play once and check the Console to see each tree's index.")]
    public List<int> treePrototypeIndexes = new List<int>();
    public bool logPrototypesOnStart = true;

    [Header("Amount")]
    [Tooltip("Fraction of matching trees that get a leaf emitter (0.15 = 15%).")]
    [Range(0f, 1f)] public float spawnChance = 0.15f;
    [Tooltip("Hard cap on the number of emitters.")]
    public int maxEmitters = 150;
    public int randomSeed = 1;

    [Header("Placement")]
    [Tooltip("How high up the tree the leaves start (0 = base, 1 = top).")]
    [Range(0f, 1f)] public float heightFraction = 0.75f;
    [Tooltip("Tree height in metres. 0 = measure automatically from the tree prefab.")]
    public float treeHeightOverride = 0f;
    public Vector3 extraOffset = Vector3.zero;

    [Header("Performance")]
    [Tooltip("Emitters only run near this object. Empty = Main Camera.")]
    public Transform viewer;
    public float activeDistance = 150f;
    public float checkInterval = 0.5f;

    struct Emitter
    {
        public GameObject go;
        public Vector3 position;
    }

    readonly List<Emitter> emitters = new List<Emitter>();
    Transform container;
    float timer;

    void Start()
    {
        if (terrain == null) terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            Debug.LogWarning("TreeLeafSpawner: no Terrain found.");
            enabled = false;
            return;
        }
        if (leafParticlesPrefab == null)
        {
            Debug.LogWarning("TreeLeafSpawner: assign the Leaves Particles prefab.");
            enabled = false;
            return;
        }
        if (viewer == null && Camera.main != null) viewer = Camera.main.transform;

        TerrainData data = terrain.terrainData;
        TreePrototype[] prototypes = data.treePrototypes;

        if (logPrototypesOnStart)
        {
            for (int i = 0; i < prototypes.Length; i++)
            {
                string n = prototypes[i].prefab != null ? prototypes[i].prefab.name : "(none)";
                Debug.Log($"TreeLeafSpawner - tree prototype [{i}] = {n}");
            }
        }

        float[] prototypeHeights = MeasurePrototypeHeights(prototypes);

       
        TreeInstance[] trees = data.treeInstances;
        bool filter = treePrototypeIndexes != null && treePrototypeIndexes.Count > 0;
        List<int> candidates = new List<int>();
        for (int i = 0; i < trees.Length; i++)
        {
            if (filter && !treePrototypeIndexes.Contains(trees[i].prototypeIndex)) continue;
            candidates.Add(i);
        }

       
        System.Random rng = new System.Random(randomSeed);
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int tmp = candidates[i];
            candidates[i] = candidates[j];
            candidates[j] = tmp;
        }

        int target = Mathf.Min(maxEmitters, Mathf.CeilToInt(candidates.Count * spawnChance));

        container = new GameObject("Tree Leaf Emitters").transform;

        for (int c = 0; c < target; c++)
        {
            TreeInstance tree = trees[candidates[c]];

            Vector3 basePos = terrain.transform.position + Vector3.Scale(tree.position, data.size);

            float height = treeHeightOverride > 0f
                ? treeHeightOverride
                : prototypeHeights[Mathf.Clamp(tree.prototypeIndex, 0, prototypeHeights.Length - 1)];
            height *= tree.heightScale;

            Vector3 pos = basePos + Vector3.up * (height * heightFraction) + extraOffset;

            GameObject go = Instantiate(leafParticlesPrefab, pos, Quaternion.identity, container);
            emitters.Add(new Emitter { go = go, position = pos });
        }

        Debug.Log($"TreeLeafSpawner: created {emitters.Count} leaf emitters from {candidates.Count} matching trees.");
    }

    void Update()
    {
        if (viewer == null || emitters.Count == 0) return;

        timer += Time.unscaledDeltaTime;
        if (timer < checkInterval) return;
        timer = 0f;

        float maxSqr = activeDistance * activeDistance;
        Vector3 v = viewer.position;

        for (int i = 0; i < emitters.Count; i++)
        {
            Emitter e = emitters[i];
            if (e.go == null) continue;
            bool near = (e.position - v).sqrMagnitude <= maxSqr;
            if (e.go.activeSelf != near) e.go.SetActive(near);
        }
    }

    void OnDestroy()
    {
        if (container != null) Destroy(container.gameObject);
    }

   
    static float[] MeasurePrototypeHeights(TreePrototype[] prototypes)
    {
        float[] heights = new float[Mathf.Max(1, prototypes.Length)];
        for (int i = 0; i < prototypes.Length; i++)
        {
            float best = 0f;
            GameObject prefab = prototypes[i].prefab;
            if (prefab != null)
            {
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    float h = mf.sharedMesh.bounds.size.y * Mathf.Abs(mf.transform.lossyScale.y);
                    if (h > best) best = h;
                }
            }
            heights[i] = best > 0.5f ? best : 12f; 
        }
        return heights;
    }
}