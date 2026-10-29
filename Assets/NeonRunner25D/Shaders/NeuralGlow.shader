// Additive glow for the menu's neural network: vertex colour x glow texture x HDR intensity.
// Intensity rides in TEXCOORD1.x so it can go above 1 and drive bloom.
Shader "NeonRunner/NeuralGlow"
{
    Properties
    {
        _MainTex ("Glow (alpha falloff)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 glow : TEXCOORD1;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float intensity : TEXCOORD1;
                float4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.intensity = v.glow.x;
                o.color = v.color;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half a = tex2D(_MainTex, i.uv).a;
                return half4(i.color.rgb * (a * i.color.a * i.intensity), 0);
            }
            ENDCG
        }
    }
}
