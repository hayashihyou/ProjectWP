using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// みんなでぶつけっこの「当たり判定」と「当たったときの処理」をまとめたもの。
/// MG_Butsukekko の Minigame オブジェクト(ButsukekkoMinigame と同じ所)に付ける。
///
/// ■ 当たり判定の登録
///   シーンにいるプレイヤー(PlayerSlot)を、1秒に1回探して「的(HitTarget)」として登録する。
///   的は「足元からの円柱」で、半径 = hitRadius、高さ = bodyHeight。
///   (シーンビューでプレイヤーを選ばなくても、黄色い円柱で見える。無敵中は水色)
///
/// ■ ボールが当たったときの流れ(OnServerHit を上から読めば分かる)
///   [サーバー] 1. 当てられた人を無敵にする
///              2. 当てた人の点数を足す        → 点数UI・スコア板が書き換わる / 当てた本人だけ得点音
///              3. 「ヒット」の出来事を全員へ   → 全員で命中音(ButsukekkoSounds)
///              4. 演出の命令を全員へ
///   [全員]     5. ヒットエフェクト(当てた人の色・飛んできた向き)
///              6. 当てられた人を点滅させる
///              7. 当てられたのが自分なら画面を揺らす
///
/// プレイヤーのプレハブには何も付けない(プレイヤーは他の担当なので、外から見て判定・点滅させる)。
/// </summary>
public class ButsukekkoHitManager : NetworkBehaviour
{
    [Header("当たり判定(1人分の大きさ)")]
    [Tooltip("体の中心からこの距離(横方向)にボールが入ったら当たり")]
    [SerializeField] private float hitRadius = 0.5f;

    [Tooltip("足元からこの高さまでを体とみなす")]
    [SerializeField] private float bodyHeight = 1.4f;

    [Header("無敵")]
    [Tooltip("当てられた後、もう一度当てられない秒数。この間は点滅する")]
    [SerializeField] private float invincibleSeconds = 1.5f;

    [Tooltip("点滅の速さ(1秒に何回 消える→出る を繰り返すか)")]
    [SerializeField] private float blinkPerSecond = 10f;

    [Header("ヒットエフェクト")]
    [Tooltip("Prefabs/Effects/BallHitShockwave")]
    [SerializeField] private GameObject hitEffectPrefab;

    [Tooltip("1P〜4Pの色(当てた人の色でエフェクトを出す)。1Pは白ではなくピンク")]
    [SerializeField] private Color[] playerColors =
    {
        new Color(1f, 0.45f, 0.7f),   // 1P: ピンク
        new Color(0.65f, 0.45f, 1f),  // 2P: 紫(キャラの色より少し濃くして、エフェクトで薄く見えないようにする)
        new Color(0.4f, 0.9f, 1f),    // 3P: 水色
        new Color(0.6f, 1f, 0.2f),    // 4P: 黄緑
    };

    [Tooltip("白っぽさを残すパーツ(光・内側の輪・破片)に、色を混ぜる割合")]
    [Range(0f, 1f)]
    [SerializeField] private float lightTintAmount = 0.35f;

    [Tooltip("エフェクトを消すまでの秒数")]
    [SerializeField] private float effectLifetime = 1f;

    [Header("画面揺れ(自分が当てられたときだけ)")]
    [SerializeField] private float shakeSeconds = 0.18f;
    [SerializeField] private float shakeStrength = 0.15f;


    /// <summary>的1人分。どのプレイヤーか・無敵がいつまでか・点滅がいつまでか</summary>
    private class HitTarget
    {
        public PlayerSlot Player;
        public Renderer[] Renderers;     // 点滅させる見た目
        public float InvincibleUntil;    // サーバーが使う(この時間までは当たらない)
        public float BlinkUntil;         // 全員が使う(この時間まで点滅する)

        public int Slot => Player != null ? Player.SlotIndex : -1;
        public Vector3 Feet => Player.transform.position;
    }

    // 登録されている的(枠番号 → 的)
    private readonly Dictionary<int, HitTarget> targets = new Dictionary<int, HitTarget>();
    private float targetsRefreshedTime = -999f;

    private ButsukekkoMinigame minigame;

    // 画面揺れ
    private float shakeTimer;
    private Vector3 lastShakeOffset;


    private void Awake()
    {
        minigame = GetComponent<ButsukekkoMinigame>();
    }

    private void OnEnable()
    {
        // ラウンドが始まったら、無敵と点滅を全部解除する
        MinigameInfo.OnRoundStarted += HandleRoundStarted;
    }

    private void OnDisable()
    {
        MinigameInfo.OnRoundStarted -= HandleRoundStarted;
        StopAllBlinks();
    }

    private void Update()
    {
        RefreshTargets(false);
        UpdateBlinks();
    }

    private void LateUpdate()
    {
        UpdateShake();
    }


    // ==================== 当たり判定(サーバー) ====================

    /// <summary>
    /// ボールの位置で、誰かに当たったか調べる(サーバーのみ。ButsukekkoBall から呼ばれる)。
    /// 投げた本人と、無敵中の人には当たらない。当たったら true と、その人の枠番号を返す
    /// </summary>
    public bool ServerFindHit(Vector3 ballPosition, int throwerSlot, out int hitSlot)
    {
        hitSlot = -1;
        if (!IsServer) { return false; }

        foreach (HitTarget target in targets.Values)
        {
            if (target.Player == null) { continue; }
            if (target.Slot < 0 || target.Slot == throwerSlot) { continue; }
            if (Time.time < target.InvincibleUntil) { continue; } // 無敵中

            if (IsInsideBody(target.Feet, ballPosition))
            {
                hitSlot = target.Slot;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ボールが当たったときの処理(サーバーのみ。ButsukekkoBall から呼ばれる)
    /// </summary>
    /// <param name="throwerSlot">当てた人の枠番号</param>
    /// <param name="hitSlot">当てられた人の枠番号</param>
    /// <param name="hitPoint">当たった位置(ボールの位置)</param>
    /// <param name="ballDirection">ボールが飛んできた向き</param>
    public void OnServerHit(int throwerSlot, int hitSlot, Vector3 hitPoint, Vector3 ballDirection)
    {
        if (!IsServer) { return; }
        if (!targets.TryGetValue(hitSlot, out HitTarget target)) { return; }

        // 1. 当てられた人を無敵にする(この間はボールがすり抜ける)
        target.InvincibleUntil = Time.time + invincibleSeconds;

        // 2. 当てた人の点数を足す
        // 3. 「ヒット」の出来事を全員へ送る
        //    (どちらも ButsukekkoMinigame.ServerReportHit の中でやっている)
        if (minigame != null) { minigame.ServerReportHit(throwerSlot, hitSlot); }

        // 4. 演出の命令を全員へ送る(5〜7 は PlayHitRpc の中)
        PlayHitRpc(throwerSlot, hitSlot, hitPoint, ballDirection);
    }


    // ==================== 当たったときの演出(全員) ====================

    [Rpc(SendTo.Everyone)]
    private void PlayHitRpc(int throwerSlot, int hitSlot, Vector3 hitPoint, Vector3 ballDirection)
    {
        RefreshTargets(true);
        targets.TryGetValue(hitSlot, out HitTarget target);

        // 5. ヒットエフェクト(当てた人の色・飛んできた向き)
        float groundY = target != null && target.Player != null ? target.Feet.y : hitPoint.y;
        PlayHitEffect(hitPoint, groundY, ballDirection, GetPlayerColor(throwerSlot));

        // 6. 当てられた人を点滅させる(無敵の時間だけ)
        if (target != null) { target.BlinkUntil = Time.time + invincibleSeconds; }

        // 7. 当てられたのが自分なら、画面を揺らす
        if (MinigameInfo.TryGetLocalPlayer(out MinigamePlayerState me) && me.SlotIndex == hitSlot)
        {
            shakeTimer = shakeSeconds;
        }
    }

    // 床の輪は足元の高さに、光と破片は当たった高さに出す。
    // エフェクトの前(+Z)を、ボールが飛んできた向きにそろえる
    private void PlayHitEffect(Vector3 hitPoint, float groundY, Vector3 ballDirection, Color color)
    {
        if (hitEffectPrefab == null) { return; }

        Vector3 flat = new Vector3(ballDirection.x, 0f, ballDirection.z);
        Quaternion rotation = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) : Quaternion.identity;

        Vector3 rootPosition = new Vector3(hitPoint.x, groundY, hitPoint.z);
        GameObject effect = Instantiate(hitEffectPrefab, rootPosition, rotation);

        float height = hitPoint.y - groundY;
        SetLocalHeight(effect.transform, "Flash", height);
        SetLocalHeight(effect.transform, "Debris", height);

        // 当てた人の色に染める(光・内側の輪・破片は、白っぽさを残す)
        Color lightColor = Color.Lerp(Color.white, color, lightTintAmount);
        foreach (ParticleSystem particle in effect.GetComponentsInChildren<ParticleSystem>())
        {
            bool light = particle.name == "Flash" || particle.name == "GroundRingInner" || particle.name == "Debris";
            ParticleSystem.MainModule main = particle.main;
            float alpha = main.startColor.color.a;
            Color c = light ? lightColor : color;
            main.startColor = new Color(c.r, c.g, c.b, alpha);
            particle.Play(false);
        }

        Destroy(effect, effectLifetime);
    }

    /// <summary>その枠の人の色(エフェクトや「+1」の文字に使う)。1Pはピンク</summary>
    public Color GetPlayerColor(int slot)
    {
        return slot >= 0 && slot < playerColors.Length ? playerColors[slot] : Color.white;
    }


    // ==================== 点滅 ====================

    // 点滅中の人の見た目を、消す・出すを繰り返す。
    // Renderer.enabled ではなく forceRenderingOff を使う
    // (enabled は PlayerCharacterVisual がシーンごとの表示切り替えに使っているので、ぶつからないように)
    private void UpdateBlinks()
    {
        foreach (HitTarget target in targets.Values)
        {
            bool blinking = Time.time < target.BlinkUntil;
            bool hidden = blinking && Mathf.FloorToInt(Time.time * blinkPerSecond * 2f) % 2 == 0;
            SetHidden(target, hidden);
        }
    }

    private void StopAllBlinks()
    {
        foreach (HitTarget target in targets.Values)
        {
            target.BlinkUntil = 0f;
            SetHidden(target, false);
        }
    }

    private static void SetHidden(HitTarget target, bool hidden)
    {
        if (target.Renderers == null) { return; }
        foreach (Renderer r in target.Renderers)
        {
            if (r != null) { r.forceRenderingOff = hidden; }
        }
    }

    private void HandleRoundStarted(int round)
    {
        foreach (HitTarget target in targets.Values) { target.InvincibleUntil = 0f; }
        StopAllBlinks();
    }


    // ==================== 画面揺れ ====================

    private void UpdateShake()
    {
        Camera cam = Camera.main;
        if (cam == null) { return; }

        // 前のフレームで足した揺れを戻してから、新しい揺れを足す(カメラを動かす他の処理とぶつからないように)
        cam.transform.position -= lastShakeOffset;
        lastShakeOffset = Vector3.zero;

        if (shakeTimer <= 0f) { return; }

        shakeTimer -= Time.deltaTime;
        float strength = shakeStrength * Mathf.Clamp01(shakeTimer / shakeSeconds);
        lastShakeOffset = cam.transform.right * Random.Range(-1f, 1f) * strength
                        + cam.transform.up * Random.Range(-1f, 1f) * strength;
        cam.transform.position += lastShakeOffset;
    }


    // ==================== 的の登録 ====================

    // シーンにいるプレイヤーを探して、的に登録し直す(1秒に1回まで。force で今すぐ)
    private void RefreshTargets(bool force)
    {
        if (!force && Time.unscaledTime - targetsRefreshedTime < 1f) { return; }
        targetsRefreshedTime = Time.unscaledTime;

        // いなくなった人・枠番号が変わった人を外す
        List<int> removeSlots = new List<int>();
        foreach (KeyValuePair<int, HitTarget> pair in targets)
        {
            if (pair.Value.Player == null || pair.Value.Slot != pair.Key) { removeSlots.Add(pair.Key); }
        }
        foreach (int slot in removeSlots) { targets.Remove(slot); }

        // 新しく来た人を登録する
        foreach (PlayerSlot player in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            int slot = player.SlotIndex;
            if (slot < 0 || targets.ContainsKey(slot)) { continue; }
            Register(player);
        }
    }

    private void Register(PlayerSlot player)
    {
        targets[player.SlotIndex] = new HitTarget
        {
            Player = player,
            Renderers = player.GetComponentsInChildren<Renderer>(true),
        };
    }

    // 足元から見て、点が体(円柱)の中に入っているか
    private bool IsInsideBody(Vector3 feet, Vector3 point)
    {
        float height = point.y - feet.y;
        if (height < -0.1f || height > bodyHeight) { return false; }

        Vector2 flat = new Vector2(point.x - feet.x, point.z - feet.z);
        return flat.sqrMagnitude <= hitRadius * hitRadius;
    }

    private static void SetLocalHeight(Transform root, string childName, float height)
    {
        Transform child = root.Find(childName);
        if (child == null) { return; }
        Vector3 local = child.localPosition;
        local.y = height;
        child.localPosition = local;
    }

    // シーンビューで当たり判定の円柱を見えるようにする(黄色 = 当たる、水色 = 無敵中)
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) { return; }

        foreach (HitTarget target in targets.Values)
        {
            if (target.Player == null) { continue; }
            Gizmos.color = Time.time < target.BlinkUntil ? Color.cyan : Color.yellow;
            Vector3 feet = target.Feet;
            Gizmos.DrawWireSphere(feet, hitRadius);
            Gizmos.DrawWireSphere(feet + Vector3.up * bodyHeight, hitRadius);
            Gizmos.DrawLine(feet, feet + Vector3.up * bodyHeight);
        }
    }
}
