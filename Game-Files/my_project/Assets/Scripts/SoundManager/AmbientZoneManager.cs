using UnityEngine;
using System.Collections;

public class AmbientZoneManager : MonoBehaviour
{
    public static AmbientZoneManager Instance { get; private set; }

    [Header("Crossfade")]
    [Tooltip("How long (seconds) to fade between two ambient zones.")]
    [SerializeField] private float crossfadeDuration = 2f;

    [Header("Volume")]
    [SerializeField][Range(0f, 1f)] private float masterAmbientVolume = 0.6f;

    private AudioSource sourceA; 
    private AudioSource sourceB;

    private string currentClipName = "";
    private Coroutine fadeCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        CreateAudioSources();
    }

    public void PlayAmbient(string clipName)
    {
        if (clipName == currentClipName) return;

        AudioClip clip = Resources.Load<AudioClip>($"Sounds/{clipName}");
        if (clip == null)
        {
            Debug.LogWarning($"[AmbientZoneManager] Clip not found: '{clipName}'. Make sure it is in Assets/Resources/Sounds/");
            return;
        }

        currentClipName = clipName;

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(Crossfade(clip));
    }

    /// <summary>
    /// Fade out all ambient audio (e.g. entering an interior with no zone).
    /// </summary>
    public void StopAmbient()
    {
        if (currentClipName == "") return;
        currentClipName = "";
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeOut(sourceA));
        fadeCoroutine = StartCoroutine(FadeOut(sourceB));
    }

    /// <summary>Set the master ambient volume (0–1).</summary>
    public void SetVolume(float volume)
    {
        masterAmbientVolume = Mathf.Clamp01(volume);
        // Live-update whichever source is currently active
        if (sourceA.isPlaying) sourceA.volume = masterAmbientVolume;
        if (sourceB.isPlaying) sourceB.volume = masterAmbientVolume;
    }

    // ── Internals ─────────────────────────────────────────────────────────

    private IEnumerator Crossfade(AudioClip newClip)
    {
        // sourceB will be the new one; swap references afterward
        sourceB.clip = newClip;
        sourceB.volume = 0f;
        sourceB.Play();

        float elapsed = 0f;
        float startVolA = sourceA.volume;

        while (elapsed < crossfadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / crossfadeDuration);

            sourceA.volume = Mathf.Lerp(startVolA, 0f, t);
            sourceB.volume = Mathf.Lerp(0f, masterAmbientVolume, t);

            yield return null;
        }

        sourceA.Stop();
        sourceA.volume = 0f;

        // Swap so sourceA is always "the current one"
        (sourceA, sourceB) = (sourceB, sourceA);
    }

    private IEnumerator FadeOut(AudioSource source)
    {
        float startVolume = source.volume;
        float elapsed = 0f;

        while (elapsed < crossfadeDuration)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / crossfadeDuration);
            yield return null;
        }

        source.Stop();
        source.volume = 0f;
    }

    private void CreateAudioSources()
    {
        sourceA = gameObject.AddComponent<AudioSource>();
        sourceA.loop = true;
        sourceA.playOnAwake = false;
        sourceA.volume = 0f;

        sourceB = gameObject.AddComponent<AudioSource>();
        sourceB.loop = true;
        sourceB.playOnAwake = false;
        sourceB.volume = 0f;
    }
}