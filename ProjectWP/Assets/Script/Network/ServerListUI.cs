using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

/// <summary>
/// タイトル画面 → サーバー選択画面 の流れを担当するクラス。
///
/// 「Server 1/2/3」は常時起動している専用サーバーではなく、
/// 最初にそのサーバーを選んだ人がその場でホストになる方式(専用サーバーは用意しない)。
/// Unity Lobbyサービスを「今どのサーバーに何人いるか」の一覧板として使う:
///   - サーバーが空 → Lobbyがまだ存在しない → 選ぶと自分がホストになり、新しくLobbyを作る
///   - サーバーに誰かいる → Lobbyが既にある → 選ぶとそのLobbyに参加し、
///     Lobbyに保存されているRelayの参加コードを使って自動で接続する
///     (参加コードを手入力する必要はない)
///
/// Relayへの接続自体(参加コードを使ってホスト/参加する処理)はNetworkBootstrapに任せ、
/// このクラスは「どのサーバーを選ぶか」の画面表示とLobbyサービスとのやり取りだけを担当する。
/// </summary>
public class ServerListUI : MonoBehaviour
{
    [Header("画面切り替え")]
    [Tooltip("最初に表示するタイトル画面")]
    [SerializeField] private GameObject titlePanel;

    [Tooltip("サーバー選択画面全体の背景パネル(ServerListPanelとStatusTextを含む箱)。" +
        "タイトル画面表示中はこれごと隠す")]
    [SerializeField] private GameObject connectionPanel;

    [Tooltip("スタートボタンを押すと表示するサーバー選択画面")]
    [SerializeField] private GameObject serverListPanel;

    [SerializeField] private Button startButton;

    [Header("サーバーの行(上から Server 1, 2, 3 の順で並べる)")]
    [SerializeField] private Button[] serverRowButtons = new Button[3];
    [SerializeField] private TextMeshProUGUI[] serverRowLabels = new TextMeshProUGUI[3];

    // Lobby上でのサーバー名。この名前で「どのサーバーか」を判定する。
    private static readonly string[] ServerNames = { "Server 1", "Server 2", "Server 3" };

    // Lobbyのデータに、Relayの参加コードを保存するときのキー名
    private const string RelayJoinCodeKey = "relayJoinCode";

    // 直近のQueryLobbiesAsyncの結果。index([0]=Server1, [1]=Server2, [2]=Server3)に
    // 対応するLobbyが見つかっていればそれを、無ければnullを入れる。
    private readonly Lobby[] cachedLobbies = new Lobby[3];

    private void Awake()
    {
        // 「Start」ボタンはクリックでも押せるようにしておく(マウス操作用)。
        // それとは別に、下のShowTitlePanel()で「何かボタンを押したら進む」も仕込む。
        if (startButton != null) startButton.onClick.AddListener(OnStartButtonClicked);

        for (int i = 0; i < serverRowButtons.Length; i++)
        {
            int index = i; // ラムダに渡すインデックスがずれないよう、ループ変数をローカルにコピーしておく
            if (serverRowButtons[i] != null)
            {
                serverRowButtons[i].onClick.AddListener(() => OnServerRowClicked(index));
            }
        }

        ShowTitlePanel();
    }

    private void ShowTitlePanel()
    {
        if (titlePanel != null) titlePanel.SetActive(true);
        // サーバー選択画面の背景ごと隠す(空の箱が画面端に残らないようにする)
        if (connectionPanel != null) connectionPanel.SetActive(false);
        if (serverListPanel != null) serverListPanel.SetActive(false);

        // 「何かボタン(キーボード/マウス/ゲームパッド)を押したら次に進む」を1回だけ待つ。
        // ここで押されたデバイスを、この端末の操作デバイスとして記録しておく。
        InputSystem.onAnyButtonPress.CallOnce(OnAnyButtonPressedOnTitle);
    }

    private void OnAnyButtonPressedOnTitle(InputControl control)
    {
        if (LocalInputDeviceService.Instance != null)
        {
            LocalInputDeviceService.Instance.SetChosenDevice(control.device);
        }

        OnStartButtonClicked();
    }

    private async void OnStartButtonClicked()
    {
        if (titlePanel != null) titlePanel.SetActive(false);
        if (connectionPanel != null) connectionPanel.SetActive(true);
        if (serverListPanel != null) serverListPanel.SetActive(true);

        await RefreshServerListAsync();

        // ゲームパッド/キーボードで上下移動を始められるように、最初の行を選択状態にしておく。
        // 一度nullにしてから選び直すことで、既に同じものが選ばれていた場合でも
        // 選択イベント(縁の点灯など)が確実に発火するようにしている。
        if (EventSystem.current != null && serverRowButtons.Length > 0 && serverRowButtons[0] != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(serverRowButtons[0].gameObject);
        }
    }

    /// <summary>
    /// Lobbyサービスに問い合わせて、Server 1/2/3それぞれの現在の状態
    /// (誰もいない/何人いるか)を取得し、一覧の表示を更新する。
    /// </summary>
    private async Task RefreshServerListAsync()
    {
        await NetworkBootstrap.Instance.EnsureSignedInAsync();

        NetworkBootstrap.Instance.SetStatus("サーバーを探しています...");

        QueryResponse response;
        try
        {
            response = await LobbyService.Instance.QueryLobbiesAsync();
        }
        catch (Exception e)
        {
            NetworkBootstrap.Instance.SetStatus($"サーバー一覧の取得に失敗しました: {e.Message}");
            Debug.LogException(e);
            return;
        }

        // 一旦全部「空」に戻してから、見つかったものだけ埋めていく
        for (int i = 0; i < cachedLobbies.Length; i++)
        {
            cachedLobbies[i] = null;
        }

        foreach (Lobby lobby in response.Results)
        {
            int index = Array.IndexOf(ServerNames, lobby.Name);
            if (index >= 0)
            {
                cachedLobbies[index] = lobby;
            }
        }

        for (int i = 0; i < ServerNames.Length; i++)
        {
            Lobby lobby = cachedLobbies[i];
            int playerCount = lobby != null ? lobby.MaxPlayers - lobby.AvailableSlots : 0;
            int maxPlayers = lobby != null ? lobby.MaxPlayers : NetworkBootstrap.Instance.MaxPlayersPerRoom;

            if (serverRowLabels[i] != null)
            {
                serverRowLabels[i].text = $"{ServerNames[i]}   {playerCount} / {maxPlayers}";
            }
        }

        NetworkBootstrap.Instance.SetStatus("サーバーを選んでください。");
    }

    private async void OnServerRowClicked(int index)
    {
        SetServerRowsInteractable(false);

        try
        {
            // 一覧を開いた時点の情報は古い可能性がある(その間に他の人がホストになっているかも)。
            // 「ホストになるか参加するか」を決める直前に、必ず最新の状態を取り直す。
            await RefreshServerListAsync();

            if (cachedLobbies[index] != null)
            {
                await JoinServerAsync(cachedLobbies[index].Id, ServerNames[index]);
            }
            else
            {
                await HostServerAsync(index);
            }
        }
        catch (Exception e)
        {
            NetworkBootstrap.Instance.SetStatus($"接続に失敗しました: {e.Message}");
            Debug.LogException(e);
            SetServerRowsInteractable(true);
        }
    }

    // ============================== ホスト側(その場でホストになる) ==============================

    private async Task HostServerAsync(int index)
    {
        NetworkBootstrap.Instance.SetStatus($"{ServerNames[index]} を起動しています...");

        // まずRelayの部屋だけ確保する(この時点ではまだホストとして起動しない)
        string joinCode = await NetworkBootstrap.Instance.PrepareRelayHostAsync();

        // 作ったRelayの参加コードをLobbyのデータとして保存しておく。
        // Visibility.Member = 「そのLobbyに参加した人にだけ見える」設定。
        // (参加コードを不特定多数に公開する必要は無いため)
        var options = new CreateLobbyOptions
        {
            Data = new Dictionary<string, DataObject>
            {
                { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
            }
        };

        Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(
            ServerNames[index], NetworkBootstrap.Instance.MaxPlayersPerRoom, options);

        // 同じタイミングで別の人も同じサーバーを選んでいた場合、同名のLobbyが2つできてしまう。
        // 全員が同じ基準(作成時刻が古い方、同時刻ならID順)で勝者を決めれば、
        // 通信の順序に関係なく必ず1つに絞れる。負けた側はホストにならず、勝者に参加する。
        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync();
        Lobby winner = lobby;
        foreach (Lobby other in response.Results)
        {
            if (other.Name != ServerNames[index]) continue;

            bool otherIsOlder = other.Created < winner.Created
                || (other.Created == winner.Created && string.CompareOrdinal(other.Id, winner.Id) < 0);
            if (otherIsOlder) winner = other;
        }

        if (winner.Id != lobby.Id)
        {
            await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
            await JoinServerAsync(winner.Id, ServerNames[index]);
            return;
        }

        // 自分が勝者と確定してから、初めてホストとして起動して待機画面へ進む
        NetworkBootstrap.Instance.StartHostAndEnterWaitingRoom();

        // シーンを跨いで生き続けるNetworkBootstrap側にハートビートを任せる。
        // (このServerListUIは02_PlayerJoinに移ったら消えてしまうため)
        NetworkBootstrap.Instance.RegisterHostedLobby(lobby.Id);
    }

    // ============================== 参加側 ==============================

    private async Task JoinServerAsync(string lobbyId, string serverName)
    {
        NetworkBootstrap.Instance.SetStatus($"{serverName} に参加しています...");

        // Lobbyに参加する(これで「参加人数」に自分がカウントされるようになる)
        Lobby lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);

        // Lobbyに保存されている参加コードを取り出して、それでRelayに接続する
        if (lobby.Data == null || !lobby.Data.TryGetValue(RelayJoinCodeKey, out DataObject joinCodeData))
        {
            NetworkBootstrap.Instance.SetStatus("このサーバーには参加コードがありません(内部エラー)。");
            return;
        }

        await NetworkBootstrap.Instance.JoinRelayAsync(joinCodeData.Value);
    }

    private void SetServerRowsInteractable(bool interactable)
    {
        foreach (Button button in serverRowButtons)
        {
            if (button != null) button.interactable = interactable;
        }
    }
}
