// 캐릭터 도트 굽기 전용 보조 셰이더. 게임 화면에는 쓰지 않는다.
// 1) 부위 번호 그리기: _Id.x 값을 그대로 색(빨강)으로 쓴다. _Classify가 1이면 텍스처 색을 보고
//    옷/가죽/금속/어두운 곳을 나눠 번호를 정한다 (팔레트 교체용 부위 나누기).
// 2) 가림막: _ColorMask 0 으로 깊이만 쓰고 색은 안 쓴다 (무기 레이어를 구울 때 몸이 무기를 가리게).
// _DepthPush 는 BakeToon 과 같다 (몸을 살짝 뒤로 밀기).
Shader "Hidden/CardGame/BakeFlat"
{
    Properties
    {
        _MainTex ("텍스처", 2D) = "white" {}
        _Color ("색", Color) = (1,1,1,1)
        _Id ("부위 번호", Vector) = (0,0,0,0)
        _Classify ("텍스처 색으로 부위 나누기", Float) = 0
        _DepthPush ("깊이 밀기(m)", Float) = 0
        _ColorMask ("색 쓰기 (0 = 깊이만)", Float) = 15
        _ClothRange ("천: 색상 최소·최대(도), 채도 최소·최대", Vector) = (55, 170, 0.2, 1)
        _ClothMinV ("천: 가장 어두운 밝기", Float) = 0.04
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask [_ColorMask]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _Id;
            float _Classify;
            float _DepthPush;
            float _CGBakeMipBias;
            float4 _ClothRange;
            float _ClothMinV;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                float3 viewPos = UnityObjectToViewPos(v.vertex.xyz);
                viewPos.z -= _DepthPush;
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // 부위 번호: 1 피부, 2 천(옷), 3 가죽, 4 금속, 5 어두운 곳(눈, 머리카락, 아주 어두운 색)
            float4 frag (v2f i) : SV_Target
            {
                float id = _Id.x;
                if (_Classify > 0.5)
                {
                    float3 c = tex2Dbias(_MainTex, float4(i.uv, 0, _CGBakeMipBias)).rgb * _Color.rgb;
                    #ifndef UNITY_COLORSPACE_GAMMA
                    c = LinearToGammaSpace(c); // 기준값은 화면에 보이는 색(sRGB) 기준
                    #endif
                    float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
                    float s = mx > 1e-4 ? (mx - mn) / mx : 0;
                    float h = 0;
                    if (mx - mn > 1e-4)
                    {
                        if (mx == c.r) h = fmod((c.g - c.b) / (mx - mn) + 6.0, 6.0);
                        else if (mx == c.g) h = (c.b - c.r) / (mx - mn) + 2.0;
                        else h = (c.r - c.g) / (mx - mn) + 4.0;
                        h *= 60.0;
                    }
                    // 천: 설정한 색상·채도 범위 (기본은 초록 계열, 어두운 초록 바지 포함)
                    if (h >= _ClothRange.x && h <= _ClothRange.y && s >= _ClothRange.z && s <= _ClothRange.w && mx >= _ClothMinV) id = 2;
                    else if (mx < 0.12) id = 5;          // 아주 어두운 색
                    else if (s < 0.18) id = 4;           // 회색 = 금속
                    else id = 3;                          // 갈색, 주황 = 가죽
                }
                return float4(id / 255.0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
