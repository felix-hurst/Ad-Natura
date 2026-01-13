using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Handles interactive demo controls and mouse interactions
public class DemoController : MonoBehaviour
{
    [Header("References")]
    public SlimeMoldManager slimeManager;
    public Slime slimeSimulation;
    public Camera mainCamera;

    [Header("Source Prefabs")]
    public GameObject waterSourcePrefab;
    public GameObject lightSourcePrefab;

    [Header("UI Sliders")]
    public Slider agentCountSlider;
    public Slider moveSpeedSlider;
    public Slider turnSpeedSlider;
    public Slider evaporateSpeedSlider;
    public Slider waterStrengthSlider;
    public Slider lightStrengthSlider;

    [Header("UI Text")]
    public TextMeshProUGUI agentCountText;
    public TextMeshProUGUI moveSpeedText;
    public TextMeshProUGUI turnSpeedText;
    public TextMeshProUGUI evaporateSpeedText;
    public TextMeshProUGUI waterStrengthText;
    public TextMeshProUGUI lightStrengthText;
    public TextMeshProUGUI fpsText;

    [Header("Placement Settings")]
    public float sourceDefaultRadius = 20f;
    public float sourceDefaultStrength = 1.0f;

    private float fpsUpdateTimer = 0f;

    private void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (slimeManager == null) slimeManager = FindObjectOfType<SlimeMoldManager>();
        if (slimeSimulation == null) slimeSimulation = FindObjectOfType<Slime>();

        InitializeUI();
    }

    private void Update()
    {
        HandleMouseInput();
        UpdateFPS();

        if (Input.GetKeyDown(KeyCode.Space))
        {
            ResetSimulation();
        }
    }

    private void HandleMouseInput()
    {
        if (mainCamera == null) return;

        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0;

        // Left click - place water source
        if (Input.GetMouseButtonDown(0))
        {
            PlaceWaterSource(mouseWorldPos);
        }

        // Right click - place light source
        if (Input.GetMouseButtonDown(1))
        {
            PlaceLightSource(mouseWorldPos);
        }

        // Middle click - remove nearby sources
        if (Input.GetMouseButtonDown(2))
        {
            RemoveNearbySource(mouseWorldPos, 5f);
        }
    }

    private void PlaceWaterSource(Vector3 position)
    {
        GameObject waterObj;
        if (waterSourcePrefab != null)
        {
            waterObj = Instantiate(waterSourcePrefab, position, Quaternion.identity);
        }
        else
        {
            waterObj = new GameObject("WaterSource");
            waterObj.transform.position = position;
            waterObj.AddComponent<WaterSource>();
        }

        WaterSource water = waterObj.GetComponent<WaterSource>();
        if (water == null) water = waterObj.AddComponent<WaterSource>();
        water.attractionRadius = sourceDefaultRadius;
        water.attractionStrength = sourceDefaultStrength;
    }

    private void PlaceLightSource(Vector3 position)
    {
        GameObject lightObj;
        if (lightSourcePrefab != null)
        {
            lightObj = Instantiate(lightSourcePrefab, position, Quaternion.identity);
        }
        else
        {
            lightObj = new GameObject("LightSource");
            lightObj.transform.position = position;
            lightObj.AddComponent<LightSource>();
        }

        LightSource light = lightObj.GetComponent<LightSource>();
        if (light == null) light = lightObj.AddComponent<LightSource>();
        light.attractionRadius = sourceDefaultRadius;
        light.attractionStrength = sourceDefaultStrength;
    }

    private void RemoveNearbySource(Vector3 position, float radius)
    {
        WaterSource[] waterSources = FindObjectsOfType<WaterSource>();
        foreach (WaterSource source in waterSources)
        {
            if (Vector3.Distance(source.transform.position, position) < radius)
            {
                Destroy(source.gameObject);
            }
        }

        LightSource[] lightSources = FindObjectsOfType<LightSource>();
        foreach (LightSource source in lightSources)
        {
            if (Vector3.Distance(source.transform.position, position) < radius)
            {
                Destroy(source.gameObject);
            }
        }
    }

    private void InitializeUI()
    {
        if (agentCountSlider != null && slimeSimulation != null)
        {
            agentCountSlider.onValueChanged.AddListener(OnAgentCountChanged);
        }
        if (moveSpeedSlider != null && slimeSimulation != null)
        {
            moveSpeedSlider.onValueChanged.AddListener(OnMoveSpeedChanged);
        }
        if (turnSpeedSlider != null && slimeSimulation != null)
        {
            turnSpeedSlider.onValueChanged.AddListener(OnTurnSpeedChanged);
        }
        if (evaporateSpeedSlider != null && slimeSimulation != null)
        {
            evaporateSpeedSlider.onValueChanged.AddListener(OnEvaporateSpeedChanged);
        }
        if (waterStrengthSlider != null && slimeManager != null)
        {
            waterStrengthSlider.onValueChanged.AddListener(OnWaterStrengthChanged);
        }
        if (lightStrengthSlider != null && slimeManager != null)
        {
            lightStrengthSlider.onValueChanged.AddListener(OnLightStrengthChanged);
        }
    }

    public void OnAgentCountChanged(float value)
    {
        if (agentCountText != null) agentCountText.text = $"{(int)value}";
    }

    public void OnMoveSpeedChanged(float value)
    {
        if (moveSpeedText != null) moveSpeedText.text = $"{value:F1}";
    }

    public void OnTurnSpeedChanged(float value)
    {
        if (turnSpeedText != null) turnSpeedText.text = $"{value:F1}";
    }

    public void OnEvaporateSpeedChanged(float value)
    {
        if (evaporateSpeedText != null) evaporateSpeedText.text = $"{value:F2}";
    }

    public void OnWaterStrengthChanged(float value)
    {
        if (slimeManager != null) slimeManager.waterAttractionStrength = value;
        if (waterStrengthText != null) waterStrengthText.text = $"{value:F1}";
    }

    public void OnLightStrengthChanged(float value)
    {
        if (slimeManager != null) slimeManager.lightAttractionStrength = value;
        if (lightStrengthText != null) lightStrengthText.text = $"{value:F1}";
    }

    private void UpdateFPS()
    {
        fpsUpdateTimer += Time.deltaTime;
        if (fpsUpdateTimer >= 0.5f && fpsText != null)
        {
            float fps = 1f / Time.deltaTime;
            fpsText.text = $"FPS: {fps:F0}";
            fpsUpdateTimer = 0f;
        }
    }

    public void ResetSimulation()
    {
        // Remove all sources
        foreach (WaterSource source in FindObjectsOfType<WaterSource>())
        {
            Destroy(source.gameObject);
        }
        foreach (LightSource source in FindObjectsOfType<LightSource>())
        {
            Destroy(source.gameObject);
        }

        // Reload scene would be better but this works for demo
        Debug.Log("Reset - removed all sources. Slime will disperse.");
    }

    // Preset configurations
    public void SetCooperationPreset()
    {
        if (slimeManager == null) return;
        slimeManager.waterAttractionStrength = 15f;
        slimeManager.lightAttractionStrength = 15f;
        if (waterStrengthSlider != null) waterStrengthSlider.value = 15f;
        if (lightStrengthSlider != null) lightStrengthSlider.value = 15f;
    }

    public void SetCompetitionPreset()
    {
        if (slimeManager == null) return;
        slimeManager.waterAttractionStrength = 50f;
        slimeManager.lightAttractionStrength = 5f;
        if (waterStrengthSlider != null) waterStrengthSlider.value = 50f;
        if (lightStrengthSlider != null) lightStrengthSlider.value = 5f;
    }

    public void SetFluidPreset()
    {
        if (slimeManager == null) return;
        slimeManager.waterAttractionStrength = 10f;
        slimeManager.lightAttractionStrength = 30f;
        if (waterStrengthSlider != null) waterStrengthSlider.value = 10f;
        if (lightStrengthSlider != null) lightStrengthSlider.value = 30f;
    }
}
