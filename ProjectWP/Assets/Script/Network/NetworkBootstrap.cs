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
using UnityEngine.UI;

/// <summary>
/// 「インターネット越しにPC同士を繋げる」ところだけを担当するクラス。
///
/// 全体の流れ:
///   1. 起動時に Unity Gaming Services にサインインする(匿名サインイン。アカウント登録は不要)
///   2. ホスト役のPC: Unity Relay に部屋(Allocation)を作ってもらい、
///      その部屋に対応する「参加コード」を発行してもらう → Netcodeのホストとして起動
///   3. 参加側のPC: ホストから共有された参加コードを使って同じ部屋(Relay)に入り、
///      Netcodeのクライアントとして接続する
///
/// 注意: これは「PCとPCがネットワーク的に繋がる」ところまでの責務。
/// 繋がった後、「その端末のどの入力デバイス(キーボード/ゲームパッド)が
/// どのキャラクターを操作するか」は PlayerInputManager / LocalPlayerInputHandler が別途担当する。
/// この2つの役割をあえて分けることで、後から機能を足しても混ざって複雑にならないようにしている。
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    [Header("接続テスト用UI")]
    [Tooltip("「ホストになる」ボタン")]
    [SerializeField] private Button hostButton;

    [Tooltip("「参加する」ボタン")]
    [SerializeField] private Button joinButton;

    [Tooltip("参加コードを入力する欄(参加する側が使う)")]
    [SerializeField] private TMP_InputField joinCodeInputField;

    [Tooltip("発行された参加コードを表示するテキスト(ホストになった側が使う)")]
    [SerializeField] private TextMeshProUGUI joinCodeDisplayText;

    [Tooltip("進行状況・エラーメッセージを表示するテキスト")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Relay設定")]
    [Tooltip("ホスト自身を除く、Relayの部屋に入れる最大人数。ConnectionapprovalhandlerのmaxTotalPlayersと揃えておく")]
    [SerializeField] private int maxConnections = 3;

    // 接続後に移動する先のシーン名。
    // (PlayerInputManagerがこのシーンにいるので、そこに移らないと
    //  キーボード/ゲームパッドでの「参加」が試せない)
    private const string PlayerJoinSceneName = "02_PlayerJoin";

    // 2重サインインを防ぐためのフラグ
    private bool isSigningIn;

    private void Awake()
    {
        // ボタンが押されたときに、対応する処理を呼ぶように登録しておく。
        // (InspectorのOnClickで設定する方法もあるが、コードで登録した方が
        //  「どのボタンで何が起きるか」が1ファイルを読むだけで分かるので、こちらを採用)
        if (hostButton != null) hostButton.onClick.AddListener(OnHostButtonClicked);
        if (joinButton != null) joinButton.onClick.AddListener(OnJoinButtonClicked);
    }

    private async void Start()
    {
        // ゲーム起動直後にサインインを済ませておく(ボタンを押した時点で待たされないように)
        await EnsureSignedInAsync();
    }

    /// <summary>
    /// Unity Gaming Servicesの初期化 + 匿名サインインを行う。
    /// すでにサインイン済みなら何もしない。
    /// </summary>
    private async Task EnsureSignedInAsync()
    {
        if (isSigningIn) return;
        isSigningIn = true;

        // UI表示用の文言は、現状プロジェクトに日本語対応のTMPフォントが無いため
        // (□に文字化けしてしまう)、暫定的に英語にしている。日本語フォントを
        // 用意したら日本語に戻してよい。
        SetStatus("Signing in...");

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

        SetStatus($"Signed in. PlayerId: {AuthenticationService.Instance.PlayerId}");
        isSigningIn = false;
    }

    // ============================== ホスト側 ==============================

    private async void OnHostButtonClicked()
    {
        // 通信中に連打されると、Netcodeが「もう起動中です」という警告を出したり
        // 二重にRelayの部屋を作ろうとしたりしてしまう。押した瞬間に両ボタンを
        // 無効化して、それを防ぐ。
        SetButtonsInteractable(false);

        await EnsureSignedInAsync();

        SetStatus("Creating room...");

        try
        {
            // Relayサーバー上に部屋を確保してもらう。
            // maxConnectionsは「自分以外」が入れる最大人数。
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

            // 他の人に伝えるための短い参加コードを発行してもらう
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // NetworkManagerが使う通信部品(UnityTransport)に、
            // 「直接IP接続」ではなく「Relay経由で通信する」ための情報を教える
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            // Netcodeのホストとして起動。この端末がサーバー役も兼ねる。
            NetworkManager.Singleton.StartHost();

            // 参加コードは、シーン遷移より前にここで表示する。
            // (遷移した02_PlayerJoinにはこのテキスト自体が存在しないため、
            //  遷移後に表示しようとしても意味がない)
            if (joinCodeDisplayText != null) joinCodeDisplayText.text = $"Join Code: {joinCode}";
            SetStatus("Hosting started. Share the join code above and wait for someone to join.");

            // すぐにシーンを切り替えず、自分以外の誰かが実際に接続してくるまで
            // この画面(参加コードが見える状態)に留まる。
            NetworkManager.Singleton.OnClientConnectedCallback += MoveToPlayerJoinSceneWhenSomeoneJoins;
        }
        catch (Exception e)
        {
            SetStatus($"Failed to start host: {e.Message}");
            Debug.LogException(e);
            // 失敗したときはやり直せるように、ボタンを再び押せるようにする
            SetButtonsInteractable(true);
        }
    }

    /// <summary>
    /// 誰かが接続してくるたびに呼ばれる。ホスト自身の接続(ClientId 0)では
    /// 反応せず、自分以外の参加者が実際に加わったときだけシーンを切り替える。
    /// NetworkConfig.EnableSceneManagement が有効なので、この後に参加してくる
    /// クライアントにも自動で同じシーンが適用される(クライアント側で個別に
    /// シーンを読み込む処理は不要)。
    /// </summary>
    private void MoveToPlayerJoinSceneWhenSomeoneJoins(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClientsIds.Count < 2) return;

        NetworkManager.Singleton.OnClientConnectedCallback -= MoveToPlayerJoinSceneWhenSomeoneJoins;
        NetworkManager.Singleton.SceneManager.LoadScene(PlayerJoinSceneName, LoadSceneMode.Single);
    }

    // ============================== 参加側 ==============================

    private async void OnJoinButtonClicked()
    {
        // 通信中に連打されると、Netcodeが「もう起動中です」という警告を出したり
        // 二重に接続を試みたりしてしまう。押した瞬間に両ボタンを無効化して、
        // それを防ぐ。
        SetButtonsInteractable(false);

        await EnsureSignedInAsync();

        string joinCode = joinCodeInputField != null ? joinCodeInputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(joinCode))
        {
            SetStatus("Please enter a join code.");
            SetButtonsInteractable(true);
            return;
        }

        SetStatus("Joining room...");

        try
        {
            // 参加コードから、ホストが作った部屋の情報をRelayサーバーに教えてもらう
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));

            // Netcodeのクライアントとして接続を開始する
            NetworkManager.Singleton.StartClient();

            SetStatus("Connecting...");
        }
        catch (Exception e)
        {
            SetStatus($"Failed to join: {e.Message}");
            Debug.LogException(e);
            // 失敗したときはやり直せるように、ボタンを再び押せるようにする
            SetButtonsInteractable(true);
        }
    }

    private void SetStatus(string message)
    {
        Debug.Log($"[NetworkBootstrap] {message}");
        if (statusText != null) statusText.text = message;
    }

    // ホスト/参加ボタンをまとめて有効・無効にする
    private void SetButtonsInteractable(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (joinButton != null) joinButton.interactable = interactable;
    }
}
