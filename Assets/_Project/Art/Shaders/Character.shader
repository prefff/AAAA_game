// Персонаж: один проход, без текстур и теней (под бойцом — мягкая тень-блоб).
// Цвет — из цвета вершин; альфа вершины — маска: ~0 обычный цвет, ~0.5 цвет команды, ~1 свечение.
// Toon-освещение от одного направленного света + окружение; ободок — для состояний (парирование, оглушение),
// вспышка — кадры попадания. Всё меняется через MaterialPropertyBlock, материал один на всех.
Shader "Game/Character"
{
    Properties
    {
        _TeamColor ("Team Color", Color) = (0.2, 0.6, 1, 1)
        _GlowColor ("Glow Color", Color) = (0.4, 0.8, 1, 1)
        _GlowIntensity ("Glow Intensity", Range(0, 6)) = 1.6
        _ShadowTint ("Shadow Tint", Color) = (0.45, 0.45, 0.6, 1)
        _RimColor ("State Rim (rgb, a = strength)", Color) = (1, 1, 1, 0)
        _BaseRim ("Base Rim", Range(0, 1)) = 0.18
        _FlashColor ("Hit Flash (rgb, a = amount)", Color) = (1, 1, 1, 0)
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
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            fixed4 _TeamColor, _GlowColor, _ShadowTint, _RimColor, _FlashColor;
            half _GlowIntensity, _BaseRim;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 normal : TEXCOORD0;
                half3 viewDir : TEXCOORD1;
                half4 color : COLOR;
                half3 ambient : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(WorldSpaceViewDir(v.vertex));
                half4 c = v.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = GammaToLinearSpace(c.rgb);
                #endif
                o.color = c;
                o.ambient = ShadeSH9(half4(o.normal, 1));
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half3 n = normalize(i.normal);
                half mask = i.color.a;
                half team = saturate(1 - abs(mask - 0.5) * 4);   // 1 около 0.5
                half glow = saturate((mask - 0.75) * 4);          // 1 около 1
                half3 albedo = i.color.rgb * lerp(half3(1, 1, 1), _TeamColor.rgb, team);

                half ndl = dot(n, _WorldSpaceLightPos0.xyz) * 0.5 + 0.5;
                half ramp = smoothstep(0.42, 0.52, ndl);
                half3 light = _LightColor0.rgb * lerp(_ShadowTint.rgb, half3(1, 1, 1), ramp) + i.ambient;
                half3 col = albedo * light;

                half fres = 1 - saturate(dot(n, normalize(i.viewDir)));
                half rim = fres * fres * fres;
                col += rim * (_BaseRim * _TeamColor.rgb + _RimColor.rgb * _RimColor.a * 2);

                col = lerp(col, _GlowColor.rgb * _GlowIntensity, glow);
                col = lerp(col, _FlashColor.rgb, _FlashColor.a);
                return half4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
