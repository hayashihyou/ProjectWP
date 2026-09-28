using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ネットワークに繋がった後のシーン遷移を、全員の画面でフェード付きで行うクラス。
///
/// 流れ:
///   1. ホストが LoadSceneForAllAsync を呼ぶ
///   2. ホストが参加者全員に「暗くして」(FadeOut)を送り、自分もフェードアウトする
///   3. 参加者は暗くなったら「暗くなった」(FadeOutDone)を返す
///   4. 全員から返事が来たら(または一定時間たったら)、ホストが NetworkSceneManager でシーンを読み込む
///      (参加者はNetcodeが自動で付いてこさせる)
///   5. 全員の読み込みが終わったら(または一定時間たったら)、ホストが「明るくして」(FadeIn)を送り、
///      自分もフェードインする
///
/// タイトルに戻る(ReturnToTitleAsync)も担当する。ホストが呼ぶと部屋の解散、参加者が呼ぶと自分だけ抜ける。
/// ホストとの接続が切れた参加者は、画面にお知らせ(ScreenNotice)を出してから、自動でタイトルに戻る。
/// お知らせの文言は「ホストが解散した」と「ホストとの接続が切れた(回線が落ちた等)」で出し分ける。
///
/// フェードの実処理は SceneLoader(接続前の遷移にも使う、DontDestroyOnLoad の入れ物)に任せ、
/// このクラスは「いつ暗くして、いつ明るくするか」をネットワーク越しにそろえることだけを担当する。
///
/// 置き場所: 01_Title の NetworkManager オブジェクト。NetworkManager はシーンをまたいで残り、
/// ホストにも参加者にも必ずあるので、ここに付けておけばどのシーンからでも使える。
///
/// 通信には Named Message(名前付きメッセージ)を使う。RPC だと NetworkObject の prefab を作って
/// 登録し、スポーンする必要があるが、Named Message なら NetworkManager だけで送受信できる。
/// </summary>
public class NetworkSceneTransition : MonoBehaviour
{
    public static NetworkSceneTransition Instance { get; private set; }

    [Header("フェードの秒数")]
    [SerializeField] private float fadeOutDuration = 0.3f;
    [SerializeField] private float fadeInDuration = 0.3f;

    [Header("待ち時間の上限(秒)。回線の悪い人がいても全体が止まらないようにするため")]
    [Tooltip("ホストが「全員暗くなった」の返事を待つ最大秒数。過ぎたら返事が無い人を待たずに読み込みを始める")]
    [SerializeField] private float fadeOutWaitTimeout = 3f;

    [Tooltip("ホストが「全員読み込み終わった」を待つ最大秒数。過ぎたら全員を明るくする")]
    [SerializeField] private float loadWaitTimeout = 10f;

    [Tooltip("参加者が暗くなった後、ホストから「明るくして」が来ない場合に自分で明るくするまでの秒数(保険)")]
    [SerializeField] private float clientFadeInFallback = 15f;

    [Header("ホストが抜けたときのお知らせ")]
    [Tooltip("参加者の画面にお知らせを出してから、タイトルに戻り始めるまでの秒数")]
    [SerializeField] private float hostLeftNoticeSeconds = 2.5f;

    private const string HostDisbandedNotice = "ホストが部屋を解散しました。\nタイトルに戻ります。";
    private const string HostDisconnectedNotice = "ホストとの接続が切れました。\nタイトルに戻ります。";

    [Header("読み込みの再挑戦")]
    [Tooltip("誰かがまだ同期中などで読み込みを始められなかったときに、もう一度試す回数")]
    [SerializeField] private int loadRetryCount = 5;
    [SerializeField] private float loadRetryInterval = 0.5f;

    // メッセージの名前。他の機能の Named Message とぶつからないよう、頭にクラス名を付けている。
    private const string FadeOutMessage = "NetworkSceneTransition.FadeOut";          // ホスト → 参加者「暗くして」
    private const string FadeOutDoneMessage = "NetworkSceneTransition.FadeOutDone";  // 参加者 → ホスト「暗くなった」
    private const string FadeInMessage = "NetworkSceneTransition.FadeIn";            // ホスト → 参加者「明るくして」
    private const string DisbandMessage = "NetworkSceneTransition.Disband";          // ホスト → 参加者「これから解散する」

    private NetworkManager networkManager;

    // 遷移中のフェードや待ち時間をまとめて止めるためのもの。
    // 「タイトルに戻る」が始まったら、途中だった遷移のフェードが後から画面を明るくしてしまわないよう、
    // これをキャンセルする。このコンポーネントが破棄されたときにも自動でキャンセルされる。
    private CancellationTokenSource transitionCts;

    // ---------- タイトルに戻るときに使う ----------

    // 自分から抜けている最中かどうか。自分で切断したときに「切断された → 自動でタイトルへ」が
    // 二重に動かないようにするために使う。
    private bool isLeaving;

    // このPCが一度でも接続に成功したかどうか。
    // 満員で断られたとき(=まだタイトル画面で、ServerListUIがエラーを表示している)に、
    // 「切断された → タイトルを読み込み直す」が動いてエラー表示を消してしまわないようにするために使う。
    private bool hasConnected;

    // ホストから「これから解散する」の合図を受け取ったかどうか(参加者側)。
    // 切断されたときに、「ホストが自分で解散した」のか「ホストの回線が落ちた等」なのかを
    // 見分けて、お知らせの文言を変えるために使う。
    private bool hostDisbanded;

    // ---------- ホスト側で使う ----------

    // 遷移の途中かどうか(多重起動の防止)
    private bool isTransitioning;

    // 「暗くなった」の返事をまだ返していない参加者のID
    private readonly HashSet<ulong> waitingFadeOutClients = new HashSet<ulong>();

    // ---------- 参加者側で使う ----------

    // ホストに言われて暗くしている最中かどうか。
    // 遷移の途中で参加した人は暗くなっていないので、「明るくして」が来ても無視するために使う
    // (無視しないと、明るい画面がいきなり真っ黒になってから明るくなる、ちらつきが出る)。
    private bool isFadedOutByNetwork;

    // フェードアウトのアニメーション中かどうか。途中で「明るくして」が来たときに、
    // 2つのフェードが同時に動いて取り合いにならないよう、終わるまで待つために使う。
    private bool isClientFadingOut;

    // 何回目のフェードアウトか。保険の自動フェードインが、古いフェードアウトの分で
    // 動いてしまわないようにするための番号。
    private int clientFadeOutCount;

    private void Awake()
    {
        Instance = this;
        networkManager = GetComponent<NetworkManager>();
        transitionCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

        // CustomMessagingManager はホスト/クライアントとして開始するたびに作り直されるので、
        // 開始したタイミングで毎回メッセージの受け取り先を登録する。
        networkManager.OnServerStarted += RegisterMessageHandlers;
        networkManager.OnClientStarted += RegisterMessageHandlers;
        networkManager.OnServerStopped += OnNetworkStopped;
        networkManager.OnClientStopped += OnNetworkStopped;
        networkManager.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.OnServerStarted -= RegisterMessageHandlers;
            networkManager.OnClientStarted -= RegisterMessageHandlers;
            networkManager.OnServerStopped -= OnNetworkStopped;
            networkManager.OnClientStopped -= OnNetworkStopped;
            networkManager.OnClientConnectedCallback -= OnClientConnected;
        }

        transitionCts?.Dispose();
        if (Instance == this) Instance = null;
    }

    private void RegisterMessageHandlers()
    {
        // ホストはOnServerStartedとOnClientStartedの両方が来るので2回呼ばれるが、
        // 同じ名前での登録は上書きされるだけなので問題ない。
        // どのメッセージも、受け取った側が「自分がホストか参加者か」を見て処理するかを決める。
        CustomMessagingManager messaging = networkManager.CustomMessagingManager;
        messaging.RegisterNamedMessageHandler(FadeOutMessage, OnFadeOutReceived);
        messaging.RegisterNamedMessageHandler(FadeOutDoneMessage, OnFadeOutDoneReceived);
        messaging.RegisterNamedMessageHandler(FadeInMessage, OnFadeInReceived);
        messaging.RegisterNamedMessageHandler(DisbandMessage, OnDisbandReceived);
    }

    private void OnClientConnected(ulong clientId)
    {
        // 自分自身の接続が完了したときだけ覚えておく(他の人の接続でも呼ばれるため)
        if (clientId == networkManager.LocalClientId) hasConnected = true;
    }

    private void OnNetworkStopped(bool wasHost)
    {
        // 切断したら、遷移の途中だった状態を捨てる。
        isTransitioning = false;
        waitingFadeOutClients.Clear();

        bool wasConnected = hasConnected;
        hasConnected = false;

        // 参加者が「自分から抜けた」のではなく切断された場合(ホストが解散した、ホストが落ちた、
        // 回線が切れた等)は、取り残されないよう自動でタイトルに戻る。
        // ホストは自分から抜けたときしか止まらないので対象外。ホストはOnServerStoppedと
        // OnClientStoppedの両方でここに来るが、どちらもwasHost=trueなので何もしない。
        if (!wasHost && wasConnected && !isLeaving)
        {
            string notice = hostDisbanded ? HostDisbandedNotice : HostDisconnectedNotice;
            Debug.Log($"[NetworkSceneTransition] {notice.Replace("\n", "")}");
            ReturnToTitleAsync(notice).Forget();
        }
    }

    // ============================== ホスト側 ==============================

    /// <summary>
    /// 全員をフェード付きで指定のシーンへ移動させる。ホストだけが呼べる。
    /// 呼び出し側は、待つ必要がなければ .Forget() を付けて呼んでよい。
    /// </summary>
    public async UniTask LoadSceneForAllAsync(string sceneName)
    {
        if (!networkManager.IsServer)
        {
            Debug.LogWarning($"[NetworkSceneTransition] シーン遷移はホストだけが呼べます: {sceneName}");
            return;
        }

        if (isTransitioning)
        {
            Debug.LogWarning($"[NetworkSceneTransition] 遷移中のため無視しました: {sceneName}");
            return;
        }

        isTransitioning = true;
        SceneLoader loader = SceneLoader.Instance;
        CancellationToken ct = transitionCts.Token;

        try
        {
            // ---------- 1. 全員を暗くする ----------
            waitingFadeOutClients.Clear();
            foreach (ulong clientId in GetConnectedClientIdsExceptHost())
            {
                waitingFadeOutClients.Add(clientId);
            }

            SendToClients(FadeOutMessage, GetConnectedClientIdsExceptHost(), fadeOutDuration);
            await loader.FadeOutAsync(fadeOutDuration, ct);

            // ---------- 2. 全員から「暗くなった」が来るまで待つ(上限あり) ----------
            float waitStart = Time.realtimeSinceStartup;
            await UniTask.WaitUntil(() =>
            {
                // 待っている間に切断した人は、返事が来ることは無いので待つ対象から外す
                waitingFadeOutClients.RemoveWhere(id => !networkManager.ConnectedClients.ContainsKey(id));
                return waitingFadeOutClients.Count == 0
                    || Time.realtimeSinceStartup - waitStart >= fadeOutWaitTimeout;
            }, cancellationToken: ct);

            if (waitingFadeOutClients.Count > 0)
            {
                Debug.LogWarning(
                    $"[NetworkSceneTransition] {waitingFadeOutClients.Count}人から返事が無いまま読み込みを始めます");
            }

            // ---------- 3. シーンを読み込み、全員の完了を待つ(上限あり) ----------
            bool loadCompleted = false;
            void OnLoadEventCompleted(string loadedSceneName, LoadSceneMode mode,
                List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
            {
                if (loadedSceneName == sceneName) loadCompleted = true;
            }

            NetworkSceneManager sceneManager = networkManager.SceneManager;
            sceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            try
            {
                if (!await TryStartLoadAsync(sceneName, ct))
                {
                    // 読み込みを始められなかった。暗いまま止まらないよう、全員を明るくして元のシーンに戻す。
                    Debug.LogError($"[NetworkSceneTransition] '{sceneName}' の読み込みを始められなかったため中止しました");
                    await FadeInEveryoneAsync(loader, ct);
                    return;
                }

                float loadStart = Time.realtimeSinceStartup;
                await UniTask.WaitUntil(
                    () => loadCompleted || Time.realtimeSinceStartup - loadStart >= loadWaitTimeout,
                    cancellationToken: ct);

                if (!loadCompleted)
                {
                    Debug.LogWarning("[NetworkSceneTransition] 読み込みが終わっていない人がいますが、先に明るくします");
                }
            }
            finally
            {
                sceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            }

            // ---------- 4. 全員を明るくする ----------
            await FadeInEveryoneAsync(loader, ct);
        }
        finally
        {
            isTransitioning = false;
        }
    }

    /// <summary>
    /// NetworkSceneManager.LoadScene を呼ぶ。誰かが同期中などで始められなかった場合は、少し待って再挑戦する。
    /// </summary>
    private async UniTask<bool> TryStartLoadAsync(string sceneName, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= loadRetryCount; attempt++)
        {
            SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status == SceneEventProgressStatus.Started) return true;

            Debug.LogWarning($"[NetworkSceneTransition] '{sceneName}' の読み込みを開始できませんでした: {status}");

            // 別のシーン処理の途中(SceneEventInProgress)なら、待てば始められる可能性がある。
            // それ以外(Build Settingsに無いシーン名など)は、何度試しても同じなのですぐ諦める。
            if (status != SceneEventProgressStatus.SceneEventInProgress) return false;

            await UniTask.Delay(TimeSpan.FromSeconds(loadRetryInterval), ignoreTimeScale: true, cancellationToken: ct);
        }

        return false;
    }

    private async UniTask FadeInEveryoneAsync(SceneLoader loader, CancellationToken ct)
    {
        SendToClients(FadeInMessage, GetConnectedClientIdsExceptHost(), fadeInDuration);
        await loader.FadeInAsync(fadeInDuration, ct);
    }

    private void OnFadeOutDoneReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!networkManager.IsServer) return;

        waitingFadeOutClients.Remove(senderClientId);
    }

    // ============================== 参加者側 ==============================

    private void OnFadeOutReceived(ulong senderClientId, FastBufferReader reader)
    {
        // ホスト自身は LoadSceneForAllAsync の中で自分でフェードしているので何もしない
        if (networkManager.IsServer) return;

        reader.ReadValueSafe(out float duration);
        ClientFadeOutAsync(duration).Forget();
    }

    private async UniTaskVoid ClientFadeOutAsync(float duration)
    {
        CancellationToken ct = transitionCts.Token;
        int myFadeOutNumber = ++clientFadeOutCount;

        isFadedOutByNetwork = true;
        isClientFadingOut = true;
        try
        {
            await SceneLoader.Instance.FadeOutAsync(duration, ct);
        }
        finally
        {
            isClientFadingOut = false;
        }

        // 暗くなったことをホストに知らせる。途中で切断していたら送れないので送らない。
        if (networkManager.IsConnectedClient)
        {
            // 中身は空。「暗くなった」という合図だけを送る(書き込みはしないが、確保するサイズは念のため1にしている)
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            networkManager.CustomMessagingManager.SendNamedMessage(
                FadeOutDoneMessage, NetworkManager.ServerClientId, writer);
        }

        // 保険: ホストから「明るくして」が一定時間来なければ、自分で明るくする。
        // (ホストのメッセージが届かなかった、ホストが落ちた、などで暗いまま止まらないようにする)
        await UniTask.Delay(TimeSpan.FromSeconds(clientFadeInFallback), ignoreTimeScale: true, cancellationToken: ct);

        // その間に「明るくして」が来ていたら、または次のフェードアウトが始まっていたら何もしない
        if (!isFadedOutByNetwork || myFadeOutNumber != clientFadeOutCount) return;

        Debug.LogWarning("[NetworkSceneTransition] ホストから「明るくして」が来ないため、自分で明るくします");
        ClientFadeInAsync(fadeInDuration).Forget();
    }

    private void OnDisbandReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (networkManager.IsServer) return;

        // この後すぐにホストから切断される。切断されたとき(OnNetworkStopped)に文言を選ぶために覚えておく。
        hostDisbanded = true;
    }

    private void OnFadeInReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (networkManager.IsServer) return;

        reader.ReadValueSafe(out float duration);
        ClientFadeInAsync(duration).Forget();
    }

    private async UniTaskVoid ClientFadeInAsync(float duration)
    {
        // ホストに言われて暗くしていないなら(遷移の途中で参加した人など)、明るくする必要はない
        if (!isFadedOutByNetwork) return;
        isFadedOutByNetwork = false;

        CancellationToken ct = transitionCts.Token;

        // フェードアウトの途中なら、それが終わってから明るくする
        // (同時に動かすと、後から終わったフェードアウトで真っ黒のまま止まってしまう)
        await UniTask.WaitWhile(() => isClientFadingOut, cancellationToken: ct);
        await SceneLoader.Instance.FadeInAsync(duration, ct);
    }

    // ============================== タイトルに戻る ==============================

    /// <summary>
    /// 部屋から抜けて、01_Titleに戻る。ホストでも参加者でも呼べる。
    ///   ホスト  : 部屋の解散。Lobbyを削除して切断するので、参加者も全員(自動で)タイトルに戻る。
    ///   参加者  : 自分だけが抜ける。Lobbyから自分を外して切断する。
    ///
    /// 流れ: フェードアウト → Lobbyの後始末と切断 → NetworkManagerを破棄 → 01_Titleを読み込む → フェードイン
    ///
    /// NetworkManagerを破棄するのは、01_TitleにもNetworkManagerが置いてあるため。
    /// 古いものを残したまま01_Titleを読み込むと2つになり、Netcodeは「最初の1つ」しか
    /// NetworkManager.Singletonとして使わない(新しい方は無視される)。
    /// 古い方を消してから読み込むことで、01_Titleの新しいNetworkManagerで最初からやり直せる。
    /// </summary>
    /// <param name="notice">
    /// 戻る前に画面に出すお知らせ。nullなら出さない(自分でボタンを押して戻る場合など)。
    /// ホストとの接続が切れた参加者が自動で戻るときに使う。
    /// </param>
    public async UniTask ReturnToTitleAsync(string notice = null)
    {
        if (isLeaving) return;
        isLeaving = true;

        // ホストが解散する場合は、切断する前に「これから解散する」を参加者全員に伝えておく。
        // (参加者はこれを受け取っていれば「解散しました」、受け取っていなければ
        //  「接続が切れました」と、お知らせの文言を出し分ける)
        // 暗転やLobbyの削除より前に送ることで、切断より先に確実に届くようにしている。
        if (networkManager.IsServer)
        {
            SendToClients(DisbandMessage, GetConnectedClientIdsExceptHost(), 0f);
        }

        // 途中だった遷移(フェードや待ち時間)を止める。止めないと、後から「明るくして」が
        // 動いて、タイトルに戻る途中の画面が見えてしまうことがある。
        transitionCts.Cancel();

        // 注意: このコンポーネントは途中でNetworkManagerごと破棄される。
        // 破棄した後に使うものは、先にローカル変数へ取っておき、それだけを使う。
        // (キャンセルの合図も、このコンポーネントではなくSceneLoader側のものを使う)
        SceneLoader loader = SceneLoader.Instance;
        CancellationToken ct = loader.GetCancellationTokenOnDestroy();
        NetworkManager manager = networkManager;
        NetworkBootstrap bootstrap = NetworkBootstrap.Instance;
        float fadeIn = fadeInDuration;
        ScreenNotice screenNotice = loader.GetComponent<ScreenNotice>();

        // ---------- 0. お知らせを出して、読む時間を取る ----------
        // お知らせはフェードの黒画像より手前に出るので、この後暗くなっても文字は見えたまま残る。
        if (notice != null && screenNotice != null)
        {
            screenNotice.Show(notice);
            await UniTask.Delay(TimeSpan.FromSeconds(hostLeftNoticeSeconds), ignoreTimeScale: true, cancellationToken: ct);
        }

        // ---------- 1. 暗くする ----------
        await loader.FadeOutAsync(fadeOutDuration, ct);

        // ---------- 2. Lobbyの後始末と切断 ----------
        if (bootstrap != null)
        {
            await bootstrap.LeaveSessionAsync();
        }
        else if (manager.IsListening)
        {
            manager.Shutdown();
        }

        // Shutdown()はすぐには終わらず、次のフレーム以降に実際の切断処理が行われるので、終わるまで待つ
        await UniTask.WaitWhile(() => manager.ShutdownInProgress, cancellationToken: ct);

        // ---------- 3. NetworkManagerを破棄する ----------
        // (NetworkBootstrapやこのコンポーネントも同じオブジェクトに付いているので一緒に消える)
        Destroy(manager.gameObject);
        await UniTask.WaitUntil(() => NetworkManager.Singleton == null, cancellationToken: ct);

        // ---------- 4. 01_Titleを読み込んで明るくする ----------
        // もうネットワークには繋がっていないので、SceneLoader(Unity標準の読み込み)で移動する。
        // 画面はもう暗いので、ここではフェードを付けずに読み込み、最後に明るくする。
        await loader.LoadSceneAsync(SceneNames.Title, useFade: false, ct: ct);

        // タイトルが見える前に、お知らせを消す
        if (screenNotice != null) screenNotice.Hide();

        await loader.FadeInAsync(fadeIn, ct);
    }

    // ============================== 共通 ==============================

    /// <summary>接続している人のうち、ホスト自身を除いたIDの一覧</summary>
    private List<ulong> GetConnectedClientIdsExceptHost()
    {
        var clientIds = new List<ulong>();
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId) clientIds.Add(clientId);
        }
        return clientIds;
    }

    /// <summary>
    /// 参加者たちにメッセージを送る。中身はフェードの秒数(ホストの設定に全員をそろえるため)。
    /// </summary>
    private void SendToClients(string messageName, List<ulong> clientIds, float duration)
    {
        // 送る相手がいない(ホスト1人だけ)ときに送ると、Netcodeがエラーを出すので送らない
        if (clientIds.Count == 0) return;

        using var writer = new FastBufferWriter(sizeof(float), Allocator.Temp);
        writer.WriteValueSafe(duration);
        networkManager.CustomMessagingManager.SendNamedMessage(messageName, clientIds, writer);
    }
}
