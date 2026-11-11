using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// コードを書かずに、シーンの演出を「もう一度再生できる演出」として EffectTester に登録するためのコンポーネント。
/// 空のGameObjectを作って付け、インスペクターで「何をすれば再生し直せるか」を選ぶ。
///
///   ・ReactivateObject: 対象のGameObjectを一度OFFにしてONに戻す。
///                       OnEnable/Start で動き出す演出(アニメーション、ふわっと出るUIなど)はこれで再生し直せる。
///   ・InvokeEvent:      インスペクターで指定した処理(publicなメソッド)を呼ぶ。
///                       「Play()」のような再生用のメソッドを持っている演出に使う。
///
/// 注意: ReactivateObject の対象に、このコンポーネント自身のGameObjectを指定しないこと
/// (OFFにした瞬間にこのコンポーネントも止まってしまい、ONに戻せなくなるため)。
/// </summary>
public class ReplayableEffect : MonoBehaviour
{
    public enum ReplayMode
    {
        ReactivateObject,
        InvokeEvent,
    }

    [Tooltip("テスト用パネルでの見出し(例: タイトル、リザルト)")]
    [SerializeField] private string group = "未分類";

    [Tooltip("テスト用パネルのボタンに出す名前(空ならGameObjectの名前)")]
    [SerializeField] private string label = "";

    [SerializeField] private ReplayMode mode = ReplayMode.ReactivateObject;

    [Tooltip("ReactivateObject のとき: OFF→ONにするGameObject")]
    [SerializeField] private GameObject target;

    [Tooltip("InvokeEvent のとき: 呼ぶ処理")]
    [SerializeField] private UnityEvent onReplay = new UnityEvent();

    private void OnEnable()
    {
        string buttonLabel = string.IsNullOrEmpty(label) ? gameObject.name : label;
        EffectReplayRegistry.RegisterButton(this, group, buttonLabel, Replay);
    }

    private void OnDisable()
    {
        EffectReplayRegistry.Unregister(this);
    }

    /// <summary>演出をもう一度再生する</summary>
    public void Replay()
    {
        switch (mode)
        {
            case ReplayMode.ReactivateObject:
                if (target == null || target == gameObject)
                {
                    Debug.LogWarning($"[ReplayableEffect] {name}: 対象のGameObjectが未設定か、自分自身になっています。");
                    return;
                }
                StartCoroutine(ReactivateRoutine());
                break;

            case ReplayMode.InvokeEvent:
                onReplay.Invoke();
                break;
        }
    }

    private IEnumerator ReactivateRoutine()
    {
        target.SetActive(false);
        // 同じフレームでOFF→ONにすると、OnEnableで始まる処理が正しく最初からにならないことがあるので、1フレーム空ける
        yield return null;
        target.SetActive(true);
    }
}
