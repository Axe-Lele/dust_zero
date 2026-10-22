// Sequences the lobby's forced onboarding: the `guide` table's two short flows, "first"
// (before any stage is cleared) and "levelup" (right after). Each row names one UI element to
// spotlight; whichever screen owns that element resolves it and reports back when the player
// reaches it. Independent of tutOff/tut, which gate the battle tutorial instead.
using DustZero.Data;

namespace DustZero.Core.Meta
{
    public sealed class GuideStep
    {
        public readonly GuideRow Row;
        public readonly string Text;
        public GuideStep(GuideRow row, string text) { Row = row; Text = text; }
    }

    public sealed class GuideDirector
    {
        readonly GameDatabase _db;
        readonly SaveData _save;

        /// <summary>Raised whenever a step advances or a flow is skipped, so the host can persist.</summary>
        public event System.Action Changed;

        public GuideDirector(GameDatabase db, SaveData save)
        {
            _db = db;
            _save = save;
        }

        /// <summary>
        /// "first" is for an account that has not cleared a stage yet; "levelup" picks up once
        /// one has. Gating "first" on best &lt; 1 (not just on its own step count) matters for a
        /// save that already had progress before this feature existed - without it, an account
        /// ten stages in would suddenly see "1-1을 눌러 지켜 주세요" again.
        /// </summary>
        string ActiveFlow()
        {
            if (_db.Guides.TryGetValue("first", out var first) &&
                _save.best < 1 && _save.GuideStep("first") < first.Count)
                return "first";
            if (_db.Guides.TryGetValue("levelup", out var lv) &&
                _save.best >= 1 && _save.GuideStep("levelup") < lv.Count)
                return "levelup";
            return null;
        }

        /// <summary>The step that should be showing right now, or null if nothing is due.</summary>
        public GuideStep Current()
        {
            var flow = ActiveFlow();
            if (flow == null) return null;
            var rows = _db.Guides[flow];
            int i = _save.GuideStep(flow);
            if (i >= rows.Count) return null;
            var row = rows[i];
            return new GuideStep(row, _db.Text(row.textKey));
        }

        /// <summary>
        /// Called by whichever screen just showed (or whose player action reached) the element
        /// the current step names. A no-op unless key is actually what the active step is
        /// waiting on, so callers can report every relevant transition unconditionally.
        /// </summary>
        public void ReportReached(string key)
        {
            var step = Current();
            if (step == null || key != step.Row.target) return;
            _save.AdvanceGuide(step.Row.flow);
            Changed?.Invoke();
        }

        /// <summary>The bubble's own "건너뛰기": skips the rest of the active flow only.</summary>
        public void Skip()
        {
            var flow = ActiveFlow();
            if (flow == null) return;
            if (_db.Guides.TryGetValue(flow, out var rows)) _save.guide[flow] = rows.Count;
            Changed?.Invoke();
        }
    }
}
