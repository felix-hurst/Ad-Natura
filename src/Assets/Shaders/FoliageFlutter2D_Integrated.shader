Shader "PixelArt/FoliageFlutter2D_Integrated"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        [Header(Wind Response)]
        _WindResponse ("Wind Response", Range(0, 1)) = 0.5
        _IdleSwayAmount ("Idle Sway Amount", Range(0, 0.1)) = 0.01
        _IdleSwaySpeed ("Idle Sway Speed", Range(0, 3)) = 0.8
        _Smoothness ("Motion Smoothness", Range(0.5, 3)) = 1.2
        
        [Header(Leaf Flutter)]
        _FlutterAmount ("Flutter Amount", Range(0, 0.05)) = 0.015
        _FlutterSpeed ("Flutter Speed", Range(0, 10)) = 4.0
        _FlutterScale ("Flutter Scale", Range(1, 20)) = 8.0
        _FlutterVariation ("Flutter Variation", Range(0, 5)) = 2.0
        
        [Header(Bending)]
        _BendFromBase ("Bend From Base", Range(0, 0.5)) = 0.0
        _BendCurve ("Bend Curve Power", Range(1, 3)) = 1.5
        _MaxBendX ("Max Bend Horizontal", Range(0, 1)) = 0.15
        _MaxBendY ("Max Bend Vertical", Range(0, 1)) = 0.05
        
        [Header(Variation)]
        _PhaseVariation ("Phase Variation", Range(0, 10)) = 3.0
        _StrengthVariation ("Strength Variation", Range(0, 0.5)) = 0.2
        
        [Header(Interaction)]
        _InteractionDisplacement ("Interaction Displacement", Vector) = (0,0,0,0)
        
        [Header(Wind Data)]
        _WindVelocity ("Wind Velocity", Vector) = (0,0,0,0)
        _WindStrength ("Wind Strength", Float) = 0
        _IsGusting ("Is Gusting", Float) = 0
        
        // === NEW: OVERLAY PROPERTIES ===
        [Header(Color Overlay)]
        _OverlayColor ("Overlay Color", Color) = (1,1,1,1)
        _OverlayStrength ("Overlay Strength", Range(0, 1)) = 0
        
        [Header(Pixel Art)]
        _PixelSnap ("Pixel Snap", Float) = 0
        
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _RendererColor;
            
            // Sway parameters
            float _WindResponse;
            float _IdleSwayAmount;
            float _IdleSwaySpeed;
            float _Smoothness;
            
            // Flutter parameters
            float _FlutterAmount;
            float _FlutterSpeed;
            float _FlutterScale;
            float _FlutterVariation;
            
            // Bending parameters
            float _BendFromBase;
            float _BendCurve;
            float _MaxBendX;
            float _MaxBendY;
            
            // Variation
            float _PhaseVariation;
            float _StrengthVariation;
            
            // Wind data (set by FoliageWindReceiver script)
            float4 _WindVelocity;
            float _WindStrength;
            float _IsGusting;
            
            // Interaction (set by InteractiveFoliage script)
            float4 _InteractionDisplacement;
            
            // === NEW: OVERLAY ===
            fixed4 _OverlayColor;
            float _OverlayStrength;

            // Hash function for variation
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            
            // 2D noise function for flutter
            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                
                // Smoothstep for interpolation
                float2 u = f * f * (3.0 - 2.0 * f);
                
                // Four corners
                float a = hash(i);
                float b = hash(i + float2(1.0, 0.0));
                float c = hash(i + float2(0.0, 1.0));
                float d = hash(i + float2(1.0, 1.0));
                
                // Bilinear interpolation
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }
            
            // Fractal Brownian Motion for organic flutter
            float fbm(float2 p, float time)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                
                // Layer 1 - slow, large movement
                value += amplitude * sin(noise(p * frequency) * 6.28 + time * 0.7);
                
                // Layer 2 - medium
                amplitude *= 0.5;
                frequency *= 2.0;
                value += amplitude * sin(noise(p * frequency + 50.0) * 6.28 + time * 1.1);
                
                // Layer 3 - fast, small flutter
                amplitude *= 0.5;
                frequency *= 2.0;
                value += amplitude * sin(noise(p * frequency + 100.0) * 6.28 + time * 1.7);
                
                return value;
            }
            
            // Organic sway using layered sine waves
            float2 organicSway(float time, float phase, float amount)
            {
                float primary = sin(time * 0.7 + phase) * 0.6;
                float secondary = sin(time * 1.3 + phase * 1.7) * 0.3;
                float tertiary = sin(time * 2.1 + phase * 0.5) * 0.1;
                
                float x = (primary + secondary + tertiary) * amount;
                
                float yPrimary = cos(time * 0.5 + phase * 1.2) * 0.5;
                float ySecondary = cos(time * 1.1 + phase) * 0.3;
                float yTertiary = sin(time * 1.8 + phase * 0.8) * 0.2;
                
                float y = (yPrimary + ySecondary + yTertiary) * amount * 0.3;
                
                return float2(x, y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Get world position for variation
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                
                // Calculate unique variation per plant based on position
                float2 plantId = floor(worldPos.xy * 0.5);
                float phaseOffset = hash(plantId) * 6.28318 * _PhaseVariation;
                float strengthMod = 1.0 + (hash(plantId + 100) - 0.5) * _StrengthVariation * 2.0;
                
                // Calculate height factor for bending (can be disabled with BendFromBase = 0)
                float heightFactor = 1.0;
                if (_BendFromBase > 0.001)
                {
                    heightFactor = saturate((v.texcoord.y - _BendFromBase) / (1.0 - _BendFromBase + 0.001));
                    heightFactor = pow(heightFactor, _BendCurve);
                    heightFactor = smoothstep(0, 1, heightFactor);
                }
                
                // === LEAF FLUTTER ===
                float2 flutterUV = v.texcoord * _FlutterScale;
                float flutterTime = _Time.y * _FlutterSpeed;
                flutterUV += worldPos.xy * 0.1;
                
                float flutterX = fbm(flutterUV, flutterTime + phaseOffset);
                float flutterY = fbm(flutterUV + float2(50.0, 50.0), flutterTime * 0.8 + phaseOffset);
                
                float localVariation = hash(flutterUV) * _FlutterVariation;
                flutterX *= (1.0 + sin(flutterTime * 0.5 + localVariation) * 0.3);
                flutterY *= (1.0 + cos(flutterTime * 0.4 + localVariation) * 0.3);
                
                float2 flutter = float2(flutterX, flutterY) * _FlutterAmount;
                
                float windFlutterBoost = 1.0 + _WindStrength * 0.5;
                flutter *= windFlutterBoost;
                
                float gustFlutterBoost = lerp(1.0, 2.0, _IsGusting);
                flutter *= gustFlutterBoost;
                
                // === WIND DISPLACEMENT ===
                float2 windDir = _WindVelocity.xy;
                float windMag = _WindStrength;
                
                float windTime = _Time.y * _Smoothness;
                float windWave1 = sin(windTime * 0.8 + phaseOffset) * 0.5 + 0.5;
                float windWave2 = sin(windTime * 1.4 + phaseOffset * 1.3) * 0.3;
                float windWave3 = sin(windTime * 2.3 + phaseOffset * 0.7) * 0.2;
                float windOscillation = 0.7 + (windWave1 + windWave2 + windWave3) * 0.3;
                
                float gustBoost = lerp(1.0, 1.3, _IsGusting);
                
                float2 windBend = windDir * windMag * _WindResponse * heightFactor * strengthMod * windOscillation * gustBoost;
                
                // === IDLE SWAY ===
                float idleTime = _Time.y * _IdleSwaySpeed;
                float2 idleSway = organicSway(idleTime, phaseOffset, _IdleSwayAmount) * heightFactor;
                
                // === INTERACTION DISPLACEMENT ===
                float2 interactionBend = _InteractionDisplacement.xy * heightFactor;
                
                // === COMBINE ALL DISPLACEMENTS ===
                float2 totalDisplacement = windBend + idleSway + interactionBend + flutter;
                
                totalDisplacement.x = clamp(totalDisplacement.x, -_MaxBendX, _MaxBendX);
                totalDisplacement.y = clamp(totalDisplacement.y, -_MaxBendY, _MaxBendY);
                
                worldPos.xy += totalDisplacement;
                o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                
                #ifdef PIXELSNAP_ON
                o.vertex = UnityPixelSnap(o.vertex);
                #endif

                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color * _RendererColor;
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample texture
                fixed4 c = tex2D(_MainTex, i.texcoord) * i.color;
                
                // === NEW: APPLY OVERLAY ===
                if (_OverlayStrength > 0.001)
                {
                    // Multiply blend - darkens/tints the sprite
                    c.rgb *= lerp(fixed3(1,1,1), _OverlayColor.rgb, _OverlayStrength);
                }
                
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
    
    Fallback "Sprites/Default"
}