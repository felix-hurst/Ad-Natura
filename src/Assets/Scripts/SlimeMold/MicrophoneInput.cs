using UnityEngine;

public class MicrophoneInput : MonoBehaviour
{
    [Header("Microphone")]
    [Tooltip("Leave empty to use default mic device")]
    [SerializeField] private string micDeviceName = "";
    [SerializeField] private int sampleRate = 44100;
    [SerializeField] private int fftSize = 1024;

    [Header("Sensitivity")]
    [Tooltip("Amplification for quiet mics. Increase if values stay near zero")]
    [SerializeField, Range(1f, 20f)] private float micGain = 5f;

    [Header("Smoothing")]
    [Tooltip("How fast values approach raw input. Higher = more responsive, lower = smoother")]
    [SerializeField, Range(1f, 20f)] private float smoothingSpeed = 5f;
    [Tooltip("How fast onset spikes decay")]
    [SerializeField, Range(1f, 20f)] private float onsetDecay = 8f;

    [Header("Debug")]
    [SerializeField] private bool showDebugValues;

    // Public read-only properties — Slime.cs reads these
    public float Amplitude { get; private set; }
    public float SpectralCentroid { get; private set; }
    public float Onset { get; private set; }
    public bool IsActive { get; private set; }

    private AudioClip micClip;
    private AudioSource hiddenSource;
    private float[] sampleBuffer;
    private float[] spectrum;
    private float prevRawAmplitude;
    private string activeDevice;

    void Start()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("MicrophoneInput: No microphone detected. Mic features disabled.");
            IsActive = false;
            return;
        }

        // null = default device (most reliable on Windows)
        activeDevice = string.IsNullOrEmpty(micDeviceName) ? null : micDeviceName;

        string deviceDisplay = activeDevice ?? Microphone.devices[0];
        Debug.Log($"MicrophoneInput: Starting mic '{deviceDisplay}'");

        // Longer clip (10s) avoids wrap-around issues
        micClip = Microphone.Start(activeDevice, true, 10, sampleRate);

        if (micClip == null)
        {
            Debug.LogError("MicrophoneInput: Microphone.Start returned null.");
            IsActive = false;
            return;
        }

        // Hidden AudioSource for spectrum analysis via Unity's FFT
        // Use a tiny volume instead of 0 — some platforms skip processing at volume 0
        hiddenSource = gameObject.AddComponent<AudioSource>();
        hiddenSource.clip = micClip;
        hiddenSource.loop = true;
        hiddenSource.volume = 0f;
        hiddenSource.mute = true;  // mute prevents audible leak/feedback; GetSpectrumData still works
        hiddenSource.Play();

        sampleBuffer = new float[fftSize];
        spectrum = new float[fftSize];
        IsActive = true;
    }

    void Update()
    {
        if (!IsActive) return;

        int micPos = Microphone.GetPosition(activeDevice);

        // Need enough data, and a small buffer behind write head to avoid race
        if (micPos < fftSize + 128) return;

        // --- RMS Amplitude ---
        int readOffset = micPos - fftSize - 64;
        micClip.GetData(sampleBuffer, readOffset);

        float sumSq = 0f;
        for (int i = 0; i < fftSize; i++)
            sumSq += sampleBuffer[i] * sampleBuffer[i];
        float rawAmplitude = Mathf.Sqrt(sumSq / fftSize) * micGain;
        rawAmplitude = Mathf.Clamp01(rawAmplitude);

        // --- Spectral Centroid (pitch brightness) ---
        // Sync hidden AudioSource to mic position for accurate spectrum
        int syncPos = Mathf.Max(0, micPos - fftSize * 2);
        if (syncPos < micClip.samples)
            hiddenSource.timeSamples = syncPos;
        hiddenSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

        float weightedSum = 0f;
        float totalEnergy = 0f;
        int halfFFT = fftSize / 2;
        for (int i = 1; i < halfFFT; i++)
        {
            weightedSum += i * spectrum[i];
            totalEnergy += spectrum[i];
        }
        float rawCentroid = totalEnergy > 0.0001f ? (weightedSum / totalEnergy) / halfFFT : 0f;
        rawCentroid = Mathf.Clamp01(rawCentroid);

        // --- Onset Detection ---
        float delta = rawAmplitude - prevRawAmplitude;
        float rawOnset = delta > 0.05f ? Mathf.Clamp01(delta * 5f) : 0f;
        prevRawAmplitude = rawAmplitude;

        // --- Smoothing ---
        float dt = Time.deltaTime;
        Amplitude = Mathf.Lerp(Amplitude, rawAmplitude, smoothingSpeed * dt);
        SpectralCentroid = Mathf.Lerp(SpectralCentroid, rawCentroid, smoothingSpeed * dt);
        Onset = Mathf.Max(rawOnset, Onset - onsetDecay * dt);

        if (showDebugValues && Time.frameCount % 30 == 0)
            Debug.Log($"[Mic] Amp:{Amplitude:F3} RawAmp:{rawAmplitude:F3} Centroid:{SpectralCentroid:F3} Onset:{Onset:F3} MicPos:{micPos}");
    }

    void OnDestroy()
    {
        if (IsActive)
            Microphone.End(activeDevice);
        if (hiddenSource != null)
            Destroy(hiddenSource);
    }
}
