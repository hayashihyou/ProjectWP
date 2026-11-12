using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 1つの音の設定。「ID」と「音声ファイル」と「鳴らし方」をまとめたもの。
/// コードからは ID(文字列)だけで鳴らせるので、ファイルの場所を知らなくてよい。
/// </summary>
[Serializable]
public class SoundEntry
{
    [Tooltip("コードから呼ぶときの名前。例: SoundManager.Instance.PlaySe(\"Common_Decide\")")]
    public string id;

    public AudioClip clip;

    [Tooltip("Bgm: PlayBgmで鳴らす(同時に1曲) / Se: PlaySeで鳴らす(同時にいくつでも)")]
    public SoundCategory category = SoundCategory.Se;

    [Header("確認シーンでの表示")]
    [Tooltip("どの場面で使う音か。サウンド確認シーンの一覧で見出しになる。例: 待機所 / ストップウォッチ")]
    public string sceneLabel;

    [Tooltip("何の音か(日本語)。サウンド確認シーンの一覧のボタンに出る。例: プレイヤー参加")]
    public string usageLabel;

    [Header("鳴らし方")]
    // リストに要素を追加したとき0になっていることがあるので、音が出ないときはまずここを確認
    [Tooltip("この音だけの音量。音量スライダーの値と掛け算される")]
    [Range(0f, 1f)] public float volume = 1f;

    [Tooltip("再生速度。1=等速、2=2倍速(音も高くなる)、0.5=半分の速さ(音も低くなる)")]
    [Range(0.1f, 3f)] public float pitch = 1f;

    [Tooltip("ONにすると止めるまで繰り返し鳴る。BGMは基本ON")]
    public bool loop;

    [Tooltip("ループするとき、曲の終わりのこの秒数をかけて小さくしていき、消えたら最初から流し直す。" +
             "0 なら終わりと始まりをそのままつなぐ(継ぎ目のないループ素材向け)")]
    [Min(0f)] public float loopFadeSeconds;

    [Tooltip("鳴らし始めにこの秒数かけて大きくする。0 ならいきなり鳴る。" +
             "PlaySe / PlayBgm で秒数を指定したときは、そちらが優先される")]
    [Min(0f)] public float fadeInSeconds;

    [Tooltip("止めるときにこの秒数かけて小さくする。0 ならすぐ止まる。" +
             "Stop / StopBgm で秒数を指定したときは、そちらが優先される")]
    [Min(0f)] public float fadeOutSeconds;

    [Tooltip("ONにすると、鳴らすたびに再生速度(音の高さ)を少しずらす。連打したときの単調さを減らす。OFFなら毎回同じ音")]
    public bool randomizePitch;

    [Tooltip("ずらす幅。0.05なら±5%(randomizePitch が ON のときだけ使う)")]
    [Range(0f, 0.5f)] public float randomPitchRange;

    [Tooltip("同じ音をこの秒数以内に連続で鳴らそうとしたら無視する。同じフレームで何重にも鳴って爆音になるのを防ぐ(SEのみ)")]
    [Min(0f)] public float minInterval = 0.05f;
}

/// <summary>
/// ゲーム内の音を一覧で管理するデータ。
/// Assets/Resources/SoundLibrary.asset を SoundManager が自動で読み込む。
///
/// 音を追加するとき:
///   1. 音声ファイルを Assets/Audio/BGM か Assets/Audio/SE に入れる
///   2. ファイルを右クリック →「SoundLibrary に登録」(ID などは自動で入る)。増えた行に使用場面・使用用途を入れる
///      (Entries の「＋」で増やすと1つ上の行がコピーされるので使わない。手順書: Docs/Sound/01_BGM・SEの追加と確認.md)
///   3. コードから SoundManager.Instance.PlaySe("ID") / PlayBgm("ID") で鳴らす
/// </summary>
[CreateAssetMenu(fileName = "SoundLibrary", menuName = "WachaParty/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [SerializeField] private List<SoundEntry> _entries = new List<SoundEntry>();

    // ID → エントリ の辞書。最初に検索したときに作る
    private Dictionary<string, SoundEntry> _lookup;

    /// <summary>登録されている音の一覧(読み取り専用)。サウンド確認シーンで一覧を作るのに使う</summary>
    public IReadOnlyList<SoundEntry> Entries => _entries;

    public bool TryGet(string id, out SoundEntry entry)
    {
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(id, out entry);
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<string, SoundEntry>();
        foreach (var entry in _entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

            if (_lookup.ContainsKey(entry.id))
            {
                Debug.LogWarning($"[SoundLibrary] ID「{entry.id}」が重複しています。先に書かれた方を使います。", this);
                continue;
            }
            _lookup.Add(entry.id, entry);
        }
    }

    // Inspectorで編集したら辞書を作り直す(エディタでPlay中に編集しても反映されるように)
    private void OnValidate()
    {
        _lookup = null;
    }
}
