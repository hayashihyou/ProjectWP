using UnityEngine;

/// <summary>
/// AudioSource 1つ分の「再生チャンネル」。SoundManager の内部でだけ使う。
///
/// AudioSource をそのまま使わずにこのクラスで包んでいる理由:
///   ・フェードの途中経過(今の音量倍率)を覚えておく必要がある
///   ・音量スライダーが動いたとき、再生中の音にもすぐ反映したい
///   ・使い回すので「今どの再生か」を Generation(世代番号)で区別し、
///     古い SoundHandle から操作されても関係ない音を止めないようにする
/// </summary>
internal sealed class SoundVoice
{
    public readonly AudioSource Source;

    /// <summary>再生し終わる(または止められる)たびに1増える番号</summary>
    public int Generation { get; private set; }

    public bool InUse { get; private set; }
    public bool IsPaused { get; private set; }
    public SoundEntry Entry { get; private set; }

    /// <summary>再生を始めた時刻。SEが鳴らせる上限に達したとき「一番古い音」を探すのに使う</summary>
    public float StartTime { get; private set; }

    /// <summary>フェードアウトして止まる途中かどうか</summary>
    public bool IsStopping => _fading && _stopAfterFade;

    private float _volumeScale = 1f; // 鳴らすときに個別に指定された音量倍率
    private float _loopFadeSeconds;  // 0 より大きければ、曲終わりをこの秒数でフェードさせて自分でループする
    private float _fade = 1f;        // フェードによる音量倍率(0〜1)

    private bool _fading;
    private bool _stopAfterFade;
    private float _fadeFrom;
    private float _fadeTo;
    private float _fadeDuration;
    private float _fadeTime;

    public SoundVoice(AudioSource source)
    {
        Source = source;
        Source.playOnAwake = false;
        Source.spatialBlend = 0f; // 2D(距離で音量が変わらない)
    }

    public void Begin(SoundEntry entry, float volumeScale, float pitch, bool loop, float fadeInSeconds)
    {
        if (InUse) Release();

        Entry = entry;
        InUse = true;
        IsPaused = false;
        StartTime = Time.unscaledTime;
        _volumeScale = volumeScale;

        Source.clip = entry.clip;
        Source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);

        // 曲終わりをフェードさせるループは、AudioSource のループを使わずに自分で最初から流し直す
        // (AudioSource のループだと、終わりと始まりが音量そのままでつながってしまうため)
        _loopFadeSeconds = loop ? Mathf.Min(entry.loopFadeSeconds, entry.clip.length * 0.5f) : 0f;
        Source.loop = loop && _loopFadeSeconds <= 0f;

        if (fadeInSeconds > 0f)
        {
            _fade = 0f;
            StartFade(1f, fadeInSeconds, stopAfter: false);
        }
        else
        {
            _fade = 1f;
            _fading = false;
        }

        Source.Play();
    }

    /// <summary>止める。fadeSeconds が 0 ならすぐ止まる</summary>
    public void Stop(float fadeSeconds)
    {
        if (!InUse) return;

        if (fadeSeconds <= 0f)
        {
            Release();
            return;
        }
        // 一時停止中だとフェードが進まないので、再開してからフェードアウトする
        Resume();
        StartFade(0f, fadeSeconds, stopAfter: true);
    }

    public void Pause()
    {
        if (!InUse || IsPaused) return;
        IsPaused = true;
        Source.Pause();
    }

    public void Resume()
    {
        if (!InUse || !IsPaused) return;
        IsPaused = false;
        Source.UnPause();
    }

    public void SetPitch(float pitch)
    {
        if (!InUse) return;
        Source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
    }

    public void SetVolumeScale(float volumeScale)
    {
        if (!InUse) return;
        _volumeScale = Mathf.Max(0f, volumeScale);
    }

    /// <summary>
    /// 毎フレーム SoundManager から呼ばれる。フェードを進めて、音量を AudioSource に反映する。
    /// channelVolume は「Master × カテゴリ(Bgm/Se)」の音量。
    /// </summary>
    public void Tick(float deltaTime, float channelVolume)
    {
        if (!InUse) return;

        if (_fading && !IsPaused)
        {
            _fadeTime += deltaTime;
            float t = _fadeDuration <= 0f ? 1f : Mathf.Clamp01(_fadeTime / _fadeDuration);
            _fade = Mathf.Lerp(_fadeFrom, _fadeTo, t);

            if (t >= 1f)
            {
                _fading = false;
                if (_stopAfterFade)
                {
                    Release();
                    return;
                }
            }
        }

        // 曲終わりをフェードさせるループ: 最後まで鳴り終わったら、最初から流し直す
        // (一時停止中も isPlaying は false になるので、IsPaused で除外する)
        if (_loopFadeSeconds > 0f && !IsPaused && !Source.isPlaying)
        {
            Source.time = 0f;
            Source.Play();
        }

        // ループしない音が最後まで鳴り終わったら、このチャンネルを空きに戻す
        if (_loopFadeSeconds <= 0f && !IsPaused && !Source.loop && !Source.isPlaying)
        {
            Release();
            return;
        }

        Source.volume = Entry.volume * _volumeScale * _fade * LoopEndFade() * channelVolume;
    }

    // 曲の残りが _loopFadeSeconds 秒を切ったら、残りに合わせて 1 → 0 に小さくする
    private float LoopEndFade()
    {
        if (_loopFadeSeconds <= 0f) return 1f;
        float remaining = (Source.clip.length - Source.time) / Mathf.Abs(Source.pitch); // 再生速度が速いほど早く終わる
        return Mathf.Clamp01(remaining / _loopFadeSeconds);
    }

    private void StartFade(float to, float duration, bool stopAfter)
    {
        _fading = true;
        _stopAfterFade = stopAfter;
        _fadeFrom = _fade;
        _fadeTo = to;
        _fadeDuration = duration;
        _fadeTime = 0f;
    }

    private void Release()
    {
        Source.Stop();
        Source.clip = null;
        Entry = null;
        InUse = false;
        _loopFadeSeconds = 0f;
        IsPaused = false;
        _fading = false;
        _stopAfterFade = false;
        Generation++; // これで古い SoundHandle は無効になる
    }
}
