using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ミニゲームのイントロ演出で、1P から最後の人まで横に流れるカメラ
/// </summary>
public class MinigameIntroCamera : MonoBehaviour
{
    [Header("映し方")]
    [Tooltip("プレイヤーからどれだけ離れて映すか(大きいほど引きの画になる)")]
    [SerializeField] private float shotDistance = 4.0f;

    [Tooltip("プレイヤーの足元から、どの高さを画面の中心にするか")]
    [SerializeField] private float lookHeight = 1.0f;

    [Tooltip("1P よりどれだけ左から流れ始めるか")]
    [SerializeField] private float leadInDistance = 1.5f;

    [Header("モニター")]
    [Tooltip("ステージ後ろの大きなモニター(本体のオブジェクトを入れる)。空にするとモニターは映さず、今まで通り定点に戻る")]
    [SerializeField] private Transform monitor;

    [Tooltip("モニターからどれだけ離れて映すか(大きいほどモニターの周りまで映る)")]
    [SerializeField] private float monitorShotDistance = 6.0f;

    [Tooltip("最後の人からモニターへ移る途中で、どれだけ上にふくらませるか(カメラがプレイヤーや机にめり込むときは大きくする)")]
    [SerializeField] private float monitorArcHeight = 1.0f;

    [Header("時間(秒)")]
    [Tooltip("流れ始めの位置から最後の人まで横に流れる時間(この間は一定の速さ。短いほど速い)")]
    [SerializeField] private float panSeconds = 2.4f;

    [Tooltip("最後の人を過ぎてから、モニターを映す位置に着くまでの時間")]
    [SerializeField] private float toMonitorSeconds = 1.2f;

    [Tooltip("モニターを映したまま止まっている時間")]
    [SerializeField] private float monitorHoldSeconds = 1.0f;

    [Tooltip("定点位置に戻るまでの時間。長いほど戻る動きがゆっくり見える")]
    [SerializeField] private float returnSeconds = 2.0f;

    // 定点位置(シーンに置いたときのカメラの位置・向き)
    private Vector3 fixedPosition;
    private Quaternion fixedRotation;

    private Coroutine introRoutine;

    // シーンを開いてから Intro フェーズが来るまでの間、流れ始めの位置で待っているか
    private bool waitingForIntro;


    /// <summary>演出全体の秒数。サーバーの Intro Seconds はこれより 0.5秒ほど長くする</summary>
    public float TotalSeconds => monitor != null
        ? panSeconds + toMonitorSeconds + monitorHoldSeconds + returnSeconds
        : panSeconds + returnSeconds;


    private void Awake()
    {
        fixedPosition = transform.position;
        fixedRotation = transform.rotation;

        // シーンを開いた最初のフレームから、定点ではなく流れ始めの位置を映しておく
        waitingForIntro = true;
        TryMoveToPanStart();
    }

    private void OnEnable()
    {
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
    }

    private void LateUpdate()
    {
        // Intro を待っている間は、流れ始めの位置にい続ける
        // (プレイヤーが後から出てきた場合も、出てきたフレームで正しい位置にそろう)
        if (waitingForIntro)
        {
            TryMoveToPanStart();
        }
    }


    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        switch (phase)
        {
            case MinigamePhase.Intro:
                waitingForIntro = false;
                StopIntro();
                introRoutine = StartCoroutine(PlayIntro());
                break;

            case MinigamePhase.RoundIntro:
                // Intro を受け取れないまま次に進んだ(途中参加など)ときは、待つのをやめて定点に戻す
                if (waitingForIntro)
                {
                    ReturnToFixed();
                }
                // イントロ演出中なら、ここでは止めない(最後まで戻る動きを見せる)
                break;

            default:
                if (waitingForIntro)
                {
                    ReturnToFixed();
                }
                // カウントダウン以降になってもまだ動いていたら、操作に影響しないよう定点位置にそろえる
                else if (introRoutine != null)
                {
                    Debug.LogWarning($"[IntroCamera] カウントダウンまでに演出が終わりませんでした。Minigame の Intro Seconds を {TotalSeconds + 0.5f:0.0} 秒以上にしてください");
                    ReturnToFixed();
                }
                break;
        }
    }

    private void StopIntro()
    {
        if (introRoutine == null) return;

        StopCoroutine(introRoutine);
        introRoutine = null;
    }

    // 演出を止めて、すぐに定点位置に戻す
    private void ReturnToFixed()
    {
        waitingForIntro = false;
        StopIntro();
        transform.SetPositionAndRotation(fixedPosition, fixedRotation);
    }

    // プレイヤーがいれば、流れ始めの位置にカメラを置く
    private void TryMoveToPanStart()
    {
        List<Transform> players = GetPlayersInSlotOrder();
        if (players.Count == 0) return;

        CalculatePanPath(players, out Vector3 panStart, out _);
        transform.SetPositionAndRotation(panStart, fixedRotation);
    }


    private IEnumerator PlayIntro()
    {
        List<Transform> players = GetPlayersInSlotOrder();
        Debug.Log($"[IntroCamera] 映すプレイヤー : {players.Count}人");

        if (players.Count == 0)
        {
            ReturnToFixed();
            yield break;
        }

        // 向きは定点カメラのまま変えない(位置だけを動かす = ズームや首振りが起きない)
        transform.rotation = fixedRotation;

        CalculatePanPath(players, out Vector3 panStart, out Vector3 panEnd);

        // 1. 流れ始めの位置 → 最後の人を一定の速さで横に流れる
        for (float time = 0f; time < panSeconds; time += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(panStart, panEnd, time / panSeconds);
            yield return null;
        }

        // 横に流れていた速さ(次の動きの出だしに引き継いで、つなぎ目で止まらないようにする)
        Vector3 panVelocity = (panEnd - panStart) / Mathf.Max(panSeconds, 0.01f);

        if (monitor != null)
        {
            Vector3 monitorShot = GetMonitorShotPosition();

            // 2. 最後の人 → モニター(3次ベジェ曲線)
            //    出だしは横の速さを引き継ぎ、上にふくらみながら回り込んで、モニターの前でふわっと止まる
            Vector3 control1 = panEnd + panVelocity * (toMonitorSeconds / 3f) + Vector3.up * monitorArcHeight;
            Vector3 control2 = monitorShot;
            yield return MoveAlongBezier(panEnd, control1, control2, monitorShot, toMonitorSeconds);

            // 3. モニターを映したまま少し止まる
            transform.position = monitorShot;
            yield return new WaitForSeconds(monitorHoldSeconds);

            // 4. モニター → 定点へ引いて戻る(ゆっくり動き出して、ゆっくり止まる)
            yield return MoveAlongBezier(monitorShot, monitorShot, fixedPosition, fixedPosition, returnSeconds);
        }
        else
        {
            // モニター未設定のときは今まで通り:余韻 → 定点へ戻る
            Vector3 control1 = panEnd + panVelocity * (returnSeconds / 3f);
            Vector3 control2 = fixedPosition;
            yield return MoveAlongBezier(panEnd, control1, control2, fixedPosition, returnSeconds);
        }

        transform.SetPositionAndRotation(fixedPosition, fixedRotation);
        introRoutine = null;
    }

    // 横に流れる区間の始まりと終わりを求める
    private void CalculatePanPath(List<Transform> players, out Vector3 panStart, out Vector3 panEnd)
    {
        // 流れる向き = 定点カメラから見た「右」を、水平にしたもの(上下・前後には動かない)
        Vector3 panDirection = fixedRotation * Vector3.right;
        panDirection.y = 0f;
        panDirection.Normalize();

        // 1P を映す位置を基準にする。高さと奥行きはここで固定し、横にだけ動かす
        Vector3 firstShot = GetShotPosition(players[0]);
        Vector3 lastShot = GetShotPosition(players[players.Count - 1]);

        // 最後の人が、1P から横にどれだけ離れているか(高さ・奥行きのずれは無視する)
        float panLength = Vector3.Dot(lastShot - firstShot, panDirection);

        // 1P より少し左から始め、最後の人の真横まで流れる
        panStart = firstShot - panDirection * leadInDistance;
        panEnd = firstShot + panDirection * panLength;
    }

    // 3次ベジェ曲線に沿って、指定の秒数でカメラを動かす
    private IEnumerator MoveAlongBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float seconds)
    {
        for (float time = 0f; time < seconds; time += Time.deltaTime)
        {
            transform.position = EvaluateBezier(p0, p1, p2, p3, time / seconds);
            yield return null;
        }
        transform.position = p3;
    }

    // プレイヤーの顔のあたりが画面の中心にくる、カメラの位置(定点カメラと同じ向きで、shotDistance だけ手前)
    private Vector3 GetShotPosition(Transform player)
    {
        Vector3 look = player.position + Vector3.up * lookHeight;
        return look - (fixedRotation * Vector3.forward) * shotDistance;
    }

    // モニターの中心が画面の中心にくる、カメラの位置(定点カメラと同じ向きで、monitorShotDistance だけ手前)
    private Vector3 GetMonitorShotPosition()
    {
        return GetMonitorCenter() - (fixedRotation * Vector3.forward) * monitorShotDistance;
    }

    // モニターの見た目の中心(子も含めた Renderer の範囲の中心。Renderer が無ければ Transform の位置)
    private Vector3 GetMonitorCenter()
    {
        Renderer[] renderers = monitor.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return monitor.position;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds.center;
    }

    // 3次ベジェ曲線上の位置(t = 0 で p0、t = 1 で p3)
    private Vector3 EvaluateBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0
             + 3f * u * u * t * p1
             + 3f * u * t * t * p2
             + t * t * t * p3;
    }

    // 参加しているプレイヤーを、枠番号(1P, 2P, …)の順に並べて返す
    private List<Transform> GetPlayersInSlotOrder()
    {
        List<PlayerSlot> slots = new List<PlayerSlot>(
            FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None));

        slots.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

        List<Transform> result = new List<Transform>();
        foreach (PlayerSlot slot in slots)
        {
            if (slot.SlotIndex < 0) continue; // 枠が決まっていない
            result.Add(slot.transform);
        }
        return result;
    }
}
