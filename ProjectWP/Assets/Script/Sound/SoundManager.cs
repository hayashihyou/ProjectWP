using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BGM・SEの再生をまとめて管理するクラス。
///
/// シーンに置く必要はない。ゲーム開始時に自動で生成され、DontDestroyOnLoad で
/// シーンをまたいでも残るので、画面が切り替わってもBGMが途切れない。
///
/// 使い方(どのスクリプトからでも):
///   SoundManager.Instance.PlaySe("Test_SE");                 // SEを1回鳴らす
///   var h = SoundManager.Instance.PlaySe("Test_SE", loop: true); // ループで鳴らして…
///   h.Stop();                                                   // …あとで止める
///   SoundManager.Instance.PlayBgm("Test_BGM");               // BGMを流す(前の曲からは自動でクロスフェード)
///   SoundManager.Instance.StopBgm();                         // BGMをフェードアウトして止める
///   SoundManager.Instance.SetVolume(VolumeType.Bgm, 0.5f);   // BGM音量を半分に(自動で保存される)
///
/// 音は「各プレイヤーのPCでそれぞれ鳴る」だけで、ネットワーク同期はしない。
/// 全員に鳴らしたい音は、同期済みのイベント(ClientRpcなど)を受け取った側で PlaySe を呼ぶ。
/// </summary>
public class SoundManager : MonoBehaviour
{
    // Assets/Resources/ 以下のパス(拡張子なし)
    private const string LibraryResourcePath = "SoundLibrary";

    // SE用のチャンネル数。最初は Initial 個用意し、足りなくなったら Max 個まで増やす
    private const int InitialSeVoices = 8;
    private const int MaxSeVoices = 32;

    private const float DefaultBgmFadeSeconds = 0.5f;

    private const string VolumeKeyPrefix = "Sound.Volume.";

    private static SoundManager _instance;

    /// <summary>
    /// シングルトンインスタンス。初めて触ったときに無ければ自動生成する。
    /// </summary>
    public static SoundManager Instance
    {
        get
        {
            if (_instance != null) return _instance;

            var go = new GameObject("SoundManager");
            _instance = go.AddComponent<SoundManager>(); // Awake で初期化される
            return _instance;
        }
    }

    // ゲーム開始時に生成しておく(最初の PlaySe でカクつかないように)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateOnStartup()
    {
        _ = Instance;
    }

    private SoundLibrary _library;

    // BGMは2チャンネルを交互に使う(片方をフェードアウトしながら、もう片方をフェードインする=クロスフェード)
    private readonly SoundVoice[] _bgmVoices = new SoundVoice[2];
    private int _currentBgmIndex;

    private readonly List<SoundVoice> _seVoices = new List<SoundVoice>();

    // 連打制限用: IDごとに最後に鳴らした時刻
    private readonly Dictionary<string, float> _lastPlayTime = new Dictionary<string, float>();

    // VolumeType の順(Master, Bgm, Se)
    private readonly float[] _volumes = { 1f, 1f, 1f };

    // ミュート(VolumeType の順)。音量とは別に持つので、解除すると元の音量に戻る。保存はしない
    private readonly bool[] _muted = new bool[3];

    private SoundVoice CurrentBgm => _bgmVoices[_currentBgmIndex];

    /// <summary>今流れているBGMのID。流れていなければ null</summary>
    public string CurrentBgmId =>
        CurrentBgm.InUse && !CurrentBgm.IsStopping ? CurrentBgm.Entry.id : null;

    /// <summary>今鳴っているSEの数(動作確認用)</summary>
    public int ActiveSeCount
    {
        get
        {
            int count = 0;
            foreach (var voice in _seVoices)
            {
                if (voice.InUse) count++;
            }
            return count;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        _library = Resources.Load<SoundLibrary>(LibraryResourcePath);
        if (_library == null)
        {
            Debug.LogError($"[SoundManager] Resources/{LibraryResourcePath}.asset が見つかりません。音が鳴りません。");
        }

        for (int i = 0; i < _bgmVoices.Length; i++)
        {
            _bgmVoices[i] = CreateVoice($"BGM {i}");
        }
        for (int i = 0; i < InitialSeVoices; i++)
        {
            _seVoices.Add(CreateVoice($"SE {i}"));
        }

        LoadVolumes();
    }

    private void Update()
    {
        // ポーズ中(Time.timeScale = 0)でもフェードが進むように unscaled を使う
        float dt = Time.unscaledDeltaTime;

        float bgmVolume = EffectiveVolume(VolumeType.Master) * EffectiveVolume(VolumeType.Bgm);
        foreach (var voice in _bgmVoices)
        {
            voice.Tick(dt, bgmVolume);
        }

        float seVolume = EffectiveVolume(VolumeType.Master) * EffectiveVolume(VolumeType.Se);
        foreach (var voice in _seVoices)
        {
            voice.Tick(dt, seVolume);
        }
    }

    // ───────────── SE ─────────────

    /// <summary>
    /// SEを鳴らす。
    /// </summary>
    /// <param name="id">SoundLibrary に登録したID</param>
    /// <param name="volumeScale">この1回だけの音量倍率</param>
    /// <param name="loop">ループするか。省略するとSoundLibraryの設定に従う</param>
    /// <param name="pitch">再生速度。省略するとSoundLibraryの設定に従う</param>
    /// <returns>止めたり速度を変えたりするためのハンドル。鳴らなかったときは SoundHandle.None</returns>
    public SoundHandle PlaySe(string id, float volumeScale = 1f, bool? loop = null, float? pitch = null)
    {
        if (!TryGetEntry(id, out var entry)) return SoundHandle.None;

        if (entry.category != SoundCategory.Se)
        {
            Debug.LogWarning($"[SoundManager] 「{id}」はBGMとして登録されています。PlayBgm を使ってください。");
        }

        // 連打制限: 直前に同じ音を鳴らしたばかりなら無視する
        float now = Time.unscaledTime;
        if (entry.minInterval > 0f
            && _lastPlayTime.TryGetValue(id, out float last)
            && now - last < entry.minInterval)
        {
            return SoundHandle.None;
        }

        var voice = GetFreeSeVoice();
        if (voice == null)
        {
            Debug.LogWarning($"[SoundManager] SEの同時再生数が上限({MaxSeVoices})です。「{id}」は鳴らしませんでした。");
            return SoundHandle.None;
        }

        _lastPlayTime[id] = now;
        voice.Begin(entry, volumeScale, RandomizePitch(pitch ?? entry.pitch, entry),
            loop ?? entry.loop, fadeInSeconds: 0f);
        return new SoundHandle(voice);
    }

    /// <summary>鳴っているSEをすべて止める</summary>
    public void StopAllSe(float fadeSeconds = 0f)
    {
        foreach (var voice in _seVoices)
        {
            voice.Stop(fadeSeconds);
        }
    }

    // ───────────── BGM ─────────────

    /// <summary>
    /// BGMを流す。別の曲が流れていたら、fadeSeconds かけてクロスフェードで切り替える。
    /// 同じ曲が既に流れていれば何もしない(シーンをまたいでも最初から再生し直さない)。
    /// </summary>
    /// <param name="restartIfSame">true なら同じ曲でも最初から流し直す</param>
    public SoundHandle PlayBgm(string id, float fadeSeconds = DefaultBgmFadeSeconds, bool restartIfSame = false)
    {
        if (!TryGetEntry(id, out var entry)) return SoundHandle.None;

        if (entry.category != SoundCategory.Bgm)
        {
            Debug.LogWarning($"[SoundManager] 「{id}」はSEとして登録されています。PlaySe を使ってください。");
        }

        if (!restartIfSame && CurrentBgmId == id)
        {
            // 一時停止中なら再開だけする
            CurrentBgm.Resume();
            return new SoundHandle(CurrentBgm);
        }

        // 今の曲をフェードアウトさせ、もう片方のチャンネルで新しい曲をフェードインさせる
        CurrentBgm.Stop(fadeSeconds);
        _currentBgmIndex = 1 - _currentBgmIndex;

        // 前回のクロスフェードがまだ終わっていない場合は、残っている古い曲をすぐ止めて上書きする
        CurrentBgm.Begin(entry, 1f, RandomizePitch(entry.pitch, entry),
            entry.loop, fadeSeconds);
        return new SoundHandle(CurrentBgm);
    }

    /// <summary>BGMを止める(デフォルトは0.5秒でフェードアウト)</summary>
    public void StopBgm(float fadeSeconds = DefaultBgmFadeSeconds)
    {
        foreach (var voice in _bgmVoices)
        {
            voice.Stop(fadeSeconds);
        }
    }

    /// <summary>今流れているBGMの再生速度を変える。1=等速、1.5=1.5倍速(焦り演出など)</summary>
    public void SetBgmPitch(float pitch)
    {
        CurrentBgm.SetPitch(pitch);
    }

    public void PauseBgm()
    {
        foreach (var voice in _bgmVoices) voice.Pause();
    }

    public void ResumeBgm()
    {
        foreach (var voice in _bgmVoices) voice.Resume();
    }

    // ───────────── 全体 ─────────────

    /// <summary>BGMもSEもすべて止める</summary>
    public void StopAll(float fadeSeconds = 0f)
    {
        StopBgm(fadeSeconds);
        StopAllSe(fadeSeconds);
    }

    /// <summary>
    /// 今鳴っている音をすべて一時停止する(ポーズ画面用)。
    /// 一時停止中でも新しく PlaySe した音は普通に鳴るので、ポーズメニューの決定音などは使える。
    /// </summary>
    public void PauseAll()
    {
        PauseBgm();
        foreach (var voice in _seVoices) voice.Pause();
    }

    public void ResumeAll()
    {
        ResumeBgm();
        foreach (var voice in _seVoices) voice.Resume();
    }

    // ───────────── 音量 ─────────────

    public float GetVolume(VolumeType type) => _volumes[(int)type];

    /// <summary>
    /// 音量を設定する(0〜1)。再生中の音にもすぐ反映され、次回起動時のために保存もされる。
    /// </summary>
    public void SetVolume(VolumeType type, float volume)
    {
        volume = Mathf.Clamp01(volume);
        _volumes[(int)type] = volume;
        PlayerPrefs.SetFloat(VolumeKeyPrefix + type, volume);
    }

    public bool IsMuted(VolumeType type) => _muted[(int)type];

    /// <summary>
    /// ミュートする / 解除する。音量の設定はそのまま残るので、解除すると元の音量に戻る。
    /// 再生中の音にもすぐ反映される(止めるのではなく音量0にするので、解除すると続きから聞こえる)。
    /// </summary>
    public void SetMuted(VolumeType type, bool muted)
    {
        _muted[(int)type] = muted;
    }

    private float EffectiveVolume(VolumeType type) => _muted[(int)type] ? 0f : _volumes[(int)type];

    private void LoadVolumes()
    {
        for (int i = 0; i < _volumes.Length; i++)
        {
            var type = (VolumeType)i;
            _volumes[i] = PlayerPrefs.GetFloat(VolumeKeyPrefix + type, 1f);
        }
    }

    // SetFloat はメモリ上に書くだけなので、アプリを閉じる・裏に回るタイミングでディスクに書き出す
    private void OnApplicationPause(bool paused)
    {
        if (paused) PlayerPrefs.Save();
    }

    private void OnApplicationQuit()
    {
        PlayerPrefs.Save();
    }

    // ───────────── 登録内容の確認 ─────────────

    /// <summary>
    /// SoundLibrary に登録されている設定を返す。未登録・音声ファイル未設定なら null(警告は出さない)。
    /// 「この音はループか」などを調べたいとき用。
    /// </summary>
    public SoundEntry FindEntry(string id)
    {
        if (_library == null || string.IsNullOrEmpty(id)) return null;
        return _library.TryGet(id, out var entry) && entry.clip != null ? entry : null;
    }

    // ───────────── 内部処理 ─────────────

    private bool TryGetEntry(string id, out SoundEntry entry)
    {
        entry = null;
        if (_library == null || string.IsNullOrEmpty(id)) return false;

        if (!_library.TryGet(id, out entry))
        {
            Debug.LogWarning($"[SoundManager] ID「{id}」が SoundLibrary に登録されていません。");
            return false;
        }
        if (entry.clip == null)
        {
            Debug.LogWarning($"[SoundManager] 「{id}」に音声ファイル(Clip)が設定されていません。");
            return false;
        }
        return true;
    }

    private SoundVoice CreateVoice(string name)
    {
        // チャンネルごとに子オブジェクトを作る(Hierarchyで見たときどれが鳴っているか分かりやすいように)
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        return new SoundVoice(child.AddComponent<AudioSource>());
    }

    /// <summary>空いているSEチャンネルを返す。無ければ増やし、上限なら一番古い(ループしない)音を止めて譲ってもらう</summary>
    private SoundVoice GetFreeSeVoice()
    {
        foreach (var voice in _seVoices)
        {
            if (!voice.InUse) return voice;
        }

        if (_seVoices.Count < MaxSeVoices)
        {
            var voice = CreateVoice($"SE {_seVoices.Count}");
            _seVoices.Add(voice);
            return voice;
        }

        // ループSEは「止めるまで鳴っていてほしい音」なので奪わない
        SoundVoice oldest = null;
        foreach (var voice in _seVoices)
        {
            if (voice.Source.loop) continue;
            if (oldest == null || voice.StartTime < oldest.StartTime) oldest = voice;
        }
        return oldest; // Begin の中で前の音は止められる
    }

    // ずらす設定(randomizePitch)が ON のときだけ、再生速度を ±randomPitchRange の範囲でずらす
    private static float RandomizePitch(float pitch, SoundEntry entry)
    {
        if (pitch <= 0f) pitch = 1f; // 未設定(0)のときは等速にしておく
        float range = entry.randomPitchRange;
        if (!entry.randomizePitch || range <= 0f) return pitch;
        return pitch * Random.Range(1f - range, 1f + range);
    }
}
