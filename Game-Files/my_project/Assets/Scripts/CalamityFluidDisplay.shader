// CalamityFluidDisplay.shader
// Renders the fluid density field as dark mist on a world-space quad.
// Separate file — Unity requires one Shader block per .shader file.

Shader "Hidden/CalamityFluidDisplay"
{
    Properties
    {
        _DensityTex   ("Density",      2D)    = "black" {}
        _MistColor    ("Mist Color",   Color) = (0.04, 0.02, 0.06, 1)
        _MistColorAlt ("Mist Alt",     Color) = (0.06, 0.03, 0.08, 1)
        _MistOpacity  ("Mist Opacity", Float) = 0.72
        _PixelCount ("Pixel Count", Float) = 48
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _DensityTex;
            float4    _MistColor;
            float4    _MistColorAlt;
            float     _MistOpacity;
            float     _PixelCount;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Snap UV to pixel grid before sampling — enforces hard pixel boundaries
                float2 pixelUV = floor(i.uv * _PixelCount) / _PixelCount;
                float  dens    = saturate(tex2D(_DensityTex, pixelUV).r);
                float3 col  = lerp(_MistColor.rgb, _MistColorAlt.rgb, dens * 0.5);

                // Wide edge fade — quad border never visible
                float edgeFade = smoothstep(0,    0.25, i.uv.x)
                               * smoothstep(1,    0.75, i.uv.x)
                               * smoothstep(0,    0.20, i.uv.y)
                               * smoothstep(1,    0.80, i.uv.y);

                // Linear dens — thin wisps are visible, not squared away
                float alpha = dens * _MistOpacity * edgeFade;
                return float4(col, alpha);
            }
            ENDCG
        }
    }
}
