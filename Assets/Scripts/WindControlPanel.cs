using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class WindControlPanel : MonoBehaviour
{
    [Header("Wind Settings")]
    public bool windEnabled = true;
    [Tooltip("Direction the wind blows TOWARD, in degrees. 0 = +Z (north), 90 = +X (east).")]
    [Range(0f, 360f)] public float direction = 45f;
    [Tooltip("How hard the wind blows. Drives WindZone 'Wind Main' (pines, leaves, camera).")]
    [Range(0f, 15f)] public float strength = 3f;
    [Tooltip("How fast things sway. Drives gust frequency + grass 'Wind Speed'.")]
    [Range(0f, 10f)] public float speed = 3f;
    [Tooltip("Grass / bush sway amplitude. Drives the shader's 'Wind Intensity'.")]
    [Range(0f, 0.2f)] public float intensity = 0.04f;
    [Tooltip("Randomness of the wind. Drives WindZone turbulence + grass 'Wind Turbulence'.")]
    [Range(0f, 1f)] public float turbulence = 0.2f;
    [Tooltip("Gust size. Drives WindZone 'Pulse Magnitude'.")]
    [Range(0f, 2f)] public float gust = 0.5f;

    [Header("Grass Shader (advanced)")]
    [Range(0f, 50f)] public float wavelength = 10f;
    [Range(0f, 1f)] public float blast = 0.05f;
    [Range(0f, 1f)] public float ripples = 0.05f;
    [Tooltip("If grass sways in a different direction than the trees, change this (try 180).")]
    [Range(0f, 360f)] public float grassYawOffset = 0f;
    [Tooltip("Tick if grass direction is mirrored compared to the trees.")]
    public bool invertGrassYaw = false;
    [Tooltip("Grass intensity is multiplied by (Strength / Strength Reference) so grass reacts to Strength too.")]
    public bool linkGrassToStrength = true;
    public float strengthReference = 3f;

    [Header("Mapping")]
    [Tooltip("WindZone turbulence = Turbulence slider x this.")]
    public float zoneTurbulenceMax = 3f;
    [Tooltip("WindZone pulse frequency = Speed slider x this.")]
    public float pulseFrequencyPerSpeed = 0.1f;
    [Tooltip("Seconds to ease between values. 0 = instant.")]
    [Range(0f, 2f)] public float smoothTime = 0.3f;

    [Header("Sources & Integration")]
    [Tooltip("Leave empty to auto-find the WindZone in the scene.")]
    public WindZone windZone;
    [Tooltip("Leave empty to auto-find the FreeFlyCamera in the scene.")]
    public FreeFlyCamera freeFlyCamera;
    public bool includeDetailMaterials = true;
    public bool includeTreeMaterials = true;
    public bool includeSceneRenderers = true;
    [Tooltip("Drag any extra materials here if the panel doesn't find them automatically.")]
    public List<Material> extraMaterials = new List<Material>();
    [Tooltip("Puts material values back when you stop the game (material edits in Play Mode otherwise persist).")]
    public bool restoreMaterialsOnExit = true;
    public bool logFoundMaterials = true;

    [Header("Audio References")]
    public AudioSource windAudioSource;
    public AudioClip calmSound;
    public AudioClip breezeSound;
    public AudioClip windySound;
    public AudioClip stormSound;
    public bool autoSwitchAudioWithPreset = true;

    [Header("Built-in Panel (IMGUI)")]
    [Tooltip("Untick if you use your own UI Canvas.")]
    public bool enableBuiltInPanel = true;
    public bool showPanel = true;
    public KeyCode toggleKey = KeyCode.F2;
    [Range(0.75f, 2.5f)] public float uiScale = 1.25f;
    public Rect windowRect = new Rect(20f, 20f, 340f, 10f);

    const int WindowId = 7184321;

    const int Speed = 0, Intensity = 1, Turbulence = 2, Wavelength = 3, Blast = 4, Ripples = 5, Yaw = 6, Multiplier = 7, PropCount = 8;

    static readonly string[] PropKeys =
    {
        "windspeed", "windintensity", "windturbulence", "windwavelength",
        "windblast", "windripples", "windyaw", "windmultiplier"
    };

    class MatBinding
    {
        public Material mat;
        public int[] ids = new int[PropCount];
        public float[] original = new float[PropCount];
        public MatBinding() { for (int i = 0; i < PropCount; i++) ids[i] = -1; }
    }

    struct Preset
    {
        public string name;
        public float strength, speed, intensity, turbulence, gust;
        public Preset(string n, float st, float sp, float it, float tu, float gu)
        { name = n; strength = st; speed = sp; intensity = it; turbulence = tu; gust = gu; }
    }

    static readonly Preset[] Presets =
    {
        new Preset("Calm",   0.5f, 1f, 0.03f, 0.05f, 0.1f),
        new Preset("Breeze", 2f,   2f, 0.04f, 0.2f,  0.3f),
        new Preset("Windy",  5f,   4f, 0.05f, 0.4f,  0.7f),
        new Preset("Storm",  10f,  7f, 0.08f, 0.8f,  1.4f),
    };

    readonly List<MatBinding> bindings = new List<MatBinding>();
    readonly float[] values = new float[PropCount];

    float cDir, cStrength, cSpeed, cIntensity, cTurb, cGust, cBlast, cRipples;
    bool initialized;
    bool showAdvanced;
    bool showOutput;
    readonly Dictionary<string, string> fieldBuffers = new Dictionary<string, string>();

    void Start()
    {
        if (windZone == null) windZone = FindFirstObjectByType<WindZone>();
        if (windZone != null) windZone.mode = WindZoneMode.Directional;
        else Debug.LogWarning("WindControlPanel: no WindZone found in the scene (pines/leaves won't react).");

        if (freeFlyCamera == null) freeFlyCamera = FindFirstObjectByType<FreeFlyCamera>();

        if (windAudioSource == null) windAudioSource = GetComponent<AudioSource>();

        BuildMaterialBindings();
        SnapToTargets();
        initialized = true;
    }

    void OnDisable()
    {
        if (!restoreMaterialsOnExit) return;
        foreach (MatBinding b in bindings)
        {
            if (b.mat == null) continue;
            for (int k = 0; k < PropCount; k++)
                if (b.ids[k] >= 0) b.mat.SetFloat(b.ids[k], b.original[k]);
        }
    }

    void Update()
    {
        if (!initialized) return;

        float on = windEnabled ? 1f : 0f;
        float k = smoothTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothTime);

        cDir = Mathf.LerpAngle(cDir, direction, k);
        cStrength = Mathf.Lerp(cStrength, strength * on, k);
        cSpeed = Mathf.Lerp(cSpeed, speed, k);
        cIntensity = Mathf.Lerp(cIntensity, intensity * on, k);
        cTurb = Mathf.Lerp(cTurb, turbulence * on, k);
        cGust = Mathf.Lerp(cGust, gust * on, k);
        cBlast = Mathf.Lerp(cBlast, blast * on, k);
        cRipples = Mathf.Lerp(cRipples, ripples * on, k);

        ApplyToWindZone();
        ApplyToMaterials();
    }

    public void SetDirection(float v) { direction = Mathf.Clamp(v, 0f, 360f); }
    public void SetStrength(float v) { strength = Mathf.Max(0f, v); }
    public void SetSpeed(float v) { speed = Mathf.Max(0f, v); }
    public void SetIntensity(float v) { intensity = Mathf.Max(0f, v); }
    public void SetTurbulence(float v) { turbulence = Mathf.Clamp01(v); }
    public void SetGust(float v) { gust = Mathf.Max(0f, v); }
    public void SetWavelength(float v) { wavelength = Mathf.Max(0f, v); }
    public void SetBlast(float v) { blast = Mathf.Clamp01(v); }
    public void SetRipples(float v) { ripples = Mathf.Clamp01(v); }
    public void SetWindEnabled(bool on) { windEnabled = on; }

    public void ApplyPreset(int index)
    {
        if (index < 0 || index >= Presets.Length) return;
        Preset p = Presets[index];
        strength = p.strength;
        speed = p.speed;
        intensity = p.intensity;
        turbulence = p.turbulence;
        gust = p.gust;
        windEnabled = true;

        if (autoSwitchAudioWithPreset)
        {
            PlayWindAudio(index);
        }
    }

    public void PlayWindAudio(int presetIndex)
    {
        if (windAudioSource == null) return;

        AudioClip clipToPlay = presetIndex switch
        {
            0 => calmSound,
            1 => breezeSound,
            2 => windySound,
            3 => stormSound,
            _ => null
        };

        if (clipToPlay != null && windAudioSource.clip != clipToPlay)
        {
            windAudioSource.clip = clipToPlay;
            if (!windAudioSource.isPlaying)
            {
                windAudioSource.Play();
            }
        }
    }

    public void PointWindAwayFromCamera() { direction = CameraYaw(); }
    public void PointWindTowardCamera() { direction = Mathf.Repeat(CameraYaw() + 180f, 360f); }
    public string GetDirectionName() { return Compass(direction); }

    void ApplyToWindZone()
    {
        if (windZone == null) return;
        windZone.transform.rotation = Quaternion.Euler(0f, cDir, 0f);
        windZone.windMain = cStrength;
        windZone.windTurbulence = cTurb * zoneTurbulenceMax;
        windZone.windPulseMagnitude = cGust;
        windZone.windPulseFrequency = cSpeed * pulseFrequencyPerSpeed;
    }

    void ApplyToMaterials()
    {
        if (bindings.Count == 0) return;

        float link = linkGrassToStrength ? cStrength / Mathf.Max(0.01f, strengthReference) : 1f;

        values[Speed] = cSpeed;
        values[Intensity] = cIntensity * link;
        values[Turbulence] = cTurb;
        values[Wavelength] = wavelength;
        values[Blast] = cBlast;
        values[Ripples] = cRipples;
        values[Yaw] = Mathf.Repeat((invertGrassYaw ? -cDir : cDir) + grassYawOffset, 360f);

        values[Multiplier] = 1f + (cStrength * 0.4f) + (cTurb * 1.5f);

        foreach (MatBinding b in bindings)
        {
            if (b.mat == null) continue;
            for (int i = 0; i < PropCount; i++)
                if (b.ids[i] >= 0) b.mat.SetFloat(b.ids[i], values[i]);
        }
    }

    void SnapToTargets()
    {
        float on = windEnabled ? 1f : 0f;
        cDir = direction;
        cStrength = strength * on;
        cSpeed = speed;
        cIntensity = intensity * on;
        cTurb = turbulence * on;
        cGust = gust * on;
        cBlast = blast * on;
        cRipples = ripples * on;
    }

    void BuildMaterialBindings()
    {
        HashSet<Material> found = new HashSet<Material>();

        foreach (Terrain t in Terrain.activeTerrains)
        {
            TerrainData td = t.terrainData;
            if (td == null) continue;

            if (includeDetailMaterials)
                foreach (DetailPrototype d in td.detailPrototypes)
                    if (d.usePrototypeMesh && d.prototype != null) AddRenderers(d.prototype, found);

            if (includeTreeMaterials)
                foreach (TreePrototype p in td.treePrototypes)
                    if (p.prefab != null) AddRenderers(p.prefab, found);
        }

        if (includeSceneRenderers)
        {
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (Material m in r.sharedMaterials)
                    if (m != null) found.Add(m);
        }

        foreach (Material m in extraMaterials)
            if (m != null) found.Add(m);

        foreach (Material m in found)
        {
            MatBinding b = TryBind(m);
            if (b != null) bindings.Add(b);
        }

        if (bindings.Count == 0)
        {
            Debug.LogWarning("WindControlPanel: no materials with wind properties found.");
        }
        else if (logFoundMaterials)
        {
            foreach (MatBinding b in bindings)
            {
                string props = "";
                for (int i = 0; i < PropCount; i++)
                    if (b.ids[i] >= 0) props += PropKeys[i].Replace("wind", "") + " ";
                Debug.Log($"WindControlPanel: driving material '{b.mat.name}' -> {props}");
            }
        }
    }

    static void AddRenderers(GameObject go, HashSet<Material> set)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
                if (m != null) set.Add(m);
    }

    static string Normalize(string s)
    {
        return s.ToLowerInvariant().Replace(" ", "").Replace("_", "");
    }

    static MatBinding TryBind(Material m)
    {
        Shader sh = m.shader;
        if (sh == null) return null;

        MatBinding b = new MatBinding { mat = m };
        bool any = false;
        int count = sh.GetPropertyCount();

        for (int i = 0; i < count; i++)
        {
            UnityEngine.Rendering.ShaderPropertyType type = sh.GetPropertyType(i);
            if (type != UnityEngine.Rendering.ShaderPropertyType.Float &&
                type != UnityEngine.Rendering.ShaderPropertyType.Range) continue;

            string internalName = sh.GetPropertyName(i);
            string n1 = Normalize(internalName);
            string n2 = Normalize(sh.GetPropertyDescription(i));

            for (int k = 0; k < PropCount; k++)
            {
                if (b.ids[k] >= 0) continue;
                if (n1 == PropKeys[k] || n2 == PropKeys[k])
                {
                    int id = Shader.PropertyToID(internalName);
                    b.ids[k] = id;
                    b.original[k] = m.GetFloat(id);
                    any = true;
                }
            }
        }
        return any ? b : null;
    }

    void OnGUI()
    {
        if (!enableBuiltInPanel) return;

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == toggleKey)
        {
            showPanel = !showPanel;
            e.Use();
        }
        if (!showPanel) return;

        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Wind Control Panel  (" + toggleKey + " to hide)",
                                      GUILayout.Width(390f));

        windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width / uiScale - 80f));
        windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height / uiScale - 40f));

        GUI.matrix = prev;
    }

    void DrawWindow(int id)
    {
        windEnabled = GUILayout.Toggle(windEnabled, " Wind enabled");

        if (freeFlyCamera != null)
        {
            bool currentStable = freeFlyCamera.stableCamera;
            bool newStable = GUILayout.Toggle(currentStable, " Stable camera (lock wind tilt/shake)");
            if (newStable != currentStable)
            {
                freeFlyCamera.SetStableCamera(newStable);
            }
        }

        GUILayout.Space(4f);

        GUILayout.Label("Blowing toward " + Compass(direction));
        direction = NumberField("Direction", direction, 0f, 360f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Away from me"))
            direction = CameraYaw();
        if (GUILayout.Button("Toward me"))
            direction = Mathf.Repeat(CameraYaw() + 180f, 360f);
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label("Click a box and type a value. It applies live; press Enter when done.");

        strength = NumberField("Strength", strength, 0f, 100f);
        speed = NumberField("Speed", speed, 0f, 100f);
        intensity = NumberField("Intensity", intensity, 0f, 1f);
        turbulence = NumberField("Turbulence", turbulence, 0f, 1f);
        gust = NumberField("Gusts", gust, 0f, 10f);

        GUILayout.Space(6f);
        GUILayout.Label("Presets");
        GUILayout.BeginHorizontal();
        foreach (Preset p in Presets)
        {
            if (GUILayout.Button(p.name))
            {
                strength = p.strength;
                speed = p.speed;
                intensity = p.intensity;
                turbulence = p.turbulence;
                gust = p.gust;
                windEnabled = true;

                int idx = System.Array.IndexOf(Presets, p);
                if (autoSwitchAudioWithPreset && idx >= 0)
                {
                    PlayWindAudio(idx);
                }
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        showAdvanced = GUILayout.Toggle(showAdvanced, showAdvanced ? " [-] Grass shader (advanced)" : " [+] Grass shader (advanced)", GUI.skin.button);
        if (showAdvanced)
        {
            wavelength = NumberField("Wavelength", wavelength, 0f, 200f);
            blast = NumberField("Blast", blast, 0f, 1f);
            ripples = NumberField("Ripples", ripples, 0f, 1f);
            grassYawOffset = NumberField("Yaw offset", grassYawOffset, 0f, 360f);
            smoothTime = NumberField("Smoothing", smoothTime, 0f, 5f);
            invertGrassYaw = GUILayout.Toggle(invertGrassYaw, " Invert grass direction");
            linkGrassToStrength = GUILayout.Toggle(linkGrassToStrength, " Grass intensity follows Strength");
        }

        GUILayout.Space(4f);
        showOutput = GUILayout.Toggle(showOutput, showOutput ? " [-] Live output (values being applied)" : " [+] Live output (values being applied)", GUI.skin.button);
        if (showOutput)
        {
            if (windZone != null)
            {
                GUILayout.Label("WindZone Main: " + windZone.windMain.ToString("0.00") +
                                "    Turbulence: " + windZone.windTurbulence.ToString("0.00"));
                GUILayout.Label("WindZone Pulse Magnitude: " + windZone.windPulseMagnitude.ToString("0.00") +
                                "    Pulse Frequency: " + windZone.windPulseFrequency.ToString("0.00"));
            }
            GUILayout.Label("Tree Wind Multiplier: " + values[Multiplier].ToString("0.00"));
            if (freeFlyCamera != null)
            {
                GUILayout.Label("Camera Stable Mode: " + freeFlyCamera.stableCamera);
            }
        }

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    float NumberField(string label, float value, float min, float max)
    {
        string controlName = "windfield_" + label;

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(90f));

        GUI.SetNextControlName(controlName);
        bool focused = GUI.GetNameOfFocusedControl() == controlName;

        Event e = Event.current;
        if (focused && e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            GUI.FocusControl(null);
            e.Use();
        }

        string shown = value.ToString("0.###", CultureInfo.InvariantCulture);
        if (focused && fieldBuffers.TryGetValue(controlName, out string buffered))
            shown = buffered;

        string edited = GUILayout.TextField(shown, GUILayout.ExpandWidth(true));

        if (edited != shown)
        {
            fieldBuffers[controlName] = edited;
            if (float.TryParse(edited.Replace(',', '.'), NumberStyles.Float,
                               CultureInfo.InvariantCulture, out float parsed))
            {
                value = Mathf.Clamp(parsed, min, max);
            }
        }
        else if (!focused)
        {
            fieldBuffers.Remove(controlName);
        }

        GUILayout.Label(min.ToString("0.##", CultureInfo.InvariantCulture) + " to " +
                        max.ToString("0.##", CultureInfo.InvariantCulture), GUILayout.Width(72f));
        GUILayout.EndHorizontal();
        return value;
    }

    static float CameraYaw()
    {
        Camera cam = Camera.main;
        return cam != null ? Mathf.Repeat(cam.transform.eulerAngles.y, 360f) : 0f;
    }

    static string Compass(float deg)
    {
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int i = Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 45f) % 8;
        return names[i];
    }
}