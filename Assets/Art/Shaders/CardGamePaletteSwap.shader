// 게임용: 캐릭터 몸 시트의 색을 부위별로 바꿔 그린다.
// 쓰는 법
//  1) SpriteRenderer 의 스프라이트를 몸 시트 대신 인덱스 시트(모션_idx.png)로 둔다. (자르기·기준점이 같다)
//  2) 이 셰이더로 머티리얼을 만들고 _PaletteTex 에 _palette_swap.png 를 넣는다.
//  3) 부위마다 몇 번 줄 색을 쓸지 정한다. 줄 번호는 _palette_swap.json 의 rows 순서 (0 기본, 1 천 파랑, 2 천 빨강, 3 피부 밝게, 4 피부 어둡게).
// 칸 범위(시작, 개수)는 _palette_swap.json 의 parts 값과 같아야 한다. 기본값은 피부 5, 천 6, 가죽 6, 금속 4, 어두운 곳 3, 윤곽선 1.
Shader "CardGame/PaletteSwap"
{
    Properties
    {
        [PerRendererData] _MainTex ("인덱스 시트", 2D) = "white" {}
        _PaletteTex ("색 교체 팔레트", 2D) = "white" {}
        _PaletteRows ("팔레트 줄 수", Float) = 5
        _RowSkin ("피부 줄", Float) = 0
        _RowCloth ("천 줄", Float) = 0
        _RowLeather ("가죽 줄", Float) = 0
        _RowMetal ("금속 줄", Float) = 0
        _RowDark ("어두운 곳 줄", Float) = 0
        _Ranges ("칸 시작: 천, 가죽, 금속, 어두운 곳", Vector) = (5, 11, 17, 21)
        _OutlineIndex ("윤곽선 칸", Float) = 24
        _Color ("곱할 색", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _PaletteTex;
            float4 _PaletteTex_TexelSize;
            float _PaletteRows, _RowSkin, _RowCloth, _RowLeather, _RowMetal, _RowDark, _OutlineIndex;
            float4 _Ranges;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                if (t.a < 0.5) discard;
                float idx = floor(t.r * 255.0 + 0.5);
                float row = _RowSkin;
                if (idx >= _OutlineIndex) row = 0;
                else if (idx >= _Ranges.w) row = _RowDark;
                else if (idx >= _Ranges.z) row = _RowMetal;
                else if (idx >= _Ranges.y) row = _RowLeather;
                else if (idx >= _Ranges.x) row = _RowCloth;
                // PNG 맨 위 줄이 0번
                float2 puv = float2((idx + 0.5) * _PaletteTex_TexelSize.x, 1.0 - (row + 0.5) / _PaletteRows);
                fixed4 c = tex2D(_PaletteTex, puv);
                return fixed4(c.rgb, 1) * i.color;
            }
            ENDCG
        }
    }
}
