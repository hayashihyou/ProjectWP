/// <summary>
/// 音の種類。SoundLibrary の各エントリに設定し、
/// 「どの音量スライダーの影響を受けるか」「BGMとして鳴らすかSEとして鳴らすか」を決める。
/// </summary>
public enum SoundCategory
{
    Bgm = 0,
    Se = 1,
}

/// <summary>
/// 音量設定の種類。SoundManager.SetVolume / GetVolume で使う。
/// 最終的な音量は「Master × (Bgm または Se) × エントリごとの音量」になる。
/// </summary>
public enum VolumeType
{
    Master = 0,
    Bgm = 1,
    Se = 2,
}
