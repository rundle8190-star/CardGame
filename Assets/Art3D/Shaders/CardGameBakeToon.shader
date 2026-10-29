// 캐릭터 도트 굽기 전용 셰이더. 게임 화면에는 쓰지 않는다.
// - 빛 방향은 굽기 툴이 전역 값(_CGBakeLightDir)으로 넣어 준다. (카메라 기준으로 고정)
// - 밝기를 3단계로 끊어서 도트처럼 또렷한 명암을 만든다.
// - _DepthPush: 카메라에서 멀어지는 쪽으로 깊이를 밀어서, 옷 밑에 있는 맨몸이 옷을 뚫고 나오지 않게 한다.
Shader "Hidden/CardGame/BakeToon"
{
    Properties
    {
        _MainTex ("텍스처", 2D) = "white" {}
        _Color ("색", Color) = (1,1,1,1)
        _DepthPush ("깊이 밀기(m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _DepthPush;

            float4 _CGBakeLightDir;   // xyz: 빛이 오는 쪽(월드)
            float4 _CGBakeShade;      // x: 밝은면 기준, y: 중간면 기준, z: 중간 밝기, w: 그림자 밝기
            float _CGBakeMipBias;     // 텍스처를 더 흐린 밉맵으로 읽기 (잔무늬 반짝임 방지)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 nrm : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 viewPos = UnityObjectToViewPos(v.vertex.xyz);
                viewPos.z -= _DepthPush; // 뷰 공간은 -z가 앞쪽이므로 빼면 뒤로 밀린다
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.nrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 albedo = tex2Dbias(_MainTex, float4(i.uv, 0, _CGBakeMipBias)) * _Color;
                float ndl = dot(normalize(i.nrm), normalize(_CGBakeLightDir.xyz));
                float shade = ndl > _CGBakeShade.x ? 1.0 : (ndl > _CGBakeShade.y ? _CGBakeShade.z : _CGBakeShade.w);
                return fixed4(albedo.rgb * shade, 1.0);
            }
            ENDCG
        }
    }
}
