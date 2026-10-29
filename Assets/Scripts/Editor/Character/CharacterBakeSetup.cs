using System;
using System.Collections.Generic;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// 캐릭터 한 명을 도트로 굽기 위한 설정 묶음.
    /// 프로젝트 창에서 우클릭 > Create > CardGame > 캐릭터 굽기 설정 으로 만들거나,
    /// 굽기 창의 "테스트 설정 만들기" 버튼으로 자동으로 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "CardGame/캐릭터 굽기 설정", fileName = "bake_setup")]
    public class CharacterBakeSetup : ScriptableObject
    {
        [Header("몸")]
        [Tooltip("휴머노이드로 가져온 기본 몸 FBX (예: Superhero_Male_FullBody)")]
        public GameObject baseModel;

        [Tooltip("몸에 입힐 옷, 머리카락 FBX. 뼈 이름으로 자동으로 몸에 붙는다.")]
        public List<GameObject> parts = new List<GameObject>();

        [Tooltip("기본 몸을 살짝 뒤로 밀어서 옷 밖으로 비치지 않게 한다. 0이면 끔.")]
        public float bodyDepthPush = 0.03f;

        [Tooltip("옷을 입히면 기본 몸에서 머리(와 목)만 남기고 나머지는 지운다. 옷 밖으로 몸이 비치지 않게.")]
        public bool hideBodyUnderClothes = true;

        [Tooltip("몸을 지울 때 남길 뼈 이름. 이 뼈들에 절반 이상 붙은 부분만 남는다.")]
        public List<string> keepBodyBones = new List<string> { "Head", "neck_01" };

        [Tooltip("깊이 밀기에서 뺄 렌더러 이름 (눈, 눈썹처럼 옷에 가려지지 않는 것)")]
        public List<string> noPushRenderers = new List<string> { "Eyes", "Eyebrows" };

        [Tooltip("키 배율. 1이 기본, 0.9면 작은 캐릭터, 1.15면 큰 캐릭터.")]
        public float heightScale = 1f;

        [Tooltip("발이 바닥 근처에 있을 때 발바닥을 바닥과 평평하게 맞춘다 (발끝이 아래로 꺾여 하이힐처럼 보이는 것 방지)")]
        public bool flattenFeet = true;

        [Tooltip("발 평평하게 맞추는 세기 (0 끔, 1 완전히)")]
        [Range(0f, 1f)] public float flattenFeetAmount = 1f;

        [Tooltip("골반의 비틀림(좌우로 돌아감)과 옆 기울기를 펴서 몸이 화면 오른쪽을 곧게 보게 한다. 앞뒤 숙임은 그대로. 모션 도중 몸이 뒤쪽으로 돌아가는 것도 막는다.")]
        public bool straightenHips = true;

        [Tooltip("골반 펴는 세기 (0 끔, 1 완전히)")]
        [Range(0f, 1f)] public float straightenHipsAmount = 1f;

        [Tooltip("몸(옷, 머리 포함)이 바닥 아래로 내려가면 그만큼 올린다. 무기, 방패는 빼고 잰다.")]
        public bool keepAboveGround = true;

        [Tooltip("바닥 가까이 살짝 떠 있는 발을 다리를 굽혀(IK) 바닥에 붙인다. 이 거리(미터)보다 많이 뜬 발은 걷거나 뛰는 중이라 보고 그대로 둔다.")]
        public bool plantFeet = true;
        [Tooltip("디딘 발로 보는 높이 차이 (낮은 발 기준, 미터)")]
        public float plantDistance = 0.08f;
        [Tooltip("낮은 발이 이 높이(미터) 안이면 매 프레임 바닥선에 맞춘다. 넘으면 공중(점프)으로 보고 그대로 둔다.")]
        public float groundLockHeight = 0.2f;

        [Header("텍스처 연결 (재질 이름에 글자가 들어 있으면 이 텍스처를 쓴다)")]
        public List<TextureRule> textureRules = new List<TextureRule>();

        [Header("무기, 방패")]
        public WeaponSlot weapon = new WeaponSlot();
        public ShieldSlot shield = new ShieldSlot();

        [Header("모션")]
        public List<ClipEntry> clips = new List<ClipEntry>();

        [Header("카메라 (전투 횡스크롤 기준)")]
        [Tooltip("옆모습에서 정면 쪽으로 돌린 각도. 0이면 완전 옆모습.")]
        public float yawToFront = 0f;
        [Tooltip("위에서 내려다보는 각도.")]
        public float pitch = 0f;
        [Tooltip("1미터가 몇 픽셀인지. 키 1.8m 캐릭터가 36이면 약 65픽셀.")]
        public float pixelsPerMeter = 36f;
        [Tooltip("프레임 가로. 발은 왼쪽에서 3/8 지점에 오고, 앞쪽(오른쪽)에 무기 휘두를 자리를 넉넉히 둔다.")]
        public int frameWidth = 128;
        public int frameHeight = 96;
        [Tooltip("발바닥이 프레임 아래에서 몇 픽셀 위에 오는지.")]
        public int groundPixel = 6;
        [Tooltip("1초에 몇 장 굽는지.")]
        public int fps = 12;
        [Tooltip("모션 하나당 최대 장수. 너무 긴 모션이 잘리지 않게 넉넉히.")]
        public int maxFrames = 60;
        [Tooltip("점프 공격처럼 크게 움직이는 모션은 잘리지 않게 그 모션만 프레임을 자동으로 넓힌다. (발 위치는 그대로)")]
        public bool autoExpandFrame = true;
        [Tooltip("결과가 위아래로 뒤집혀 나오면 체크.")]
        public bool flipY = false;

        [Header("명암")]
        [Tooltip("빛이 오는 방향 (화면 기준: x 오른쪽, y 위, z 화면 밖으로)")]
        public Vector3 lightFromScreen = new Vector3(-0.5f, 0.8f, 0.6f);
        [Range(-1, 1)] public float litThreshold = 0.35f;
        [Range(-1, 1)] public float midThreshold = -0.15f;
        [Range(0, 1)] public float midBrightness = 0.82f;
        [Range(0, 1)] public float shadowBrightness = 0.62f;

        [Header("도트 마무리")]
        [Tooltip("팔레트 PNG. 비워 두면 색 제한 없이 굽는다.")]
        public Texture2D palette;
        public bool outline = true;
        public Color outlineColor = new Color32(0x1a, 0x12, 0x1e, 0xff);
        [Tooltip("팔레트 PNG가 비어 있으면, 전부 굽기 때 이 캐릭터의 모든 모션에서 색을 모아 고정 팔레트를 만든다 (출력 폴더의 _palette.png). 디더링 없음.")]
        public bool autoPalette = true;
        [Range(4, 64)] public int autoPaletteColors = 24;

        [Header("반짝임 줄이기")]
        [Tooltip("몇 배 크게 그린 뒤 평균으로 줄일지. 신발·장갑 장식 같은 작은 부분이 프레임마다 깜빡이는 것을 줄인다.")]
        [Range(1, 8)] public int superSample = 4;
        [Tooltip("텍스처를 결과 해상도보다 몇 단계 더 흐리게 쓸지 (밉맵 바이어스). 잔무늬가 바탕색으로 합쳐진다.")]
        [Range(0, 4)] public float textureMipBias = 2f;
        [Tooltip("결과 화면에서 이 픽셀보다 작은 부속(버클, 징 등 따로 떨어진 조각)은 그리지 않는다. 0이면 끔.")]
        public float hideTinyPartsPixels = 1.5f;
        [Tooltip("캐릭터 위치를 매 프레임 결과 픽셀 칸에 딱 맞춘다 (반 픽셀 흔들림 방지).")]
        public bool snapToPixel = true;
        [Tooltip("앞뒤 프레임이 같은데 이 프레임만 다른 픽셀(한 프레임짜리 깜빡임)을 앞뒤 색으로 바꾼다. 타격 프레임 근처와 빠르게 휘두르는 프레임은 건드리지 않는다.")]
        public bool flickerFilter = true;
        [Tooltip("무기 끝이 한 프레임에 이 픽셀보다 많이 움직이면 깜빡임 필터를 건너뛴다.")]
        public float flickerMaxSpeed = 4f;

        [Header("모션 재생 방식")]
        [Tooltip("모션 뼈대가 캐릭터와 똑같을 때(예: Quaternius UAL), 휴머노이드 변환 없이 뼈 회전을 그대로 넣는다. Generic으로 가져온 모션에만 적용된다.")]
        public bool directPlayback = false;
        [Tooltip("모션마다 첫 프레임에서 가장 낮은 발이 바닥에 닿도록 모션 전체를 한 번만 올리거나 내린다 (프레임마다 따로 맞추지 않음).")]
        public bool groundFromFirstFrame = false;

        [Header("무기 레이어 (몸과 따로 굽기)")]
        [Tooltip("여기 넣은 무기·방패는 몸 시트에 그리지 않고 layers/이름/ 폴더에 따로 굽는다. 프레임·기준점·크기는 몸 시트와 같고, 몸에 가려지는 부분은 지운다.")]
        public List<LayerItem> layers = new List<LayerItem>();
        [Tooltip("무기 레이어를 구울 모션 이름 앞부분 (예: Sword_). 비우면 모든 모션.")]
        public List<string> layerClipPrefixes = new List<string>();
        [Tooltip("무기 레이어마다 쓸 고정 팔레트 색 수")]
        [Range(4, 32)] public int layerPaletteColors = 12;

        [Header("부위별 팔레트 (게임에서 색 교체)")]
        [Tooltip("켜면 몸 시트 색을 부위별 칸(피부, 천, 가죽, 금속, 어두운 곳)으로 나눠 정하고, 칸 번호를 담은 인덱스 이미지(_idx.png)와 색 교체 팔레트(_palette_swap.png)를 같이 만든다.")]
        public bool paletteSwap = false;
        public int skinColors = 5;
        public int clothColors = 6;
        public int leatherColors = 6;
        public int metalColors = 4;
        public int darkColors = 3;

        [Header("저장")]
        [Tooltip("결과 PNG를 저장할 폴더")]
        public string outputFolder = "Assets/Art/characters/test";
        [Tooltip("게임에서 1유닛이 몇 픽셀인지 (맵 타일과 같은 64)")]
        public int spritePixelsPerUnit = 64;

        [Serializable]
        public class TextureRule
        {
            public string materialNameContains;
            public Texture2D texture;
            public Color tint = Color.white;
        }

        [Serializable]
        public class WeaponSlot
        {
            public GameObject prefab;
            public string boneName = "hand_r";
            [Tooltip("손가락 위치로 손잡이를 자동으로 쥐게 한다. 이상하면 끄고 아래 값으로 직접 맞춘다.")]
            public bool autoGrip = true;
            [Tooltip("손잡이 방향을 반대로 (칼날이 아래로 향하면 체크)")]
            public bool flipGrip = false;
            public Vector3 extraPosition = Vector3.zero;
            public Vector3 extraRotation = Vector3.zero;
            public float scale = 1f;
            [Tooltip("손잡이 찾는 방법. 0 자동(이름), 1 칼 종류(가장 넓은 곳 = 코등이 쪽이 손잡이), 2 도끼·망치 종류(머리에서 먼 끝이 손잡이)")]
            [Range(0, 2)] public int bladeMode = 0;
        }

        [Serializable]
        public class LayerItem
        {
            [Tooltip("레이어 이름 (폴더 이름으로 쓴다)")]
            public string name;
            public bool isShield;
            public GameObject prefab;
            public string boneName = "hand_r";
            [Tooltip("손잡이 찾는 방법. 0 자동(이름), 1 칼 종류, 2 도끼·망치 종류")]
            [Range(0, 2)] public int bladeMode = 0;
            public bool flipGrip;
            public Vector3 extraPosition;
            public Vector3 extraRotation;
            public float scale = 1f;
            [Tooltip("타격 프레임을 찾을 때 이 무기 끝을 쓴다. 아무것도 체크 안 하면 첫 번째 무기.")]
            public bool strikePoint;
        }

        [Serializable]
        public class ShieldSlot
        {
            [Tooltip("방패 모델이 없을 때 임시 원판 방패를 단다.")]
            public bool useTempShield = false;
            public GameObject prefab;
            public string boneName = "lowerarm_l";
            public float diameter = 0.6f;
            public float thickness = 0.05f;
            public Color color = new Color32(0x7a, 0x4e, 0x2c, 0xff);
            public Vector3 extraPosition = Vector3.zero;
            public Vector3 extraRotation = Vector3.zero;
            [Tooltip("방패 모델 크기 배율")]
            public float scale = 1f;
            [Tooltip("방패 모델의 가장 얇은 축을 바깥면, 가장 긴 축을 팔 방향으로 자동으로 맞춘다.")]
            public bool autoOrient = true;
        }

        [Serializable]
        public class ClipEntry
        {
            public bool bake = true;
            public AnimationClip clip;
            [Tooltip("저장 파일 이름. 비우면 모션 이름으로 만든다.")]
            public string outputName;
            [Tooltip("타격 프레임을 자동으로 찾는다. (무기 끝이 가장 빨리 움직이는 순간)")]
            public bool findHitFrame = false;
            [Tooltip("타격 프레임을 직접 정한다. -1이면 자동.")]
            public int hitFrameOverride = -1;
        }
    }
}
