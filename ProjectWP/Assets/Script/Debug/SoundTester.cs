using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 【テスト用】SoundManager の動作確認用。
/// 最初のシーン(01_Title)の空のGameObjectに付けて Play する。シーンをまたいでも残る。
///
///   1キー: SE(Test_SE)を鳴らす   … 連打すると重なって鳴る/少しずつ高さが変わる
///   2キー: BGM(Test_BGM)を流す   … 流れている間に押しても最初からにはならない
///   3キー: すべての音を止める
///
/// 画面左上のパネルで、音量・再生速度・一時停止・フェードを試せる。
/// 「SE ▶」「BGM ▶」タブでは、場面ごとに登録した音を1つずつ鳴らして確認できる。
/// 「参加者」タブ(ホストだけ)では、参加者ごとに BGM・SE をミュートできる。
/// MPPM で1台のPCに4人分立ち上げたとき、音を鳴らすのを1人だけにする、といった使い方をする。
/// 本番ではこのコンポーネントを外すか、このファイル(と.meta)を消すだけでよい。
/// </summary>
public class SoundTester : MonoBehaviour
{
    // ホスト → 参加者 に「ミュートの状態」を送るメッセージの名前
    // (NetworkObject を置かずに送れる「名前つきメッセージ」を使う。テスト用の道具をシーンの作りに混ぜないため)
    private const string MuteMessageName = "SoundTester.Mute";

    private static SoundTester _instance;

    /// <summary>参加者1人分のミュートの状態(ホストが覚えておく)</summary>
    private struct MuteState
    {
        public bool Bgm;
        public bool Se;
    }

    /// <summary>一覧に並べる音1つ分(場面 / ボタンに出す名前 / SoundLibrary のID)</summary>
    private readonly struct SoundItem
    {
        public readonly string Scene;
        public readonly string Label;
        public readonly string Id;

        public SoundItem(string scene, string label, string id)
        {
            Scene = scene;
            Label = label;
            Id = id;
        }
    }

    // 音を追加したらここにも1行足すと、テストパネルから鳴らせるようになる
    private static readonly SoundItem[] SeItems =
    {
        new SoundItem("共通(メニュー)", "決定", "Common_Decide"),
        new SoundItem("共通(メニュー)", "押せない", "Common_Disabled"),
        new SoundItem("待機所", "プレイヤー参加", "Common_PlayerJoin"),
        new SoundItem("待機所", "プレイヤー退出", "Common_PlayerLeave"),
        new SoundItem("待機所", "準備完了", "Common_Ready"),
        new SoundItem("インゲーム(共通)", "カメラ演出(歓声)", "Amb_Cheer"),
        new SoundItem("インゲーム(共通)", "スタート", "Common_Start"),
        new SoundItem("インゲーム(共通)", "フィニッシュ", "Common_Finish"),
        new SoundItem("インゲーム(共通)", "時間切れ", "Common_TimeUp"),
        new SoundItem("ストップウォッチ", "目標時間の表示", "Stopwatch_TargetShow"),
        new SoundItem("ストップウォッチ", "時間を止める", "Stopwatch_Stop"),
        new SoundItem("ストップウォッチ", "時間が進んでいる(ループ)", "Stopwatch_Tick"),
        new SoundItem("ストップウォッチ", "公開前", "Stopwatch_RevealBefore"),
        new SoundItem("ストップウォッチ", "止めた時間の公開", "Stopwatch_Reveal"),
        new SoundItem("テスト", "テストSE", "Test_SE"),
    };

    private static readonly SoundItem[] BgmItems =
    {
        new SoundItem("タイトル", "タイトル", "BGM_Title"),
        new SoundItem("待機所・ステージ選択", "待機所・ステージ選択", "BGM_Lobby"),
        new SoundItem("インゲーム", "ストップウォッチ", "BGM_Stopwatch"),
        new SoundItem("リザルト", "リザルト", "BGM_Result"),
        new SoundItem("テスト", "テストBGM", "Test_BGM"),
    };

    private static readonly string[] TabNames = { "SE ▶", "BGM ▶", "その他" };
    private static readonly string[] HostTabNames = { "SE ▶", "BGM ▶", "その他", "参加者" };

    [SerializeField] private string _seId = "Test_SE";
    [SerializeField] private string _bgmId = "Test_BGM";

    // ループSEはIDごとにハンドルを持っておき、もう一度押したら止める
    private readonly Dictionary<string, SoundHandle> _loopHandles = new Dictionary<string, SoundHandle>();

    // ホストだけが使う: 参加者(クライアントID)ごとのミュートの状態
    private readonly Dictionary<ulong, MuteState> _muteStates = new Dictionary<ulong, MuteState>();

    // ミュートのメッセージを受け取る登録をした NetworkManager(接続し直したら登録し直すため)
    private NetworkManager _registeredNetworkManager;

    private int _tab;
    private Vector2 _scroll;
    private float _bgmPitch = 1f;
    private bool _paused;
    private bool _collapsed;

    private void Awake()
    {
        // シーンをまたいでも残す。別のシーンにも置いてあった場合は、先にあった方を使う
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this) { _instance = null; }
        UnregisterMuteHandler();
    }

    private void Update()
    {
        UpdateMuteHandlerRegistration();

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
        {
            SoundManager.Instance.PlaySe(_seId);
        }
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
        {
            SoundManager.Instance.PlayBgm(_bgmId);
            _bgmPitch = 1f;
        }
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
        {
            StopAll();
        }
    }

    private void OnGUI()
    {
        var sound = SoundManager.Instance;

        // たたんでいる間は、開くボタンだけ出す(ゲーム画面の邪魔にならないように)
        if (_collapsed)
        {
            if (GUI.Button(new Rect(10, 10, 140, 30), "サウンドテスト ▼")) { _collapsed = false; }
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 360, Screen.height - 20), GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("1: SE   2: BGM   3: 全停止");
        if (GUILayout.Button("たたむ ▲", GUILayout.Width(80))) { _collapsed = true; }
        GUILayout.EndHorizontal();
        GUILayout.Label($"BGM: {sound.CurrentBgmId ?? "(なし)"}   鳴っているSE: {sound.ActiveSeCount}");
        GUILayout.Label($"この画面のミュート  BGM: {(sound.IsMuted(VolumeType.Bgm) ? "ON" : "OFF")}   SE: {(sound.IsMuted(VolumeType.Se) ? "ON" : "OFF")}");

        // 音量(動かすと自動で保存され、次回 Play しても同じ値になる)
        VolumeSlider("Master", VolumeType.Master);
        VolumeSlider("BGM", VolumeType.Bgm);
        VolumeSlider("SE", VolumeType.Se);

        if (GUILayout.Button("すべて停止"))
        {
            StopAll();
        }

        GUILayout.Space(6);
        // 「参加者」タブはホストのときだけ出す
        string[] tabs = IsHost() ? HostTabNames : TabNames;
        if (_tab >= tabs.Length) { _tab = 0; }
        _tab = GUILayout.Toolbar(_tab, tabs);

        _scroll = GUILayout.BeginScrollView(_scroll);
        switch (_tab)
        {
            case 0: DrawSoundList(SeItems, isBgm: false); break;
            case 1: DrawSoundList(BgmItems, isBgm: true); break;
            case 2: DrawOtherControls(); break;
            default: DrawParticipants(); break;
        }
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    /// <summary>場面ごとの見出し + 音ごとの再生ボタンを並べる</summary>
    private void DrawSoundList(SoundItem[] items, bool isBgm)
    {
        var sound = SoundManager.Instance;
        string currentScene = null;

        foreach (var item in items)
        {
            // 場面が変わったら見出しを出す
            if (item.Scene != currentScene)
            {
                currentScene = item.Scene;
                GUILayout.Space(4);
                GUILayout.Label($"■ {currentScene}");
            }

            var entry = sound.FindEntry(item.Id);

            GUILayout.BeginHorizontal();
            GUILayout.Label(item.Label, GUILayout.Width(200));

            if (entry == null)
            {
                // まだ音を用意していないID
                GUI.enabled = false;
                GUILayout.Button("(未登録)");
                GUI.enabled = true;
            }
            else if (isBgm)
            {
                bool playing = sound.CurrentBgmId == item.Id;
                if (GUILayout.Button(playing ? "■ 停止" : "▶ 再生"))
                {
                    if (playing) sound.StopBgm();
                    else sound.PlayBgm(item.Id);
                    _bgmPitch = 1f;
                }
            }
            else if (entry.loop)
            {
                _loopHandles.TryGetValue(item.Id, out var handle);
                if (GUILayout.Button(handle.IsPlaying ? "■ 停止" : "▶ 再生"))
                {
                    if (handle.IsPlaying) handle.Stop();
                    else _loopHandles[item.Id] = sound.PlaySe(item.Id);
                }
            }
            else
            {
                if (GUILayout.Button("▶ 再生"))
                {
                    sound.PlaySe(item.Id);
                }
            }
            GUILayout.EndHorizontal();
        }
    }

    /// <summary>再生速度・一時停止・フェードなど、仕組みそのものの確認用</summary>
    private void DrawOtherControls()
    {
        var sound = SoundManager.Instance;

        GUILayout.Label($"BGM 再生速度: {_bgmPitch:0.00}");
        float pitch = GUILayout.HorizontalSlider(_bgmPitch, 0.5f, 2f);
        if (!Mathf.Approximately(pitch, _bgmPitch))
        {
            _bgmPitch = pitch;
            sound.SetBgmPitch(_bgmPitch);
        }

        GUILayout.Space(8);

        // ループ指定なしの音でも、loop: true を渡せばループで鳴らせる
        _loopHandles.TryGetValue(_seId, out var loopSe);
        if (GUILayout.Button(loopSe.IsPlaying ? "テストSEのループを止める" : "テストSEをループ再生"))
        {
            if (loopSe.IsPlaying) loopSe.Stop();
            else _loopHandles[_seId] = sound.PlaySe(_seId, loop: true);
        }

        if (GUILayout.Button(_paused ? "再開" : "一時停止(全部)"))
        {
            if (_paused) sound.ResumeAll();
            else sound.PauseAll();
            _paused = !_paused;
        }

        if (GUILayout.Button("BGMをフェードアウト(2秒)"))
        {
            sound.StopBgm(2f);
        }

        if (GUILayout.Button("テストBGMを最初から(1秒クロスフェード)"))
        {
            sound.PlayBgm(_bgmId, fadeSeconds: 1f, restartIfSame: true);
            _bgmPitch = 1f;
        }
    }

    // ==================== 参加者ごとのミュート(ホスト) ====================

    private static bool IsHost()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening && nm.IsServer;
    }

    /// <summary>参加者ごとに BGM・SE のミュートを切り替えるボタンと、全員まとめて切り替えるボタン</summary>
    private void DrawParticipants()
    {
        NetworkManager nm = NetworkManager.Singleton;
        IReadOnlyList<ulong> clients = nm.ConnectedClientsIds;

        // 1人でもミュートしていない音があれば「全員ミュート」、全部ミュート済みなら「全員ミュート解除」
        bool anyUnmuted = false;
        foreach (ulong id in clients)
        {
            MuteState s = GetMuteState(id);
            if (!s.Bgm || !s.Se) { anyUnmuted = true; break; }
        }
        if (GUILayout.Button(anyUnmuted ? "全員ミュート" : "全員ミュート解除"))
        {
            foreach (ulong id in clients)
            {
                SetMuteState(id, new MuteState { Bgm = anyUnmuted, Se = anyUnmuted });
            }
        }

        GUILayout.Space(6);
        foreach (ulong id in clients)
        {
            MuteState s = GetMuteState(id);

            GUILayout.BeginHorizontal();
            GUILayout.Label(GetParticipantLabel(nm, id), GUILayout.Width(110));
            if (GUILayout.Button(s.Bgm ? "BGM: ミュート中" : "BGM: 鳴る"))
            {
                s.Bgm = !s.Bgm;
                SetMuteState(id, s);
            }
            if (GUILayout.Button(s.Se ? "SE: ミュート中" : "SE: 鳴る"))
            {
                s.Se = !s.Se;
                SetMuteState(id, s);
            }
            GUILayout.EndHorizontal();
        }
    }

    // 「1P(自分)」のように、誰のことか分かる名前にする(キャラクターがまだいなければクライアントIDを出す)
    private static string GetParticipantLabel(NetworkManager nm, ulong clientId)
    {
        string name = $"ID {clientId}";
        if (nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
        {
            PlayerSlot slot = client.PlayerObject.GetComponent<PlayerSlot>();
            if (slot != null && slot.SlotIndex >= 0) { name = $"{slot.SlotIndex + 1}P"; }
        }
        return clientId == nm.LocalClientId ? name + "(自分)" : name;
    }

    private MuteState GetMuteState(ulong clientId)
    {
        return _muteStates.TryGetValue(clientId, out MuteState s) ? s : default;
    }

    // 覚えておいて、その人の画面に反映する(自分ならその場で、他の人ならメッセージを送る)
    private void SetMuteState(ulong clientId, MuteState state)
    {
        _muteStates[clientId] = state;

        NetworkManager nm = NetworkManager.Singleton;
        if (clientId == nm.LocalClientId)
        {
            ApplyMute(state.Bgm, state.Se);
            return;
        }

        using (var writer = new FastBufferWriter(2, Allocator.Temp))
        {
            writer.WriteValueSafe(state.Bgm);
            writer.WriteValueSafe(state.Se);
            nm.CustomMessagingManager.SendNamedMessage(MuteMessageName, clientId, writer);
        }
    }

    private static void ApplyMute(bool bgm, bool se)
    {
        SoundManager.Instance.SetMuted(VolumeType.Bgm, bgm);
        SoundManager.Instance.SetMuted(VolumeType.Se, se);
    }

    // ==================== ミュートのメッセージを受け取る(参加者) ====================

    // 接続したら受け取る登録をし、切断したら外す(接続し直すと CustomMessagingManager が作り直されるため毎回やる)
    private void UpdateMuteHandlerRegistration()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool listening = nm != null && nm.IsListening && nm.CustomMessagingManager != null;

        if (listening && _registeredNetworkManager == null)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MuteMessageName, HandleMuteMessage);
            _registeredNetworkManager = nm;
        }
        else if (!listening && _registeredNetworkManager != null)
        {
            UnregisterMuteHandler();
            // 切断したら、ホストに止められていたミュートも、ホストが覚えていた状態も元に戻す
            ApplyMute(false, false);
            _muteStates.Clear();
        }
    }

    private void UnregisterMuteHandler()
    {
        if (_registeredNetworkManager == null) { return; }
        if (_registeredNetworkManager.CustomMessagingManager != null)
        {
            _registeredNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MuteMessageName);
        }
        _registeredNetworkManager = null;
    }

    private void HandleMuteMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out bool bgm);
        reader.ReadValueSafe(out bool se);
        ApplyMute(bgm, se);
    }

    private void StopAll()
    {
        SoundManager.Instance.StopAll();
        _paused = false;
    }

    private static void VolumeSlider(string label, VolumeType type)
    {
        var sound = SoundManager.Instance;
        float current = sound.GetVolume(type);

        GUILayout.Label($"{label} 音量: {current:0.00}");
        float value = GUILayout.HorizontalSlider(current, 0f, 1f);
        if (!Mathf.Approximately(value, current))
        {
            sound.SetVolume(type, value);
        }
    }
}
