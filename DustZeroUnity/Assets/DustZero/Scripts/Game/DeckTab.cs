// 편성: six sortie slots over the appliances you own.
//
// Its own bottom tab in 10.16-n. Tapping a filled slot drops that appliance; tapping one in
// the pool adds it to the first free slot. Deck membership lives in the save, so both this
// tab and the stage tab's sortie strip read the same list.
using System;
using System.Collections.Generic;
using DustZero.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DustZero.Game
{
    public sealed class DeckTab : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI countLabel;
        [SerializeField] RectTransform slotRoot;
        [SerializeField] DeckSlot slotPrefab;
        [SerializeField] RectTransform poolRoot;
        [SerializeField] UnitCard cardPrefab;
        [SerializeField] SpriteLibrary sprites;

        GameServices _svc;
        readonly List<DeckSlot> _slots = new();
        readonly List<UnitCard> _pool = new();

        public event Action<string> Message;

        public void Bind(GameServices services)
        {
            _svc = services;
            BuildSlots();
            BuildPool();
            Refresh();
        }

        void OnEnable() { if (_svc != null) { BuildPool(); Refresh(); } }

        void BuildSlots()
        {
            if (slotRoot == null || slotPrefab == null || _svc == null) return;
            foreach (var s in _slots) if (s != null) Destroy(s.gameObject);
            _slots.Clear();

            for (int i = 0; i < _svc.Db.Config.deckMax; i++)
            {
                int index = i;
                var slot = Instantiate(slotPrefab, slotRoot);
                slot.name = "Slot" + i;
                // The row does not control child size, so each slot keeps the prefab's rect
                // and shrinks itself when empty - exactly what the build's flex row does.
                slot.Tapped += _ => DropAt(index);
                _slots.Add(slot);
            }
        }

        void BuildPool()
        {
            if (poolRoot == null || cardPrefab == null || _svc == null) return;
            foreach (var c in _pool) if (c != null) Destroy(c.gameObject);
            _pool.Clear();

            var owned = new List<UnitDef>();
            foreach (var kv in _svc.Db.Units)
                if (_svc.Profile.Data.Own(kv.Key) != null) owned.Add(kv.Value);
            owned.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (var def in owned)
            {
                var card = Instantiate(cardPrefab, poolRoot);
                card.Bind(def, sprites);
                card.Tapped += Toggle;
                _pool.Add(card);
            }
        }

        /// <summary>A tap on a filled slot takes that appliance out of the deck.</summary>
        void DropAt(int index)
        {
            var deck = _svc.Profile.Data.deck;
            if (index < 0 || index >= deck.Count) return;
            deck.RemoveAt(index);
            _svc.Persist();
            Refresh();
        }

        /// <summary>A tap in the pool adds or removes, whichever the appliance is not already.</summary>
        void Toggle(UnitDef unit)
        {
            if (unit == null) return;
            var deck = _svc.Profile.Data.deck;
            if (deck.Contains(unit.Id)) deck.Remove(unit.Id);
            else if (deck.Count >= _svc.Db.Config.deckMax)
            {
                Message?.Invoke($"편성은 {_svc.Db.Config.deckMax}칸까지예요.");
                return;
            }
            else deck.Add(unit.Id);

            _svc.Persist();
            Refresh();
        }

        public void Refresh()
        {
            if (_svc == null) return;
            var p = _svc.Profile;
            var deck = p.Data.deck;

            if (countLabel != null) countLabel.text = $"{deck.Count}/{_svc.Db.Config.deckMax}";

            for (int i = 0; i < _slots.Count; i++)
            {
                var def = i < deck.Count && _svc.Db.Units.TryGetValue(deck[i], out var d) ? d : null;
                _slots[i].Bind(def, sprites);
            }
            foreach (var c in _pool) c.Sync(p);
        }
    }
}
