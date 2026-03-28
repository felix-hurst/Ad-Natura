using UnityEngine;

public static class SlimeShaderLoader
{
    private static ComputeShader _slimeShader;

    public static ComputeShader SlimeShader
    {
        get
        {
            if (_slimeShader == null)
                Load();
            return _slimeShader;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Load()
    {
        _slimeShader = Resources.Load<ComputeShader>("Slime");

        if (_slimeShader == null)
            Debug.LogError("[SlimeShaderLoader] Failed to load Slime compute shader!");
        else
        {
            _slimeShader.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Debug.LogError($"[SlimeShaderLoader] Loaded: {_slimeShader.name}");
        }
    }
}