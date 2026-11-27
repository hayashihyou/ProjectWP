using System.Collections;
using UnityEngine;

/// <summary>
/// みんなでぶつけっこ固有の音を鳴らす。MG_Butsukekko の「ButsukekkoSounds」オブジェクトに付ける。
/// スタート・フィニッシュ・タイムアップは全ミニゲーム共通なので、MinigameBase が鳴らしている(ここでは鳴らさない)。
/// BGM は SceneBgm の表で、シーンに入ったときに自動で流れる。
///
/// 流れと音
///   「ヒット」の出来事       → 命中音(全員の端末で鳴る)
///   自分の点数が増えた       → 得点音(当てた本人の端末だけで鳴る。命中音とかぶらないよう、少し遅らせる)
///
/// MinigameInfo の出来事を聞くだけなので、NetworkObject は不要。
/// </summary>
public class ButsukekkoSounds : MonoBehaviour
{
    [Header("音(SoundLibrary のID。空欄 = 鳴らさない)")]
    [SerializeField] private string hitId = "Butsukekko_Hit";
    [SerializeField] private string scoreId = "Butsukekko_Score";

    [Header("タイミング")]
    [Tooltip("点数が増えてから得点音を鳴らすまでの秒数。命中音の「バンッ」が聞こえてから鳴るようにする")]
    [SerializeField] private float scoreDelay = 0.12f;

    // イベントは OnEnable で登録して、OnDisable で外す(MinigameInfo の決まり)
    private void OnEnable()
    {
        MinigameInfo.OnCustomEvent += HandleCustomEvent;
        MinigameInfo.OnPlayerValueChanged += HandlePlayerValueChanged;
    }

    private void OnDisable()
    {
        MinigameInfo.OnCustomEvent -= HandleCustomEvent;
        MinigameInfo.OnPlayerValueChanged -= HandlePlayerValueChanged;
        StopAllCoroutines();
    }

    private void HandleCustomEvent(string eventName, MinigameValue value, int slot)
    {
        if (eventName == ButsukekkoMinigame.HitEventName) { Play(hitId); }
    }

    // 自分の点数が増えたときだけ鳴らす(= 当てた本人の端末だけで鳴る)。
    // ほかの人の点数が増えたとき・当てられて減ったときは鳴らさない
    private void HandlePlayerValueChanged(int slot, float delta, float newValue)
    {
        if (delta <= 0f) { return; }
        if (!MinigameInfo.TryGetLocalPlayer(out MinigamePlayerState me) || me.SlotIndex != slot) { return; }
        StartCoroutine(PlayAfter(scoreDelay, scoreId));
    }

    private IEnumerator PlayAfter(float seconds, string id)
    {
        // 音の時間は SoundManager と同じく実時間で数える(処理が重くてゲーム時間が遅れても、ずれないように)
        yield return new WaitForSecondsRealtime(seconds);
        Play(id);
    }

    private static void Play(string id)
    {
        if (!string.IsNullOrEmpty(id)) { SoundManager.Instance.PlaySe(id); }
    }
}
