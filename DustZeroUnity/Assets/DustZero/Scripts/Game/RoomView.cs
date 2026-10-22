// Draws the lobby room: owned appliances pottering about, cleaning up dust.
//
// The rules live in RoomSim; this only maps its unit-box coordinates onto the panel and
// keeps one pooled Image per agent and per speck. Sprites are the same ones the battle
// board uses, so there is no second art path to maintain.
using System.Collections.Generic;
using DustZero.Core;
using DustZero.Core.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class RoomView : MonoBehaviour
    {
        [SerializeField] RectTransform floor;
        [SerializeField] RectTransform agentRoot;
        [SerializeField] SpriteLibrary sprites;
        [SerializeField] Sprite dustSprite;
        [SerializeField] GameObject bubbleRoot;
        [SerializeField] TextMeshProUGUI bubbleLabel;

        /// <summary>How big an appliance is drawn, as a fraction of the floor's width.</summary>
        const float AgentWidth = 0.17f;

        RoomSim _sim;
        GameServices _svc;

        readonly Dictionary<RoomAgent, RectTransform> _agentViews = new();
        readonly List<(RoomDust dust, Image img)> _dustViews = new();
        readonly Stack<Image> _dustPool = new();

        /// <summary>How many chatter lines the string table carries per appliance.</summary>
        /// <summary>
        /// Fallback only. The real count is unit.lineCount, read through UnitDef.Lines - a
        /// unit with fewer lines used to draw the raw string key for the missing ones.
        /// </summary>
        public const int LinesPerUnit = 4;

        /// <summary>
        /// The string-table key for one of an appliance's room lines. Exposed so a test can
        /// check the composed key against the shipped table: Db.Text hands back the key it
        /// was given on a miss, so a wrong pattern here is invisible at runtime and simply
        /// prints itself into the speech bubble.
        /// </summary>
        public static string ChatterKey(string type, int n) => $"unit.{type}.line{n}";

        public RoomSim Sim => _sim;

        public void Bind(GameServices services)
        {
            _svc = services;
            if (_sim == null)
            {
                _sim = new RoomSim(services.Rng);
                // Chatter comes from the string table, so the room speaks the game's own
                // lines. The table keys them unit.<id>.line1..line4 - there is no room.*
                // key in any build, and Db.Text returns the key it was given when it misses,
                // so asking for one put the literal "room.vac.1" in the speech bubble.
                _sim.LineFor = type =>
                {
                    // How many lines an appliance has is unit.lineCount, not a constant: a
                    // unit with two lines used to roll a third and print the raw key.
                    int count = services.Db.Units.TryGetValue(type, out var d) && d.Lines != null
                        ? d.Lines.Length : LinesPerUnit;
                    if (count <= 0) return null;
                    int n = Mathf.Clamp(1 + (int)(services.Rng.Value * count), 1, count);
                    var key = ChatterKey(type, n);
                    return services.Db.HasText(key) ? services.Db.Text(key) : null;
                };
            }
            RefreshCast();
        }

        /// <summary>Re-reads which appliances the player owns.</summary>
        public void RefreshCast()
        {
            if (_sim == null || _svc == null) return;
            var owned = new List<string>();
            foreach (var kv in _svc.Db.Units)
                if (_svc.Profile.Data.Own(kv.Key) != null) owned.Add(kv.Key);

            _sim.SetCast(owned, SpeedOf);
            Prune();
        }

        /// <summary>
        /// Walk speed comes from unit_art.roomSpeed - the original gives the robot a scurry
        /// and the heavy appliances a trudge, and a new appliance sets its own pace in the
        /// table rather than here.
        /// </summary>
        float SpeedOf(string type)
            => _svc != null && _svc.Db.Units.TryGetValue(type, out var d) ? d.RoomSpeed : 0.18f;

        void Update()
        {
            if (_sim == null || floor == null) return;
            _sim.Tick(Time.unscaledDeltaTime);
            Draw();
        }

        void Draw()
        {
            var rect = floor.rect;
            float w = rect.width, h = rect.height;
            if (w <= 0f || h <= 0f) return;

            foreach (var a in _sim.Agents)
            {
                if (!_agentViews.TryGetValue(a, out var view)) view = CreateAgent(a);
                float size = w * AgentWidth;
                view.sizeDelta = new Vector2(size, size);
                // Unit box to panel space: y grows downward in the simulation.
                view.anchoredPosition = new Vector2(rect.xMin + a.X * w, rect.yMax - a.Y * h);
                // Lower agents draw in front, as on the battle board.
                view.SetSiblingIndex(Mathf.RoundToInt(a.Y * 1000f).ClampIndex(agentRoot.childCount));

                var body = view.GetChild(0) as RectTransform;
                if (body != null)
                {
                    float lean = a.Kick > 0f ? 8f : 0f;
                    body.localScale = new Vector3(a.Face >= 0f ? 1f : -1f, 1f, 1f);
                    body.localRotation = Quaternion.Euler(0f, 0f, -lean * a.Face);
                    body.anchoredPosition = new Vector2(0f, a.Hop > 0f ? a.Hop * 40f : 0f);
                }
            }

            SyncDust(rect, w, h);
            SyncBubble(rect, w, h);
        }

        RectTransform CreateAgent(RoomAgent a)
        {
            var go = new GameObject("Agent_" + a.Type, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(agentRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            var body = (RectTransform)bodyGo.transform;
            body.SetParent(rt, false);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = body.offsetMax = Vector2.zero;
            var img = bodyGo.AddComponent<Image>();
            img.sprite = sprites != null ? sprites.Unit(a.Type) : null;
            img.preserveAspect = true;
            img.raycastTarget = false;

            // 10.16-n puts no name plate over an appliance - it identifies them by their
            // own art, and only a speech bubble ever appears above one. Twelve stacked plates
            // is what made the room read as cluttered next to the original.
            _agentViews[a] = rt;
            return rt;
        }

        void SyncDust(Rect rect, float w, float h)
        {
            // Retire views whose speck is gone.
            for (int i = _dustViews.Count - 1; i >= 0; i--)
            {
                if (_sim.Holds(_dustViews[i].dust)) continue;
                var img = _dustViews[i].img;
                img.gameObject.SetActive(false);
                _dustPool.Push(img);
                _dustViews.RemoveAt(i);
            }

            foreach (var d in _sim.Dust)
            {
                Image img = null;
                foreach (var pair in _dustViews) if (pair.dust == d) { img = pair.img; break; }
                if (img == null)
                {
                    img = _dustPool.Count > 0 ? _dustPool.Pop() : NewDust();
                    img.gameObject.SetActive(true);
                    _dustViews.Add((d, img));
                }
                var rt = img.rectTransform;
                float size = w * d.Radius * 2f;
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(rect.xMin + d.X * w, rect.yMax - d.Y * h);
                var c = img.color;
                img.color = new Color(c.r, c.g, c.b, 0.25f + 0.55f * d.Amount);
            }
        }

        Image NewDust()
        {
            var go = new GameObject("Dust", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(agentRoot, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.sprite = dustSprite;
            img.color = new Color(0.42f, 0.40f, 0.38f, 0.7f);
            img.raycastTarget = false;
            return img;
        }

        void SyncBubble(Rect rect, float w, float h)
        {
            if (bubbleRoot == null) return;
            var speech = _sim.Speech;
            bubbleRoot.SetActive(speech.HasValue);
            if (!speech.HasValue) return;
            if (bubbleLabel != null) bubbleLabel.text = speech.Value.Text;
            var a = speech.Value.Agent;
            var rt = (RectTransform)bubbleRoot.transform;
            rt.anchoredPosition = new Vector2(rect.xMin + a.X * w,
                                              rect.yMax - a.Y * h + w * AgentWidth * 0.75f);
        }

        /// <summary>Drops views for agents the player no longer owns.</summary>
        void Prune()
        {
            var stale = new List<RoomAgent>();
            foreach (var kv in _agentViews) if (!_sim.Holds(kv.Key)) stale.Add(kv.Key);
            foreach (var a in stale)
            {
                if (_agentViews[a] != null) Destroy(_agentViews[a].gameObject);
                _agentViews.Remove(a);
            }
        }
    }

    static class SiblingIndexExtensions
    {
        /// <summary>Keeps a computed draw order inside the parent's actual child range.</summary>
        public static int ClampIndex(this int value, int childCount)
            => childCount <= 0 ? 0 : Mathf.Clamp(value, 0, childCount - 1);
    }
}
