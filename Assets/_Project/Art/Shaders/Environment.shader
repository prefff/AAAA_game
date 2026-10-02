// Окружение арены: одна текстура в мировых координатах (проекция по доминирующей оси нормали) — кубы и
// цилиндры любого размера без растяжения и без UV. Пол дополнительно накладывает разметку арены (альфа-текстура
// в прямоугольнике _ArenaRect) и затемняет всё за краем арены. Стены темнеют у основания — дешёвая «AO».
Shader "Game/Environment"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TexWorldSize ("Texture Size (m)", Float) = 4
        _AoHeight ("Base Darkening Height (m)", Float) = 0.6
        _AoStrength ("Base Darkening", Range(0, 1)) = 0.45
        _TopTint ("Top Faces Tint", Color) = (1, 1, 1, 1)
        [Toggle(_MARKINGS)] _UseMarkings ("Arena Markings", Float) = 0
        _Markings ("Markings (alpha)", 2D) = "black" {}
        _MarkingsColor ("Markings Color", Color) = (0.9, 0.85, 0.7, 0.7)
        _ArenaRect ("Arena Rect (xMin, zMin, xMax, zMax)", Vector) = (-11, -7, 11, 7)
        _OutsideDark ("Outside Darkening", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _MARKINGS
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            sampler2D _MainTex;
            sampler2D _Markings;
            fixed4 _Color, _TopTint, _MarkingsColor;
            float _TexWorldSize, _AoHeight;
            half _AoStrength, _OutsideDark;
            float4 _ArenaRect;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                half3 normal : TEXCOORD1;
                half3 ambient : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.ambient = ShadeSH9(half4(o.normal, 1));
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half3 n = normalize(i.normal);
                half3 an = abs(n);
                float2 uv = an.y > 0.5 ? i.world.xz : (an.x > an.z ? i.world.zy : i.world.xy);
                half3 albedo = tex2D(_MainTex, uv / _TexWorldSize).rgb * _Color.rgb;
                albedo *= lerp(half3(1, 1, 1), _TopTint.rgb, step(0.5, n.y) * step(0.05, i.world.y));

                half ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz) * 0.6 + 0.4);
                half3 col = albedo * (_LightColor0.rgb * ndl + i.ambient);

                // стены и колонны темнеют к полу
                half vertical = step(an.y, 0.5);
                col *= lerp(1, lerp(1 - _AoStrength, 1, saturate(i.world.y / _AoHeight)), vertical);

                #ifdef _MARKINGS
                float2 auv = (i.world.xz - _ArenaRect.xy) / (_ArenaRect.zw - _ArenaRect.xy);
                half inside = step(0, auv.x) * step(auv.x, 1) * step(0, auv.y) * step(auv.y, 1);
                half m = tex2D(_Markings, auv).a * inside * _MarkingsColor.a;
                col = lerp(col, _MarkingsColor.rgb * (_LightColor0.rgb * ndl + i.ambient), m);
                col *= lerp(1 - _OutsideDark, 1, inside);
                #endif
                return half4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
