using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 「インターネット越しにPC同士を繋げる」ところだけを担当するクラス。
///
/// 全体の流れ:
///   1. 起動時に Unity Gaming Services にサインインする(匿名サインイン。アカウント登録は不要)
///   2. ホスト役のPC: Unity Relay に部屋(Allocation)を作ってもらい、
///      その部屋に対応する「参加コード」を発行してもらう → Netcodeのホストとして起動
///   3. 参加側のPC: 参加コードを使って同じ部屋(Relay)に入り、Netcodeのクライアントとして接続する
///
/// 注意: これは「PCとPCがネットワーク的に繋がる」ところまでの責務。
/// - 「どのサーバー(部屋)を選ぶか」というUI/一覧表示は ServerListUI が担当する
///   (ServerListUIがLobbyサービスを使って部屋を探し/作り、参加コードのやり取りだけ
///    このクラスに任せる、という役割分担)
/// - 繋がった後、「その端末のどの入力デバイス(キーボード/ゲームパッド)が
///   どのキャラクターを操作するか」は PlayerInputManager / LocalPlayerInputHandler が別途担当する
/// 役割をあえて分けることで、後から機能を足しても混ざって複雑にならないようにしている。
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    // GameSessionStateなどと同じく、他のスクリプトから簡単に参照できるようにしておく
    public static NetworkBootstrap Instance { get; private set; }

    [Tooltip("進行状況・エラーメッセージを表示するテキスト")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Relay設定")]
    [Tooltip("ホスト自身を除く、Relayの部屋に入れる最大人数。ここが実質の「1部屋あたりの人数上限-1」になる")]
    [SerializeField] private int maxConnections = 3;

    // 1部屋あたりの人数上限(ホスト込み)。ServerListUI側でLobbyのmaxPlayersに使う。
    public int MaxPlayersPerRoom => maxConnections + 1;

    // 接続後に移動する先のシーン名。
    // (PlayerInputManagerがこのシーンにいるので、そこに移らないと
    //  キーボード/ゲームパッドでの「参加」が試せない)
    private const string PlayerJoinSceneName = "02_PlayerJoin";

    // 2重サインインを防ぐためのフラグ
    private bool isSigningIn;

    // 自分がホストとして作ったLobbyのID。ハートビート(生存通知)を送り続けるために使う。
    // ホストでなければnullのまま。
    private string hostedLobbyId;
    private bool heartbeatRunning;

    // Lobbyは一定時間(既定30秒)ハートビートが無いと自動的に消えてしまう。
    // それより短い間隔で送り続ける。
    private const float HeartbeatIntervalSeconds = 15f;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        heartbeatRunning = false;
    }

    private async void Start()
    {
        // ゲーム起動直後にサインインを済ませておく(あとで待たされないように)
        await EnsureSignedInAsync();
    }

    /// <summary>
    /// Unity Gaming Servicesの初期化 + 匿名サインインを行う。
    /// すでにサインイン済みなら何もしない。
    /// </summary>
    public async Task EnsureSignedInAsync()
    {
        if (isSigningIn) return;
        isSigningIn = true;

        SetStatus("サインイン中...");

        // UnityServices自体がまだ初期化されていなければ初期化する。
        // 注意: AuthenticationService.Instance は、UnityServicesの初期化が
        // 完了する前にアクセスすると例外を投げる。なので必ずこちらを先に済ませる。
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            // 匿名サインイン。同じ端末なら次回起動時も同じIDでサインインされる。
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        // サインイン完了は画面には表示しない(タイトル画面が煩雑になるため)。
        // ログにだけ残しておく。
        Debug.Log($"[NetworkBootstrap] Signed in. PlayerId: {AuthenticationService.Instance.PlayerId}");
        isSigningIn = false;
    }

    // ============================== ホスト側 ==============================

    /// <summary>
    /// Relayに部屋を確保する(まだホストとしては起動しない)。
    /// 成功したら、他の人に共有するための「参加コード」を返す。
    /// (このコードはServerListUIがLobbyのデータとして保存し、
    ///  参加者がコードを手入力しなくても自動で使えるようにする)
    /// </summary>
    public async Task<string> PrepareRelayHostAsync()
    {
        await EnsureSignedInAsync();

        SetStatus("部屋を作成中...");

        // Relayサーバー上に部屋を確保してもらう。
        // maxConnectionsは「自分以外」が入れる最大人数。
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

        // 他の人に伝えるための短い参加コードを発行してもらう
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        // NetworkManagerが使う通信部品(UnityTransport)に、
        // 「直接IP接続」ではなく「Relay経由で通信する」ための情報を教える
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

        return joinCode;
    }

    /// <summary>
    /// PrepareRelayHostAsyncで準備した内容でホストとして起動し、待機画面へ進む。
    /// 「本当に自分がホストになってよいか」をLobby側で確認し終えてから呼ぶこと
    /// (先に起動してしまうと、同時に押した相手とホストが二重になる)。
    /// </summary>
    public void StartHostAndEnterWaitingRoom()
    {
        // Netcodeのホストとして起動。この端末がサーバー役も兼ねる。
        NetworkManager.Singleton.StartHost();

        // ホスト(サーバー)側からシーンを切り替える。
        // NetworkConfig.EnableSceneManagement が有効なので、この後に参加してくる
        // クライアントにも自動で同じシーンが適用される(クライアント側で個別に
        // シーンを読み込む処理は不要)。
        // 参加コードを手入力していた頃はホストが「コードが見える画面」に
        // 留まる必要があったが、Lobby一覧からの自動参加に変えたので、
        // 今はもうその必要が無い。ホストになったら即座に待機画面へ進む。
        NetworkManager.Singleton.SceneManager.LoadScene(PlayerJoinSceneName, LoadSceneMode.Single);

        SetStatus("ホストとして開始しました。");
    }

    // ============================== 参加側 ==============================

    /// <summary>
    /// 参加コードを使ってRelayの部屋に入り、Netcodeのクライアントとして接続する。
    ///
    /// 満員などでホスト側のConnectionApprovalCallbackに拒否された場合、Netcodeは
    /// 例外を投げてくれず、代わりに(接続できないまま)切断イベントが来るだけなので、
    /// ここで接続完了/切断のどちらが先に起きるかを見張って、拒否されたときは
    /// 自前で例外に変換している。そうすることで、呼び出し元(ServerListUI)の
    /// 既存のtry/catchがそのまま使え、「接続に失敗しました: 満員です」のように
    /// 拒否理由が参加者の画面にも表示されるようになる。
    /// </summary>
    public async Task JoinRelayAsync(string joinCode)
    {
        await EnsureSignedInAsync();

        SetStatus("部屋に参加中...");

        // 参加コードから、ホストが作った部屋の情報をRelayサーバーに教えてもらう
        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));

        var connectionResult = new TaskCompletionSource<bool>();
        void OnConnected(ulong clientId) => connectionResult.TrySetResult(true);
        void OnDisconnected(ulong clientId) => connectionResult.TrySetResult(false);

        NetworkManager.Singleton.OnClientConnectedCallback += OnConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnected;

        try
        {
            // Netcodeのクライアントとして接続を開始する
            NetworkManager.Singleton.StartClient();

            SetStatus("接続中...");

            bool connected = await connectionResult.Task;
            if (!connected)
            {
                string reason = string.IsNullOrEmpty(NetworkManager.Singleton.DisconnectReason)
                    ? "接続できませんでした。"
                    : NetworkManager.Singleton.DisconnectReason;
                throw new Exception(reason);
            }
        }
        finally
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnected;
        }
    }

    /// <summary>
    /// このマシンがホストとして作ったLobbyのIDを登録し、生存通知(ハートビート)を
    /// 送り続ける。NetworkManager(=このコンポーネント)はシーンをまたいで生き続ける
    /// (DontDestroyOnLoadされる)ので、02_PlayerJoinに移った後もLobbyが消えずに残り、
    /// 3人目・4人目がサーバー一覧から見つけて参加できる状態を保てる。
    /// </summary>
    public void RegisterHostedLobby(string lobbyId)
    {
        hostedLobbyId = lobbyId;
        StartHeartbeat();
    }

    private async void StartHeartbeat()
    {
        if (heartbeatRunning) return;
        heartbeatRunning = true;

        while (heartbeatRunning && hostedLobbyId != null)
        {
            await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds));
            if (!heartbeatRunning || hostedLobbyId == null) break;

            try
            {
                await Unity.Services.Lobbies.LobbyService.Instance.SendHeartbeatPingAsync(hostedLobbyId);
            }
            catch (Exception e)
            {
                // ハートビート1回の失敗くらいでは進行を止めない(ログだけ残す)
                Debug.LogWarning($"[NetworkBootstrap] Lobby heartbeat failed: {e.Message}");
            }
        }
    }

    public void SetStatus(string message)
    {
        Debug.Log($"[NetworkBootstrap] {message}");
        if (statusText != null) statusText.text = message;
    }
}
