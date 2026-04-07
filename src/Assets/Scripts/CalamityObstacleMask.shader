// CalamityObstacleMask.shader
// Used as a replacement shader by the obstacle camera in CalamityMistFluid.
// Renders all geometry solid white so the fluid simulation knows where
// solid objects are and can block velocity/density flow through them.

Shader "Hidden/CalamityObstacleMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Everything solid white = obstacle
                return float4(1, 1, 1, 1);
            }
            ENDCG
        }
    }

    // Transparent objects use this subshader — they are NOT obstacles
    SubShader
    {
        Tags { "RenderType" = "Transparent" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            // Transparent objects = black = no obstacle
            float4 frag(v2f i) : SV_Target { return float4(0, 0, 0, 0); }
            ENDCG
        }
    }
}
