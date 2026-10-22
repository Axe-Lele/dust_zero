// Boots the game and switches between the lobby and a battle.
//
// Everything lives in one scene: screens are enabled and disabled rather than loaded, which
// keeps the appliance sprites and pools warm between runs and makes returning to the lobby
// instant.
using TMPro;
using UnityEngine;

namespace DustZero.Game
{
    public sealed class AppRoot : MonoBehaviour
    {
        [SerializeField] LobbyScreen lobby;
        [SerializeField] BattleScreen battle;
        [SerializeField] SurvivorScreen survivor;
        [SerializeField] LeagueScreen league;
        [SerializeField] GuideOverlay guideOverlay;
        [SerializeField] AudioDirector audioDirector;

        [Header("부팅 / 오류")]
        [SerializeField] GameObject bootRoot;
        [SerializeField] TextMeshProUGUI bootLabel;

        GameServices _services;

        void Start()
        {
            // Portrait phone target; the editor still accepts mouse and keyboard.
            Screen.orientation = ScreenOrientation.Portrait;
            Application.targetFrameRate = 60;
            // Without this the editor raises OnApplicationPause every time the Game view
            // loses focus, which would suspend the music mid-session. Backgrounding on a
            // phone still reports through OnApplicationPause, and a desktop alt-tab is
            // handled by OnApplicationFocus, so nothing real is given up here.
            Application.runInBackground = true;

            if (bootRoot != null) bootRoot.SetActive(true);
            if (lobby != null) lobby.gameObject.SetActive(false);
            if (battle != null) battle.gameObject.SetActive(false);
            if (survivor != null) survivor.gameObject.SetActive(false);

            _services = GameServices.Boot();

            if (!_services.Ready)
            {
                // Table problems are shown verbatim: they name the table and row to fix.
                var msg = "게임 데이터를 불러오지 못했습니다.\n\n" +
                          string.Join("\n", _services.FatalProblems ?? new[] { "알 수 없는 오류" }) +
                          "\n\nGameData 폴더에서 다음을 실행한 뒤 다시 시도하세요:\n  python tools/sync_to_unity.py";
                if (bootLabel != null) bootLabel.text = msg;
                Debug.LogError(msg);
                return;
            }

            if (bootRoot != null) bootRoot.SetActive(false);

            lobby.Bind(_services);
            lobby.StageChosen += OnStageChosen;
            lobby.SurvivorChosen += OnSurvivorChosen;
            lobby.CoopChosen += OnCoopChosen;
            battle.Exited += OnBattleExited;
            battle.DeckRequested += () => ShowLobby(LobbyTab.Deck);
            if (survivor != null) survivor.Exited += OnSurvivorExited;
            if (league != null) league.Exited += OnLeagueExited;
            if (lobby != null) lobby.LeagueChosen += OnLeagueChosen;
            if (guideOverlay != null) guideOverlay.SkipRequested += () => _services.Guide?.Skip();
            if (audioDirector != null)
            {
                audioDirector.Bind(_services.Profile.Data);
                lobby.AudioSettingsChanged += audioDirector.ApplyTargets;
            }

            ShowLobby();

            if (_services.SaveStatus == Core.Meta.LoadStatus.RecoveredFromBackup)
                lobby.Toast("저장 파일이 손상되어 백업에서 복구했습니다.");
            else if (_services.SaveStatus == Core.Meta.LoadStatus.Corrupted)
                lobby.Toast("저장 파일을 읽지 못해 새로 시작합니다. 손상 파일은 보관해 두었습니다.");
        }

        void ShowLobby(LobbyTab? tab = null)
        {
            battle.gameObject.SetActive(false);
            if (survivor != null) survivor.gameObject.SetActive(false);
            if (league != null) league.gameObject.SetActive(false);
            lobby.gameObject.SetActive(true);
            lobby.Refresh();
            if (tab.HasValue) lobby.ShowTab(tab.Value);
            audioDirector?.Play(BgmTrack.Lobby);
        }

        void OnStageChosen(Data.StageDef stage)
        {
            lobby.gameObject.SetActive(false);
            battle.gameObject.SetActive(true);
            FocusCamera(battle.BoardCamera);
            battle.Begin(_services, stage);
            audioDirector?.Play(BgmTrack.Battle);
        }

        /// <summary>
        /// 지금 보이는 화면의 카메라를 위로 올린다.
        ///
        /// 전투판과 대청소판은 **각자 카메라를 들고 둘 다 켜져 있다.** 둘 다 깊이 0 에
        /// 전체 레이어를 찍고 배경까지 칠하므로 누가 나중에 그려질지 정해지지 않는다.
        /// 실제로 전투 카메라(#F6F1E9)가 대청소를 덮어 **빈 화면**이 됐다.
        ///
        /// 카메라를 끄지는 않는다 — 전에 그렇게 했다가 Camera.main 이 null 이 되어
        /// 캡처가 통째로 비었다. 깊이만 바꾸면 위에 있는 쪽이 이긴다.
        /// </summary>
        void FocusCamera(Camera front)
        {
            var board = battle != null ? battle.BoardCamera : null;
            var arena = survivor != null ? survivor.ArenaCamera : null;
            if (board != null) board.depth = board == front ? 10f : 0f;
            if (arena != null) arena.depth = arena == front ? 10f : 0f;
        }

        void OnBattleExited() => ShowLobby();

        void OnSurvivorChosen()
        {
            if (survivor == null) { lobby.Toast("대청소 화면이 아직 없어요."); return; }
            lobby.gameObject.SetActive(false);
            survivor.gameObject.SetActive(true);
            FocusCamera(survivor.ArenaCamera);
            survivor.Begin(_services);
            audioDirector?.Play(BgmTrack.Survival);
        }

        void OnSurvivorExited() => ShowLobby();

        /// <summary>
        /// 가전 대청소 리그. 원본은 티어를 고르는 시트를 먼저 띄우지만, 지금은 열려 있는
        /// 가장 높은 티어로 바로 들어간다 — 티어 선택 시트는 아직 이식하지 않았다.
        /// </summary>
        void OnLeagueChosen()
        {
            if (league == null) { lobby.Toast("리그 화면이 아직 없어요."); return; }
            if (!_services.Profile.LeagueUnlocked)
            {
                lobby.Toast(_services.Profile.LeagueLockMessage);
                return;
            }
            lobby.gameObject.SetActive(false);
            league.gameObject.SetActive(true);
            FocusCamera(null);
            league.Begin(_services, _services.Profile.LeagueTierMax);
            audioDirector?.Play(BgmTrack.Battle);
        }

        void OnLeagueExited() => ShowLobby();

        /// <summary>
        /// \ud611\ub3d9 \uccad\uc18c. \uc6d0\ubcf8\uacfc \uac19\uc774 \uc804\ud22c \ud654\uba74\uc744 \uadf8\ub300\ub85c \uc4f0\uace0 \ub450 \uc904\ub9cc \ubc14\uafc0\uc5b4 \ub2e4\ub294\ub2e4.
        /// \ud30c\ud2b8\ub108\ub294 \ub9c8\uc9c0\ub9c9\uc5d0 \uace0\ub978 \uc0ac\ub78c\uc73c\ub85c \ubc14\ub85c \ucd9c\ub3d9\ud55c\ub2e4.
        /// </summary>
        void OnCoopChosen(string partnerId)
        {
            if (!_services.Profile.CoopUnlocked)
            {
                lobby.Toast(_services.Profile.CoopLockMessage);
                return;
            }
            if (_services.Profile.Data.deck.Count == 0)
            {
                lobby.Toast("\uba3c\uc800 \ud3b8\uc131\uc744 \uc9dc\uc8fc\uc138\uc694.");
                return;
            }

            var partner = _services.Db.CoopPartner(partnerId);
            if (partner == null) { lobby.Toast("\ud30c\ud2b8\ub108 \uc790\ub8cc\uac00 \uc5c6\uc5b4\uc694."); return; }
            _services.Profile.SetCoopPartner(partner.id);
            _services.Persist();

            lobby.gameObject.SetActive(false);
            battle.gameObject.SetActive(true);
            FocusCamera(battle.BoardCamera);
            battle.BeginCoop(_services, partner);
            audioDirector?.Play(BgmTrack.Battle);
        }

        /// <summary>
        /// Drives the lobby guide every frame regardless of which screen is up front, since its
        /// two flows span the lobby, the battle brief and the result screen. A null target just
        /// means the screen currently shown does not own that step's element yet.
        /// </summary>
        void Update()
        {
            if (_services == null || !_services.Ready || guideOverlay == null) return;
            var step = _services.Guide?.Current();
            if (step == null) { guideOverlay.Hide(); return; }
            guideOverlay.Show(ResolveGuideTarget(step.Row.target), step.Text);
        }

        RectTransform ResolveGuideTarget(string key)
        {
            if (lobby != null && lobby.gameObject.activeInHierarchy)
            {
                var r = lobby.ResolveGuideTarget(key);
                if (r != null) return r;
            }
            if (battle != null && battle.gameObject.activeInHierarchy)
            {
                var r = battle.ResolveGuideTarget(key);
                if (r != null) return r;
            }
            return null;
        }

        void OnApplicationQuit() => _services?.Persist();

        void OnApplicationPause(bool paused)
        {
            if (paused) _services?.Persist();
            audioDirector?.SetSuspended(paused);
        }

        // Desktop builds never raise OnApplicationPause for an alt-tab, so focus is what
        // silences the music away from a phone. Not in the editor, though: the Game view
        // gives up focus every time anyone clicks the Inspector, and cutting the music for
        // that is both wrong and maddening to work next to.
        void OnApplicationFocus(bool focused)
        {
            if (Application.isEditor) return;
            audioDirector?.SetSuspended(!focused);
        }
    }
}
