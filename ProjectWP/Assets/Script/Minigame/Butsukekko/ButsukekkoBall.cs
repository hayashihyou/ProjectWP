using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// みんなでぶつけっこのボール。ButsukekkoBall プレハブの一番上に付ける。
/// 拾う・投げる・当たるの判定は、すべてサーバー(ホスト)で行い、結果を全員に同期する。
/// ほかの端末は、位置(NetworkTransform)と状態(NetworkVariable)を受け取って表示するだけ。
///
/// 状態: Idle(床にあって拾える) / Held(誰かが持っている) / Flying(投げられて飛んでいる)
///
/// 当たり判定は物理の衝突ではなく「プレイヤーの足元からの距離」で行う。
/// (ホストの画面では、ほかのプレイヤーのコライダーが無効になっているため)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ButsukekkoBall : NetworkBehaviour
{
    public enum BallState : byte { Idle, Held, Flying }

    [Header("拾う")]
    [Tooltip("足元からこの距離(横方向)にボールが入ったら拾う")]
    [SerializeField] private float pickupRadius = 0.8f;

    [Tooltip("当たった直後、誰も拾えない時間(秒)。当てられた人がすぐ拾い返せないようにする")]
    [SerializeField] private float pickupCooldown = 0.5f;

    [Header("投げる")]
    [Tooltip("投げるボタンを押してから、手からボールが離れるまでの秒数。投げるアニメーションに合わせる")]
    [SerializeField] private float releaseDelay = 0.35f;

    [Tooltip("前方向の速さ")]
    [SerializeField] private float throwForwardSpeed = 12f;

    [Tooltip("上方向の速さ")]
    [SerializeField] private float throwUpSpeed = 2f;

    [Header("当たる")]
    [Tooltip("体の中心からこの距離(横方向)にボールが入ったら当たり")]
    [SerializeField] private float hitRadius = 0.5f;

    [Tooltip("足元からこの高さまでを体とみなす(拾う判定にも使う)")]
    [SerializeField] private float bodyHeight = 1.4f;

    [Tooltip("当たった後、ボールが跳ね返る速さ")]
    [SerializeField] private float hitBounceSpeed = 2f;

    [Header("場外")]
    [Tooltip("この高さより下に落ちたら、最初の位置に戻す")]
    [SerializeField] private float fallResetY = -5f;

    // 全員に同期する状態(書けるのはサーバーだけ)
    private readonly NetworkVariable<BallState> state = new NetworkVariable<BallState>(BallState.Idle);
    private readonly NetworkVariable<int> holderSlot = new NetworkVariable<int>(-1);

    // 今シーンにあるボール(どの端末でも「この人が持っているか」を調べられるようにする)
    private static readonly List<ButsukekkoBall> allBalls = new List<ButsukekkoBall>();

    private readonly List<PlayerSlot> players = new List<PlayerSlot>();
    private float playersRefreshedTime = -999f;

    private Rigidbody rb;
    private Collider ballCollider;
    private NetworkTransform networkTransform;
    private Vector3 startPosition;
    private Quaternion startRotation;

    // 持っている人の手(表示用。毎フレーム探さないように覚えておく)
    private int cachedHandSlot = -1;
    private Transform cachedHand;

    // ここから下はサーバーだけが使う
    private ButsukekkoMinigame minigame;
    private int throwerSlot = -1;
    private bool isReleasing;
    private float releaseTimer;
    private float cooldownTimer;

    public BallState State => state.Value;

    /// <summary>持っている人の枠番号。誰も持っていなければ -1</summary>
    public int HolderSlot => holderSlot.Value;

    /// <summary>この枠の人が、どれかのボールを持っているか(どの端末でも使える)</summary>
    public static bool IsHeldBy(int slot)
    {
        foreach (ButsukekkoBall ball in allBalls)
        {
            if (ball != null && ball.State == BallState.Held && ball.HolderSlot == slot) { return true; }
        }
        return false;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ballCollider = GetComponent<Collider>();
        networkTransform = GetComponent<NetworkTransform>();
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    public override void OnNetworkSpawn()
    {
        allBalls.Add(this);

        if (!IsServer)
        {
            // サーバー以外は物理で動かさない(位置は NetworkTransform で届く)
            rb.isKinematic = true;
            return;
        }

        minigame = FindFirstObjectByType<ButsukekkoMinigame>();
    }

    public override void OnNetworkDespawn()
    {
        allBalls.Remove(this);
    }

    private void Update()
    {
        // どの端末でも、プレイヤーの体とボールがぶつからないようにする
        // (ぶつけた判定は距離で行うので、物理でぶつかる必要はない)
        if (RefreshPlayers(false))
        {
            IgnorePlayerColliders();
        }
    }

    // 持っている間は、持っている人の手に付いていく(アニメーションが動いた後に合わせる)
    private void LateUpdate()
    {
        if (!IsSpawned || state.Value != BallState.Held) { return; }

        Transform hand = GetHand(holderSlot.Value);
        if (hand == null) { return; }

        transform.SetPositionAndRotation(hand.position, hand.rotation);
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer) { return; }

        if (transform.position.y < fallResetY)
        {
            ServerResetBall();
            return;
        }

        if (cooldownTimer > 0f) { cooldownTimer -= Time.fixedDeltaTime; }

        bool playing = minigame != null && minigame.Phase == MinigamePhase.Playing;

        switch (state.Value)
        {
            case BallState.Idle:
                if (playing && cooldownTimer <= 0f) { ServerTryPickUp(); }
                break;

            case BallState.Held:
                ServerUpdateHeld();
                break;

            case BallState.Flying:
                if (playing) { ServerCheckHit(); }
                break;
        }
    }

    // 床(上向きの面)に落ちたら、また拾えるようにする
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || state.Value != BallState.Flying) { return; }

        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                throwerSlot = -1;
                state.Value = BallState.Idle;
                return;
            }
        }
    }


    // ==================== サーバーだけが呼ぶ ====================

    /// <summary>投げ始める(サーバーのみ)。持っていない・すでに投げている途中なら false</summary>
    public bool ServerTryThrow()
    {
        if (!IsServer) { return false; }
        if (state.Value != BallState.Held || isReleasing) { return false; }

        isReleasing = true;
        releaseTimer = releaseDelay;
        return true;
    }

    /// <summary>最初の位置に戻して、誰でも拾える状態にする(サーバーのみ)</summary>
    public void ServerResetBall()
    {
        if (!IsServer) { return; }

        isReleasing = false;
        throwerSlot = -1;
        cooldownTimer = 0f;
        holderSlot.Value = -1;
        state.Value = BallState.Idle;

        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (networkTransform != null)
        {
            // 補間せずに一瞬で移動させる(ほかの端末でボールが滑って見えないようにする)
            networkTransform.Teleport(startPosition, startRotation, transform.localScale);
        }
        else
        {
            transform.SetPositionAndRotation(startPosition, startRotation);
        }
        rb.position = startPosition;
        rb.rotation = startRotation;
    }

    private void ServerTryPickUp()
    {
        Vector3 ballPosition = transform.position;

        foreach (PlayerSlot player in players)
        {
            if (player == null) { continue; }
            int slot = player.SlotIndex;
            if (slot < 0 || IsHeldBy(slot)) { continue; }

            if (IsInsideBody(player.transform.position, ballPosition, pickupRadius))
            {
                ServerPickUp(slot);
                return;
            }
        }
    }

    private void ServerPickUp(int slot)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        isReleasing = false;
        throwerSlot = -1;
        holderSlot.Value = slot;
        state.Value = BallState.Held;
    }

    private void ServerUpdateHeld()
    {
        PlayerSlot holder = FindPlayer(holderSlot.Value);

        // 持っている人が抜けたら、その場に落とす
        if (holder == null)
        {
            ServerDrop();
            return;
        }

        if (!isReleasing) { return; }

        releaseTimer -= Time.fixedDeltaTime;
        if (releaseTimer <= 0f)
        {
            ServerRelease(holder);
        }
    }

    // 手から離して飛ばす
    private void ServerRelease(PlayerSlot holder)
    {
        isReleasing = false;
        throwerSlot = holderSlot.Value;

        // キャラクターの向き(水平方向)に投げる
        Vector3 forward = holder.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) { forward = Vector3.forward; }
        forward.Normalize();

        holderSlot.Value = -1;
        state.Value = BallState.Flying;

        rb.position = transform.position;
        rb.isKinematic = false;
        rb.linearVelocity = forward * throwForwardSpeed + Vector3.up * throwUpSpeed;
        rb.angularVelocity = Vector3.zero;
    }

    private void ServerDrop()
    {
        isReleasing = false;
        holderSlot.Value = -1;
        state.Value = BallState.Idle;
        rb.isKinematic = false;
    }

    private void ServerCheckHit()
    {
        Vector3 ballPosition = transform.position;

        foreach (PlayerSlot player in players)
        {
            if (player == null) { continue; }
            int slot = player.SlotIndex;
            if (slot < 0 || slot == throwerSlot) { continue; }

            if (IsInsideBody(player.transform.position, ballPosition, hitRadius))
            {
                ServerHit(slot, player.transform.position);
                return;
            }
        }
    }

    private void ServerHit(int hitSlot, Vector3 hitPlayerPosition)
    {
        int thrower = throwerSlot;
        throwerSlot = -1;
        cooldownTimer = pickupCooldown;
        state.Value = BallState.Idle;

        // 当たった人から離れる方向に、軽く跳ね返す
        Vector3 away = transform.position - hitPlayerPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f) { away = -rb.linearVelocity; away.y = 0f; }
        away.Normalize();
        rb.linearVelocity = away * hitBounceSpeed + Vector3.up * hitBounceSpeed;

        if (minigame != null)
        {
            minigame.ServerReportHit(thrower, hitSlot);
        }
    }


    // ==================== 共通 ====================

    // 足元の位置から見て、点が体の中(横方向の距離と高さ)に入っているか
    private bool IsInsideBody(Vector3 feetPosition, Vector3 point, float radius)
    {
        float height = point.y - feetPosition.y;
        if (height < -0.1f || height > bodyHeight) { return false; }

        Vector2 flat = new Vector2(point.x - feetPosition.x, point.z - feetPosition.z);
        return flat.sqrMagnitude <= radius * radius;
    }

    // シーンにいるプレイヤーを探し直す(1秒に1回まで)。探し直したら true
    private bool RefreshPlayers(bool force)
    {
        if (!force && Time.unscaledTime - playersRefreshedTime < 1f) { return false; }

        playersRefreshedTime = Time.unscaledTime;
        players.Clear();
        players.AddRange(FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None));
        return true;
    }

    private PlayerSlot FindPlayer(int slot)
    {
        if (slot < 0) { return null; }

        foreach (PlayerSlot player in players)
        {
            if (player != null && player.SlotIndex == slot) { return player; }
        }

        // 見つからなければ、一度だけ探し直す
        RefreshPlayers(true);
        foreach (PlayerSlot player in players)
        {
            if (player != null && player.SlotIndex == slot) { return player; }
        }
        return null;
    }

    private Transform GetHand(int slot)
    {
        if (slot == cachedHandSlot && cachedHand != null) { return cachedHand; }

        PlayerSlot holder = FindPlayer(slot);
        if (holder == null) { return null; }

        PlayerBallAnimation ballAnimation = holder.GetComponentInChildren<PlayerBallAnimation>();
        cachedHandSlot = slot;
        cachedHand = ballAnimation != null ? ballAnimation.Hand : holder.transform;
        return cachedHand;
    }

    private void IgnorePlayerColliders()
    {
        if (ballCollider == null) { return; }

        foreach (PlayerSlot player in players)
        {
            if (player == null) { continue; }
            foreach (Collider body in player.GetComponentsInChildren<Collider>(true))
            {
                if (body != ballCollider) { Physics.IgnoreCollision(ballCollider, body, true); }
            }
        }
    }
}