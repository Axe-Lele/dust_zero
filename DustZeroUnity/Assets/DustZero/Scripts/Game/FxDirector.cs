// Turns PresentationBus events into pooled particles, damage labels and camera shake.
//
// Everything here runs on presentation time, never simulation time. That separation is what
// keeps pause and 2x speed honest: at 2x the simulation ticks twice per frame while these
// visuals still advance once, and while paused the simulation stops but nothing here breaks.
using System.Collections.Generic;
using DustZero.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;

namespace DustZero.Game
{
    public sealed class FxDirector : MonoBehaviour
    {
        [SerializeField] Transform root;
        [SerializeField] FxParticle particlePrefab;
        [SerializeField] FxLabel labelPrefab;
        [SerializeField] Sprite roundSprite;
        [SerializeField] Sprite squareSprite;
        [SerializeField] TMP_FontAsset numberFont;
        /// <summary>라벨용 본문 글꼴. 원본은 라벨만 'IBM Plex Sans KR' 로 쓴다.</summary>
        [SerializeField] TMP_FontAsset labelFont;

        /// <summary>숫자에 두를 외곽선 머티리얼. 한 장을 모두가 공유한다.</summary>
        [SerializeField] Material numberOutline;

        /// <summary>
        /// 라벨이 넘어가지 못하는 가로 범위(월드). 원본 `clampX` 가 하는 일이다 —
        /// 판 가장자리에서 합친 가전의 이름이 판 밖으로 흘러나가지 않는다.
        /// 비워 두면(Max ≤ Min) 가두지 않는다.
        /// </summary>
        public float LabelClampMinX { get; set; }
        public float LabelClampMaxX { get; set; }
        [SerializeField] Camera shakeCamera;

        [Tooltip("동시에 살아 있을 수 있는 입자 수. 넘치면 새 입자를 만들지 않습니다.")]
        [SerializeField] int particleBudget = 600;

        ObjectPool<FxParticle> _particlePool;
        ObjectPool<FxLabel> _labelPool;
        readonly List<FxParticle> _liveParticles = new();
        readonly List<FxLabel> _liveLabels = new();

        float _shake;
        Vector3 _cameraHome;

        /// <summary>Set false to honour the player's reduced-motion preference.</summary>
        public bool AllowShake { get; set; } = true;

        /// <summary>
        /// 흔들기가 돌아갈 자리. 전투에서는 판이 고정이라 <c>Awake</c> 때 잡은 값 그대로지만,
        /// 대청소는 카메라가 리더를 따라다닌다 — 따라가는 쪽이 여기를 옮기고,
        /// 카메라 좌표를 직접 쓰지 않는다. 쓰는 쪽이 둘이면 뒤에 도는 쪽이 이겨서
        /// 화면이 원점에 머문다(실제로 대청소가 빈 화면이었다).
        /// </summary>
        public Vector3 CameraHome
        {
            get => _cameraHome;
            set => _cameraHome = value;
        }

        /// <summary>
        /// 연출도 자기 뿌리와 같은 레이어에 둔다. 대청소 아레나는 전용 레이어로 그려서,
        /// 프리팹 그대로 태어나면 전투 카메라에도 겹쳐 보인다.
        /// </summary>
        static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerDeep(c.gameObject, layer);
        }

        void Awake()
        {
            if (root == null) root = transform;
            if (shakeCamera != null) _cameraHome = shakeCamera.transform.localPosition;

            _particlePool = new ObjectPool<FxParticle>(
                createFunc: () =>
                {
                    var p = Instantiate(particlePrefab, root);
                    SetLayerDeep(p.gameObject, root.gameObject.layer);
                    return p;
                },
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: p => { if (p != null) Destroy(p.gameObject); },
                collectionCheck: false, defaultCapacity: 128, maxSize: 1024);

            _labelPool = new ObjectPool<FxLabel>(
                createFunc: () =>
                {
                    var l = Instantiate(labelPrefab, root);
                    SetLayerDeep(l.gameObject, root.gameObject.layer);
                    return l;
                },
                actionOnGet: l => l.gameObject.SetActive(true),
                actionOnRelease: l => l.gameObject.SetActive(false),
                actionOnDestroy: l => { if (l != null) Destroy(l.gameObject); },
                collectionCheck: false, defaultCapacity: 48, maxSize: 256);
        }

        /// <summary>Drains one frame of simulation output into live visuals.</summary>
        public void Consume(PresentationBus bus, Vector2 worldOffset, float worldScale)
        {
            foreach (var b in bus.Bursts)
            {
                if (_liveParticles.Count >= particleBudget) break;
                int count = Mathf.Min(b.Count, particleBudget - _liveParticles.Count);
                for (int i = 0; i < count; i++)
                {
                    float angle = Random.value * Mathf.PI * 2f;
                    float speed = b.Speed * (0.35f + Random.value * 0.9f);
                    var p = _particlePool.Get();
                    p.transform.localPosition = ToWorld(b.X, b.Y, worldOffset, worldScale);
                    p.Init(b, angle, speed, roundSprite, squareSprite);
                    _liveParticles.Add(p);
                }
            }

            foreach (var t in bus.Texts)
            {
                var l = _labelPool.Get();
                l.Init(t, numberFont, labelFont, numberOutline);

                var at = ToWorld(t.X, t.Y, worldOffset, worldScale);
                if (LabelClampMaxX > LabelClampMinX)
                {
                    // 글자 절반 폭만큼 안쪽으로 밀어 넣는다.
                    l.Text.ForceMeshUpdate();
                    float half = l.Text.GetRenderedValues(false).x * 0.5f + 0.08f;
                    at.x = Mathf.Clamp(at.x, LabelClampMinX + half, LabelClampMaxX - half);
                }
                l.transform.localPosition = at;
                _liveLabels.Add(l);
            }

            if (AllowShake && bus.Shake > _shake) _shake = bus.Shake;
        }

        static Vector3 ToWorld(float x, float y, Vector2 offset, float scale)
            => new(offset.x + x * scale, offset.y - y * scale, 0f);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = _liveParticles.Count - 1; i >= 0; i--)
                if (!_liveParticles[i].Step(dt))
                {
                    _particlePool.Release(_liveParticles[i]);
                    _liveParticles.RemoveAt(i);
                }

            for (int i = _liveLabels.Count - 1; i >= 0; i--)
                if (!_liveLabels[i].Step(dt))
                {
                    _labelPool.Release(_liveLabels[i]);
                    _liveLabels.RemoveAt(i);
                }

            if (shakeCamera == null) return;
            if (_shake > 0f)
            {
                _shake = Mathf.Max(0f, _shake - dt * 1.6f);
                float m = _shake * 0.18f;
                shakeCamera.transform.localPosition = _cameraHome +
                    new Vector3((Random.value - 0.5f) * m, (Random.value - 0.5f) * m, 0f);
            }
            else shakeCamera.transform.localPosition = _cameraHome;
        }

        /// <summary>Recycles everything, e.g. when leaving a battle.</summary>
        public void ClearAll()
        {
            foreach (var p in _liveParticles) _particlePool.Release(p);
            foreach (var l in _liveLabels) _labelPool.Release(l);
            _liveParticles.Clear();
            _liveLabels.Clear();
            _shake = 0f;
        }
    }
}
