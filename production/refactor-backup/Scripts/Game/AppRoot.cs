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

        [Header("부팅 / 오류")]
        [SerializeField] GameObject bootRoot;
        [SerializeField] TextMeshProUGUI bootLabel;

        GameServices _services;

        void Start()
        {
            // Portrait phone target; the editor still accepts mouse and keyboard.
            Screen.orientation = ScreenOrientation.Portrait;
            Application.targetFrameRate = 60;

            if (bootRoot != null) bootRoot.SetActive(true);
            if (lobby != null) lobby.gameObject.SetActive(false);
            if (battle != null) battle.gameObject.SetActive(false);

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
            battle.Exited += OnBattleExited;

            ShowLobby();

            if (_services.SaveStatus == Core.Meta.LoadStatus.RecoveredFromBackup)
                lobby.Toast("저장 파일이 손상되어 백업에서 복구했습니다.");
            else if (_services.SaveStatus == Core.Meta.LoadStatus.Corrupted)
                lobby.Toast("저장 파일을 읽지 못해 새로 시작합니다. 손상 파일은 보관해 두었습니다.");
        }

        void ShowLobby()
        {
            battle.gameObject.SetActive(false);
            lobby.gameObject.SetActive(true);
            lobby.Refresh();
        }

        void OnStageChosen(Data.StageDef stage)
        {
            lobby.gameObject.SetActive(false);
            battle.gameObject.SetActive(true);
            battle.Begin(_services, stage);
        }

        void OnBattleExited() => ShowLobby();

        void OnApplicationQuit() => _services?.Persist();

        void OnApplicationPause(bool paused)
        {
            if (paused) _services?.Persist();
        }
    }
}
