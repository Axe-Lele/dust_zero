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

        void Awake()
        {
            if (root == null) root = transform;
            if (shakeCamera != null) _cameraHome = shakeCamera.transform.localPosition;

            _particlePool = new ObjectPool<FxParticle>(
                createFunc: () => Instantiate(particlePrefab, root),
                actionOnGet: p => p.gameObject.SetActive(true),
                actionOnRelease: p => p.gameObject.SetActive(false),
                actionOnDestroy: p => { if (p != null) Destroy(p.gameObject); },
                collectionCheck: false, defaultCapacity: 128, maxSize: 1024);

            _labelPool = new ObjectPool<FxLabel>(
                createFunc: () => Instantiate(labelPrefab, root),
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
                l.transform.localPosition = ToWorld(t.X, t.Y, worldOffset, worldScale);
                l.Init(t, numberFont);
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
