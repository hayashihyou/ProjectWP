using UnityEngine;


/// <summary>
/// ミニゲームの「スタート」「フィニッシュ」のテクスチャを出す(各ミニゲームのシーンに1つ置く)
/// 
/// ・スタート：スタートの合図(カウントダウン 0)でStartScaleAnimを再生。計測は MinigameBase の Start Signal Lead Seconds 後に始まる
/// ・フィニッシュ：ラウンドが終わった瞬間にFinishEffectを再生
/// 
/// フィニッシュの後、暗転させずにリザルトのカメラへ切り替えたい時は
/// Minigame(MinigameBase)の「FadeAfterLastRound」「FadeBeforeResult」をオフにし
/// 「RoundEndSeconds」「GameEndSeconds」の合計をフィニッシュの演出の長さより長くする
/// </summary>
public class MinigameStartFinishUI : MonoBehaviour
{
    [Header("テクスチャ(SampleSceneのStartUI/FinishUIをコピーして使う)")]
    [Tooltip("「スタート」の演出PlayOnKey・PlayOnStartはオフにしておく")]
    [SerializeField] private StartScaleAnim startEffect;

    [Tooltip("「フィニッシュ」の演出PlayOnKey・PlayOnStartはオフ、HideOnAwakeはオンにしておく")]
    [SerializeField] private FinishEffect finishEffect;

    [Header("出すタイミング")]
    [Tooltip("オン=毎ラウンドの開始で「スタート」を出す。オフ=1ラウンド目だけ")]
    [SerializeField] private bool startEveryRound = true;

    [Tooltip("オン=毎ラウンドの終わりで「フィニッシュ」を出す。オフ=最後のラウンドだけ")]
    [SerializeField] private bool finishEveryRound = false;


    private void OnEnable()
    {
        // 「スタート！」はカウントダウンの 0(スタートの合図)で出す。
        // プレイ(計測)が始まるのは MinigameBase の Start Signal Lead Seconds だけ後になる
        MinigameInfo.OnCountdown += HandleCountdown;
        MinigameInfo.OnRoundEnded += HandleRoundEnded;
    }


    private void OnDisable()
    {
        MinigameInfo.OnCountdown -= HandleCountdown;
        MinigameInfo.OnRoundEnded -= HandleRoundEnded;
    }


    private void HandleCountdown(int secondsLeft)
    {
        if (secondsLeft != 0) return; // 0 = スタートの合図

        int round = MinigameInfo.CurrentRound;
        if(!startEveryRound && round > 1) return;
        if (startEffect == null) return;
        if (startEffect == null) return;

        // StartScaleAnim はコルーチンで動くので、非アクティブだと再生できない。
        // 前の再生で非アクティブになっていても(Deactivate On Finish など)、ここで表示し直してから再生する
        if (!startEffect.gameObject.activeSelf) startEffect.gameObject.SetActive(true);
        if (!startEffect.enabled) startEffect.enabled = true;

        if (startEffect.isActiveAndEnabled) startEffect.Play();
        else Debug.LogWarning("[MinigameStartFinishUI] スタートの親オブジェクトが非表示なので再生できません", startEffect);
    }


    private void HandleRoundEnded(int round)
    {
        bool isLastRound = round >= MinigameInfo.TotalRounds;
        if (!finishEveryRound && !isLastRound) return;

        // スタートが残っていたら消してから、フィニッシュを出す
        if(startEffect != null && startEffect.isActiveAndEnabled) startEffect.ResetState();
        if(finishEffect != null) finishEffect.Play();
    }
}
