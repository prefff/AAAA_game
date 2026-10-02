// Эффекты боя: текстура (белая с альфой) × цвет вершины/частицы × _Color × _Intensity.
// Additive — вспышки, искры, свечение снарядов; Alpha — пыль, тени-блобы, заливка областей.
// Без освещения, без записи глубины, без отсечения граней — самый дешёвый прозрачный проход.
Shader "Game/FxAdditive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Intensity;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 t = tex2D(_MainTex, i.uv) * i.color;
                return half4(t.rgb * _Intensity, t.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
