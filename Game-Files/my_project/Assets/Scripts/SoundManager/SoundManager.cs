using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton SoundManager — survives scene loads.
/// Place this script on a GameObject called "SoundManager" in your first scene.
///
/// SETUP:
///   1. Create a folder:  Assets/Sounds/
///   2. Drop all your AudioClip files into that folder.
///   3. Add this script to an empty GameObject named "SoundManager".
///   4. Call from anywhere:  SoundManager.Instance.Play("ClipName");
/// </summary>
public class SoundManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static SoundManager Instance { get; private set; }

    // ── Inspector Settings ───────────────────────────────────────────────────
    [Header("Audio Sources")]
    [Tooltip("Used for sound effects (SFX).")]
    [SerializeField] private AudioSource sfxSource;

    [Tooltip("Used for looping music tracks.")]
    [SerializeField] private AudioSource musicSource;

    [Header("Sound Library")]
    [Tooltip("Path inside Resources/ where clips live, e.g. 'Sounds'")]
    [SerializeField] private string resourcesFolder = "Sounds";

    [Tooltip("Optionally pre-load clips here so they show up in the Inspector.")]
    [SerializeField] private AudioClip[] preloadedClips;

    // ── Private State ────────────────────────────────────────────────────────
    private Dictionary<string, AudioClip> _clips = new();

    // ── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        // Enforce a single instance across all scenes
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureAudioSources();
        LoadAllClips();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Play a one-shot sound effect by clip name.</summary>
    public void Play(string clipName)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return;
        sfxSource.PlayOneShot(clip);
    }

    /// <summary>Play a sound effect at a volume between 0 and 1.</summary>
    public void Play(string clipName, float volume)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    /// <summary>Play a sound effect at a world position (positional audio).</summary>
    public void PlayAtPosition(string clipName, Vector3 position, float volume = 1f)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, Mathf.Clamp01(volume));
    }

    /// <summary>Start looping a music track. Fades out any current track immediately.</summary>
    public void PlayMusic(string clipName, bool loop = true)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return;

        musicSource.clip = clip;
        musicSource.loop = loop;
        musicSource.Play();
    }

    /// <summary>Stop the current music track.</summary>
    public void StopMusic() => musicSource.Stop();

    /// <summary>Pause the current music track.</summary>
    public void PauseMusic() => musicSource.Pause();

    /// <summary>Resume a paused music track.</summary>
    public void ResumeMusic() => musicSource.UnPause();

    /// <summary>Set SFX volume (0–1).</summary>
    public void SetSFXVolume(float volume) => sfxSource.volume = Mathf.Clamp01(volume);

    /// <summary>Set music volume (0–1).</summary>
    public void SetMusicVolume(float volume) => musicSource.volume = Mathf.Clamp01(volume);

    /// <summary>Mute / unmute all audio.</summary>
    public void SetMuted(bool muted)
    {
        sfxSource.mute = muted;
        musicSource.mute = muted;
    }

    // ── Internal Helpers ─────────────────────────────────────────────────────

    private AudioClip GetClip(string name)
    {
        if (_clips.TryGetValue(name, out AudioClip clip))
            return clip;

        // Last-chance: try loading at runtime
        clip = Resources.Load<AudioClip>($"{resourcesFolder}/{name}");
        if (clip != null)
        {
            _clips[name] = clip;
            return clip;
        }

        Debug.LogWarning($"[SoundManager] Clip not found: '{name}'. " +
                         $"Make sure it is in Assets/Resources/{resourcesFolder}/");
        return null;
    }

    private void LoadAllClips()
    {
        // 1) Load everything from Resources/Sounds/
        AudioClip[] fromResources = Resources.LoadAll<AudioClip>(resourcesFolder);
        foreach (AudioClip clip in fromResources)
            _clips[clip.name] = clip;

        // 2) Merge any Inspector-assigned clips (can override or supplement)
        if (preloadedClips != null)
            foreach (AudioClip clip in preloadedClips)
                if (clip != null)
                    _clips[clip.name] = clip;

        Debug.Log($"[SoundManager] Loaded {_clips.Count} clip(s).");
    }

    private void EnsureAudioSources()
    {
        // Auto-create AudioSources if the user forgot to assign them
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
        }
    }
}