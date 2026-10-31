/// <summary>
/// PlaySe / PlayBgm が返す「鳴らした音の引換券」。
/// ループSEをあとで止めたいときなどに、変数に持っておいて使う。
///
/// 例:
///   SoundHandle _footstep;
///   _footstep = SoundManager.Instance.PlaySe("Footstep", loop: true);
///   ...
///   _footstep.Stop();
///
/// 音が鳴り終わったあとや、別の音にチャンネルを譲ったあとに操作しても何も起きない(エラーにもならない)。
/// </summary>
public readonly struct SoundHandle
{
    /// <summary>何も鳴らなかったとき(IDが見つからない・連打制限に引っかかった等)に返る空のハンドル</summary>
    public static readonly SoundHandle None = default;

    private readonly SoundVoice _voice;
    private readonly int _generation;

    internal SoundHandle(SoundVoice voice)
    {
        _voice = voice;
        _generation = voice.Generation;
    }

    /// <summary>まだこの音が鳴っている(一時停止中も含む)かどうか</summary>
    public bool IsPlaying => IsValid;

    /// <summary>この音のID。鳴り終わっていたら null</summary>
    public string Id => IsValid ? _voice.Entry.id : null;

    private bool IsValid => _voice != null && _voice.InUse && _voice.Generation == _generation;

    /// <summary>止める。fadeSeconds を指定するとその秒数かけて小さくしてから止める</summary>
    public void Stop(float fadeSeconds = 0f)
    {
        if (IsValid) _voice.Stop(fadeSeconds);
    }

    public void Pause()
    {
        if (IsValid) _voice.Pause();
    }

    public void Resume()
    {
        if (IsValid) _voice.Resume();
    }

    /// <summary>再生速度を変える。1=等速、2=2倍速、0.5=半分(0.1〜3の範囲)</summary>
    public void SetPitch(float pitch)
    {
        if (IsValid) _voice.SetPitch(pitch);
    }

    /// <summary>この音だけの音量倍率を変える(1=鳴らしたときのまま)</summary>
    public void SetVolume(float volumeScale)
    {
        if (IsValid) _voice.SetVolumeScale(volumeScale);
    }
}
