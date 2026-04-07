// CalamityFluid.shader
// Stable Fluids with wind and wider emission.
//
// Pass layout:
//   0  - Advect velocity (obstacle-aware)
//   1  - Diffuse (Jacobi)
//   2  - Divergence
//   3  - Pressure (Jacobi) — RHS from _DivergenceTex
//   4  - Subtract pressure gradient
//   5  - Vorticity (curl)
//   6  - Vorticity confinement force
//   7  - Advect density (obstacle-aware)
//   8  - Add velocity force + wind
//   9  - Display
//   10 - Inject density

Shader "Hidden/CalamityFluid"
{
    Properties
    {
        _MainTex             ("Source",           2D)    = "black" {}
        _VelocityTex         ("Velocity",         2D)    = "black" {}
        _DensityTex          ("Density",          2D)    = "black" {}
        _PressureTex         ("Pressure",         2D)    = "black" {}
        _VorticityTex        ("Vorticity",        2D)    = "black" {}
        _DivergenceTex       ("Divergence",       2D)    = "black" {}
        _ObstacleTex         ("Obstacle Mask",    2D)    = "black" {}

        _Dt                  ("Time Step",        Float) = 0.016
        _TexelSize           ("Texel Size",       Float) = 0.0078125
        _Viscosity           ("Viscosity",        Float) = 0.0001
        _Diffusion           ("Diffusion",        Float) = 0.0002
        _DensityDissipation  ("Density Dissip.",  Float) = 0.988
        _VelocityDissipation ("Velocity Dissip.", Float) = 0.92
        _VorticityStrength   ("Vorticity",        Float) = 18.0

        _EmitterPos          ("Emitter UV",       Vector) = (0.5, 0.12, 0, 0)
        _EmitterRadius       ("Emitter Radius",   Float)  = 0.20
        _EmitterStrength     ("Emitter Strength", Float)  = 0.025
        _DensityStrength     ("Density Strength", Float)  = 0.045

        // Wind: X = horizontal strength (positive = left to right)
        //       Y = vertical strength (usually 0 or slight positive for uplift)
        _WindVelocity        ("Wind Velocity",    Vector) = (0.04, 0.005, 0, 0)

        _MistColor           ("Mist Color",       Color)  = (0.04, 0.02, 0.06, 1)
        _MistColorAlt        ("Mist Color Alt",   Color)  = (0.06, 0.03, 0.08, 1)
        _MistOpacity         ("Mist Opacity",     Float)  = 0.72
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    sampler2D _VelocityTex;
    sampler2D _DensityTex;
    sampler2D _PressureTex;
    sampler2D _VorticityTex;
    sampler2D _DivergenceTex;
    sampler2D _ObstacleTex;

    float _Dt;
    float _TexelSize;
    float _Viscosity;
    float _Diffusion;
    float _DensityDissipation;
    float _VelocityDissipation;
    float _VorticityStrength;

    float2 _EmitterPos;
    float  _EmitterRadius;
    float  _EmitterStrength;
    float  _DensityStrength;
    float2 _WindVelocity;

    float4 _MistColor;
    float4 _MistColorAlt;
    float  _MistOpacity;

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

    float IsObstacle(float2 uv)
    {
        return step(0.5, tex2D(_ObstacleTex, uv).r);
    }

    // Pass 0: Advect velocity
    float4 fragAdvectVelocity(v2f i) : SV_Target
    {
        if (IsObstacle(i.uv) > 0.5) return float4(0, 0, 0, 1);
        float2 vel  = tex2D(_VelocityTex, i.uv).xy;
        float2 prev = clamp(i.uv - vel * _Dt, _TexelSize, 1.0 - _TexelSize);
        float2 adv  = IsObstacle(prev) > 0.5 ? float2(0,0) : tex2D(_VelocityTex, prev).xy;
        return float4(adv * _VelocityDissipation, 0, 1);
    }

    // Pass 1: Diffuse
    float4 fragDiffuse(v2f i) : SV_Target
    {
        if (IsObstacle(i.uv) > 0.5) return float4(0, 0, 0, 1);
        float  alpha = (_TexelSize * _TexelSize) / (_Viscosity * _Dt + 1e-9);
        float  beta  = 4.0 + alpha;
        float2 l = tex2D(_MainTex, i.uv + float2(-_TexelSize,  0)).xy;
        float2 r = tex2D(_MainTex, i.uv + float2( _TexelSize,  0)).xy;
        float2 d = tex2D(_MainTex, i.uv + float2( 0, -_TexelSize)).xy;
        float2 u = tex2D(_MainTex, i.uv + float2( 0,  _TexelSize)).xy;
        float2 b = tex2D(_VelocityTex, i.uv).xy;
        return float4((l + r + d + u + alpha * b) / beta, 0, 1);
    }

    // Pass 2: Divergence
    float4 fragDivergence(v2f i) : SV_Target
    {
        if (IsObstacle(i.uv) > 0.5) return float4(0, 0, 0, 1);
        float2 l = tex2D(_VelocityTex, i.uv + float2(-_TexelSize,  0)).xy;
        float2 r = tex2D(_VelocityTex, i.uv + float2( _TexelSize,  0)).xy;
        float2 d = tex2D(_VelocityTex, i.uv + float2( 0, -_TexelSize)).xy;
        float2 u = tex2D(_VelocityTex, i.uv + float2( 0,  _TexelSize)).xy;
        float div = 0.5 * ((r.x - l.x) + (u.y - d.y)) / _TexelSize;
        return float4(div, 0, 0, 1);
    }

    // Pass 3: Pressure — RHS from _DivergenceTex (never ping-ponged)
    float4 fragPressure(v2f i) : SV_Target
    {
        float pL = tex2D(_MainTex, i.uv + float2(-_TexelSize,  0)).r;
        float pR = tex2D(_MainTex, i.uv + float2( _TexelSize,  0)).r;
        float pD = tex2D(_MainTex, i.uv + float2( 0, -_TexelSize)).r;
        float pU = tex2D(_MainTex, i.uv + float2( 0,  _TexelSize)).r;
        float bC = tex2D(_DivergenceTex, i.uv).r;
        return float4((pL + pR + pD + pU - (_TexelSize * _TexelSize) * bC) * 0.25, 0, 0, 1);
    }

    // Pass 4: Subtract pressure gradient
    float4 fragSubtractGradient(v2f i) : SV_Target
    {
        if (IsObstacle(i.uv) > 0.5) return float4(0, 0, 0, 1);
        float pL = tex2D(_PressureTex, i.uv + float2(-_TexelSize,  0)).r;
        float pR = tex2D(_PressureTex, i.uv + float2( _TexelSize,  0)).r;
        float pD = tex2D(_PressureTex, i.uv + float2( 0, -_TexelSize)).r;
        float pU = tex2D(_PressureTex, i.uv + float2( 0,  _TexelSize)).r;
        float2 grad = float2(pR - pL, pU - pD) * (0.5 / _TexelSize);
        return float4(tex2D(_VelocityTex, i.uv).xy - grad, 0, 1);
    }

    // Pass 5: Vorticity
    float4 fragVorticity(v2f i) : SV_Target
    {
        float vL = tex2D(_VelocityTex, i.uv + float2(-_TexelSize,  0)).y;
        float vR = tex2D(_VelocityTex, i.uv + float2( _TexelSize,  0)).y;
        float uD = tex2D(_VelocityTex, i.uv + float2( 0, -_TexelSize)).x;
        float uU = tex2D(_VelocityTex, i.uv + float2( 0,  _TexelSize)).x;
        return float4(0.5 * ((vR - vL) - (uU - uD)) / _TexelSize, 0, 0, 1);
    }

    // Pass 6: Vorticity confinement force
    float4 fragVorticityForce(v2f i) : SV_Target
    {
        if (IsObstacle(i.uv) > 0.5) return float4(0, 0, 0, 1);
        float cL = abs(tex2D(_VorticityTex, i.uv + float2(-_TexelSize,  0)).r);
        float cR = abs(tex2D(_VorticityTex, i.uv + float2( _TexelSize,  0)).r);
        float cD = abs(tex2D(_VorticityTex, i.uv + float2( 0, -_TexelSize)).r);
        float cU = abs(tex2D(_VorticityTex, i.uv + float2( 0,  _TexelSize)).r);
        float cC = tex2D(_VorticityTex, i.uv).r;
        float2 eta = float2(cR - cL, cU - cD) * 0.5;
        float2 psi = eta / (length(eta) + 1e-5);
        float2 force = _VorticityStrength * float2(psi.y, -psi.x) * cC * _TexelSize;
        return float4(tex2D(_VelocityTex, i.uv).xy + force * _Dt, 0, 1);
    }

    // Pass 7: Advect density
    float4 fragAdvectDensity(v2f i) : SV_Target
    {
        if (IsObstacle(i.uv) > 0.5) return float4(0, 0, 0, 1);
        float2 vel  = tex2D(_VelocityTex, i.uv).xy;
        float2 prev = clamp(i.uv - vel * _Dt, _TexelSize, 1.0 - _TexelSize);
        float  dens = IsObstacle(prev) > 0.5 ? 0 : tex2D(_DensityTex, prev).r;
        return float4(dens * _DensityDissipation, 0, 0, 1);
    }

    // Pass 8: Add force — upward emitter blob + global wind across whole field
    float4 fragAddForce(v2f i) : SV_Target
    {
        if (IsObstacle(i.uv) > 0.5) return float4(0, 0, 0, 1);
        float2 vel  = tex2D(_MainTex, i.uv).xy;

        // Gaussian emitter blob — upward force at base
        float  dist  = length(i.uv - _EmitterPos);
        float  disc  = exp(-dist * dist / (_EmitterRadius * _EmitterRadius * 0.5));
        float2 emitForce;
        emitForce.y  = disc * _EmitterStrength;
        emitForce.x  = disc * _EmitterStrength * 0.25
                     * sin(i.uv.x * 47.3 + i.uv.y * 31.7);

        // Wind — constant force across the entire field, not just the emitter
        // Fades slightly toward the bottom so ground-level density rises before
        // being blown sideways (more natural looking)
        float  heightFade = smoothstep(0.0, 0.25, i.uv.y);
        float2 windForce  = _WindVelocity * heightFade;

        return float4(vel + emitForce + windForce, 0, 1);
    }

    // Pass 9: Display
    float4 fragDisplay(v2f i) : SV_Target
    {
        float  dens = saturate(tex2D(_DensityTex, i.uv).r);
        float3 col  = lerp(_MistColor.rgb, _MistColorAlt.rgb, dens * 0.6);

        float edgeFade = smoothstep(0,    0.25, i.uv.x)
                       * smoothstep(1,    0.75, i.uv.x)
                       * smoothstep(0,    0.20, i.uv.y)
                       * smoothstep(1,    0.80, i.uv.y);

        return float4(col, dens * _MistOpacity * edgeFade);
    }

    // Pass 10: Inject density
    float4 fragInjectDensity(v2f i) : SV_Target
    {
        if (IsObstacle(i.uv) > 0.5) return float4(0, 0, 0, 1);
        float dens = tex2D(_MainTex, i.uv).r;
        float dist = length(i.uv - _EmitterPos);
        float blob = exp(-dist * dist / (_EmitterRadius * _EmitterRadius * 0.5));
        return float4(dens + blob * _DensityStrength, 0, 0, 1);
    }

    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0: Advect velocity
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragAdvectVelocity
            ENDCG
        }

        // Pass 1: Diffuse
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDiffuse
            ENDCG
        }

        // Pass 2: Divergence
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDivergence
            ENDCG
        }

        // Pass 3: Pressure
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPressure
            ENDCG
        }

        // Pass 4: Subtract gradient
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragSubtractGradient
            ENDCG
        }

        // Pass 5: Vorticity
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragVorticity
            ENDCG
        }

        // Pass 6: Vorticity confinement force
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragVorticityForce
            ENDCG
        }

        // Pass 7: Advect density
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragAdvectDensity
            ENDCG
        }

        // Pass 8: Add velocity force + wind
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragAddForce
            ENDCG
        }

        // Pass 9: Display
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDisplay
            ENDCG
        }

        // Pass 10: Inject density
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragInjectDensity
            ENDCG
        }
    }
}
