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
///      自分もフェードインする。
///      「明るくして」には「明るくし始めるサーバー時刻」が入っていて、全員がその時刻まで待ってから
///      一斉にフェードインする(ローディングの締めくくりの長さや通信の遅れが人によって違っても、
///      画面が見え始めるタイミングと、その後の処理が始まるタイミングがそろう)
///
/// 暗くなっている間(2〜5)は、各自の画面に SceneLoader のローディング表示(バー)を出す。
/// バーは各自の端末の読み込みの進み具合で動き、明るくする前に「100%を見せてから暗転」して消える。
/// (ローディング表示の見た目と演出は SceneLoader / LoadingBar のものをそのまま使う)
/// 自分のバーが100%になっても、まだ他の人の読み込みが終わっていない(=「明るくして」が来ていない)間は、
/// 「他の人の読み込みを待っています」の小さなお知らせ(LoadingWaitNotice)を出す。
///
/// タイトルに戻る(ReturnToTitleAsync)も担当する。ホストが呼ぶと部屋の解散、参加者が呼ぶと自分だけ抜ける。
/// ホストが解散したとき、参加者は「これから解散する」(Disband)を受け取った時点で、自分からタイトルに戻り始める
/// (切断を待っていると、切断がすぐに分からない参加者だけ取り残されることがあるため)。
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

    /// <summary>今シーン遷移(フェード・読み込み)の途中か。ミニゲームが「フェードインが終わってから始める」ために使う</summary>
    public bool IsTransitioning => isTransitioning;

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

    // 自分の読み込みが終わって、他の人の読み込みを待っている間に出すお知らせ
    private const string WaitingForOthersNotice = "他の人の読み込みを待っています。少々お待ちください";

    [Header("ローディング表示")]
    [Tooltip("ONにすると、暗くなっている間だけSceneLoaderのローディング表示(バー)を出す。OFFなら今までどおり暗転だけ")]
    [SerializeField] private bool useLoadingIndicator = true;

    [Header("明るくするタイミングをそろえる")]
    [Tooltip("ローディング表示の締めくくり(バーが100%まで伸びる → 100%を見せる → 暗転)にかかる秒数の目安。\n" +
             "ホストが「明るくして」を送ってから、この秒数(+下の余裕)が過ぎた時刻に全員が一斉に明るくし始める。\n" +
             "短すぎると締めくくりが間に合わなかった人だけ遅れて明るくなる。ローディング表示がOFFのときは使わない")]
    [SerializeField] private float loadingFinishSeconds = 1.3f;

    [Tooltip("「明るくして」が参加者に届くまでの通信の遅れの分の余裕(秒)")]
    [SerializeField] private float fadeInNetworkMargin = 0.2f;

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

    // ---------- ローディングバーで使う(ホスト・参加者の両方) ----------

    // 自分の端末で読み込み中のシーンの進み具合。Netcodeがシーンの読み込みを始めたときに渡してくる。
    // 読み込んでいないときはnull。
    private AsyncOperation loadingOperation;

    // ローディング表示を出してから、「明るくする」段階に入るまでの間かどうか。
    // この間に自分のバーが100%になったら、「他の人の読み込みを待っています」を出す。
    // (「明るくする」段階に入った後にバーが100%になっても、もう待つ必要は無いので出さない)
    private bool isWaitingForFadeIn;

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

            // SceneManagerは接続していないときはnullなので、ある場合だけ外す
            if (networkManager.SceneManager != null) networkManager.SceneManager.OnSceneEvent -= OnSceneEvent;
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

        // シーンの読み込み状況を受け取る(ローディングバー用)。
        // SceneManagerも開始するたびに作り直される。ホストはここに2回来るので、
        // 二重に登録しないよう、一度外してから登録する。
        networkManager.SceneManager.OnSceneEvent -= OnSceneEvent;
        networkManager.SceneManager.OnSceneEvent += OnSceneEvent;
    }

    private void OnClientConnected(ulong clientId)
    {
        // 自分自身の接続が完了したときだけ覚えておく(他の人の接続でも呼ばれるため)
        if (clientId == networkManager.LocalClientId) hasConnected = true;
    }

    // ============================== ローディングバー ==============================

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        // 自分の端末でシーンの読み込みが始まったときだけ、進み具合を覚えておく。
        // (ホストには参加者全員分のイベントも届くので、自分の分以外は無視する)
        if (sceneEvent.SceneEventType != SceneEventType.Load) return;
        if (sceneEvent.ClientId != networkManager.LocalClientId) return;

        loadingOperation = sceneEvent.AsyncOperation;
    }

    private void Update()
    {
        if (loadingOperation == null) return;

        // ローディングバーに進捗を渡す(op.progress は0〜0.9なので0.9で割って0〜1にする)
        // (SceneLoader.LoadSceneAsync でバーに進捗を渡している書き方と同じ)
        SceneLoader.Instance.SetLoadingProgress(loadingOperation.progress / 0.9f);

        // 読み込みが終わったら、もう見なくてよいので手放す。
        // そして、バーが100%になったら「他の人の読み込みを待っています」を出す準備をする。
        if (loadingOperation.isDone)
        {
            loadingOperation = null;
            ShowWaitNoticeWhenBarFilledAsync().Forget();
        }
    }

    /// <summary>
    /// 自分の読み込みが終わった後に呼ぶ。バーが100%まで伸びきったときに、
    /// まだ「明るくする」段階に入っていなければ(=他の人の読み込みが終わっていなければ)、
    /// 「他の人の読み込みを待っています」を出す。消すのは FadeInAtAsync。
    /// </summary>
    private async UniTaskVoid ShowWaitNoticeWhenBarFilledAsync()
    {
        // ローディング表示を出していないなら(設定がOFF、遷移の途中で参加した人など)、お知らせも出さない
        if (!isWaitingForFadeIn) return;

        SceneLoader loader = SceneLoader.Instance;
        CancellationToken ct = transitionCts.Token;

        // バーが100%まで伸びきるのを待つ(途中で「明るくする」段階に入ったら、そこでやめる)
        await UniTask.WaitUntil(() => loader.IsLoadingBarFilled || !isWaitingForFadeIn, cancellationToken: ct);
        if (!isWaitingForFadeIn) return;

        LoadingWaitNotice waitNotice = loader.GetComponent<LoadingWaitNotice>();
        if (waitNotice != null) waitNotice.Show(WaitingForOthersNotice);
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

            // 暗くなったので、ローディング表示を出す
            ShowLoading(loader);

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
        // 全員が明るくし始める時刻(サーバー時刻)を決める。
        // 今すぐではなく、「各自がローディング表示を締めくくる時間」と「メッセージが届くまでの時間」の分だけ先にする。
        float delay = fadeInNetworkMargin;
        if (useLoadingIndicator) delay += loadingFinishSeconds;
        double fadeInStartTime = networkManager.ServerTime.Time + delay;

        // 参加者に「明るくして」を送り、自分(ホスト)も同じ時刻に明るくする
        SendFadeInToClients(fadeInDuration, fadeInStartTime);
        await FadeInAtAsync(loader, fadeInDuration, fadeInStartTime, ct);
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

        // 暗くなったので、ローディング表示を出す
        ShowLoading(SceneLoader.Instance);

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
        // 時刻は0(=もう過ぎている)を渡して、待たずにすぐ明るくする
        ClientFadeInAsync(fadeInDuration, 0).Forget();
    }

    private void OnDisbandReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (networkManager.IsServer) return;

        // この後すぐにホストから切断される。切断されたとき(OnNetworkStopped)に文言を選ぶために覚えておく。
        hostDisbanded = true;

        // ホストの切断を待たずに、自分からタイトルに戻り始める。
        // ホストが切断しても、通信の状態によっては参加者側で「切断された」がすぐに分からず
        // (Relayからの切断の知らせが届かない等)、その参加者だけミニゲームの画面に取り残されることがあるため。
        // ここで戻り始めれば isLeaving が立つので、後から切断が分かっても OnNetworkStopped で二重には動かない。
        Debug.Log($"[NetworkSceneTransition] {HostDisbandedNotice.Replace("\n", "")}");
        ReturnToTitleAsync(HostDisbandedNotice).Forget();
    }

    private void OnFadeInReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (networkManager.IsServer) return;

        // 書き込んだ順番(フェードの秒数 → 明るくし始めるサーバー時刻)と同じ順番で読む
        reader.ReadValueSafe(out float duration);
        reader.ReadValueSafe(out double fadeInStartTime);
        ClientFadeInAsync(duration, fadeInStartTime).Forget();
    }

    /// <param name="duration">フェードインの秒数</param>
    /// <param name="fadeInStartTime">明るくし始めるサーバー時刻。ホストから届いたもの</param>
    private async UniTaskVoid ClientFadeInAsync(float duration, double fadeInStartTime)
    {
        // ホストに言われて暗くしていないなら(遷移の途中で参加した人など)、明るくする必要はない
        if (!isFadedOutByNetwork) return;
        isFadedOutByNetwork = false;

        CancellationToken ct = transitionCts.Token;

        // フェードアウトの途中なら、それが終わってから明るくする
        // (同時に動かすと、後から終わったフェードアウトで真っ黒のまま止まってしまう)
        await UniTask.WaitWhile(() => isClientFadingOut, cancellationToken: ct);

        // ホストが決めた時刻に、ホストや他の参加者とそろって明るくする
        await FadeInAtAsync(SceneLoader.Instance, duration, fadeInStartTime, ct);
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

        // 遷移の途中(ローディング表示が出ている間)に戻ってきた場合、表示が出たままになっているので消す
        // (「他の人の読み込みを待っています」も一緒に消す)
        isWaitingForFadeIn = false;
        HideWaitNotice(loader);
        loader.HideLoadingIndicator();

        await loader.FadeInAsync(fadeIn, ct);
    }

    // ============================== 共通 ==============================

    /// <summary>
    /// 暗くなった後に呼ぶ。設定(useLoadingIndicator)がONのときだけ、ローディング表示を出す。
    /// </summary>
    private void ShowLoading(SceneLoader loader)
    {
        if (!useLoadingIndicator) return;

        loader.ShowLoadingIndicator();

        // ここから「明るくする」段階に入るまでの間に、バーが100%になったら「他の人を待っています」を出す
        isWaitingForFadeIn = true;
    }

    /// <summary>「他の人の読み込みを待っています」を消す。出していなければ何もしない</summary>
    private static void HideWaitNotice(SceneLoader loader)
    {
        LoadingWaitNotice waitNotice = loader.GetComponent<LoadingWaitNotice>();
        if (waitNotice != null) waitNotice.Hide();
    }

    /// <summary>
    /// 明るくする前に呼ぶ。ローディング表示を締めくくってから消す
    /// (バーを100%まで伸ばす → 100%を少し見せる → 暗転 → 非表示)。
    /// 表示を出していなければ、待たずにすぐ終わる。
    /// </summary>
    private static async UniTask EndLoadingAsync(SceneLoader loader, CancellationToken ct)
    {
        await loader.FinishLoadingIndicatorAsync(ct);
        loader.HideLoadingIndicator();
    }

    /// <summary>
    /// 決められたサーバー時刻に明るくする。ホストも参加者もこれを使うので、全員の画面が同時に見え始める。
    /// (サーバー時刻はNetcodeが全員の端末でそろえている時計なので、同じ時刻を渡せば同じ瞬間になる)
    /// </summary>
    /// <param name="duration">フェードインの秒数</param>
    /// <param name="fadeInStartTime">明るくし始めるサーバー時刻。もう過ぎていたら待たずにすぐ明るくする</param>
    private async UniTask FadeInAtAsync(SceneLoader loader, float duration, double fadeInStartTime, CancellationToken ct)
    {
        // 0. 全員の読み込みが終わったので、「他の人の読み込みを待っています」を消す
        isWaitingForFadeIn = false;
        HideWaitNotice(loader);

        // 1. ローディング表示を締めくくって消す(この間も、この後も、画面は真っ暗なまま)
        await EndLoadingAsync(loader, ct);

        // 2. 決められた時刻まで待つ。真っ暗なので、待っている間は何も見えない。
        //    (ローディングの締めくくりが早く終わった人ほど長く待つことになり、全員の足並みがそろう)
        //    途中で切断された場合は時計が使えなくなるので、待つのをやめる
        await UniTask.WaitUntil(
            () => networkManager.ServerTime.Time >= fadeInStartTime || !networkManager.IsListening,
            cancellationToken: ct);

        // 3. 明るくする
        await loader.FadeInAsync(duration, ct);
    }

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

    /// <summary>
    /// 参加者たちに「明るくして」を送る。
    /// 中身はフェードの秒数と、明るくし始めるサーバー時刻(全員が同じ瞬間に明るくするため)。
    /// 受け取る側(OnFadeInReceived)は、ここで書き込んだ順番と同じ順番で読むこと。
    /// </summary>
    private void SendFadeInToClients(float duration, double fadeInStartTime)
    {
        List<ulong> clientIds = GetConnectedClientIdsExceptHost();

        // 送る相手がいない(ホスト1人だけ)ときに送ると、Netcodeがエラーを出すので送らない
        if (clientIds.Count == 0) return;

        using var writer = new FastBufferWriter(sizeof(float) + sizeof(double), Allocator.Temp);
        writer.WriteValueSafe(duration);
        writer.WriteValueSafe(fadeInStartTime);
        networkManager.CustomMessagingManager.SendNamedMessage(FadeInMessage, clientIds, writer);
    }
}
