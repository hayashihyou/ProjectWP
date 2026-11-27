using UnityEngine;
using Unity.Netcode;
using UnityEngine.Serialization;
using TMPro;

/// <summary>
/// 自分が操作するキャラクターの頭の上に「あなた ↓」を出す(各PCが自分の画面にだけ出す)。
/// シーンに1つ置くだけで、自分のキャラクターを探して頭の上についていく。
/// プレイヤーのプレハブ・スクリプトには何も足さなくてよい。
///
/// 色は自分の枠(1P〜4P)の色。1Pは枠の色が白(=元のピンクのまま)なので、文字はピンクにする。
/// ミニゲームのシーンでは、準備確認のボタン(「準備中」)が出た瞬間に消す。
/// 準備確認が無いミニゲームでは、「スタート！」の合図(カウントダウン0)で消す。
/// </summary>
public class LocalPlayerMarker : MonoBehaviour
{
    private const string TextWithArrow = "あなた\n↓";
    private const string TextWithoutArrow = "あなた";

    [Header("見た目")]
    [Tooltip("「あなた ↓」の文字(子の TextMeshPro)")]
    [SerializeField] private TMP_Text label;

    [Tooltip("オン=「あなた」の下に「↓」を出す。オフ=「あなた」だけ")]
    [SerializeField] private bool showArrow = false;

    [Tooltip("キャラクターの足元から、頭のてっぺんまでの高さ(m)。キャラクターの大きさ(Scale)に合わせて自動で伸び縮みする")]
    [SerializeField] private float headTopHeight = 1.75f;

    [Tooltip("頭のてっぺんから、文字の下端までのすき間(m)。画面の上方向に空ける")]
    [SerializeField] private float gapAboveHead = 0.25f;

    [Tooltip("上下にふわふわ動く幅(m)。0で動かない")]
    [SerializeField] private float bobAmount = 0.05f;

    [Tooltip("ふわふわの速さ(1秒に何往復か)。速いと落ち着かないので、ゆっくりめにしている")]
    [SerializeField] private float bobSpeed = 0.6f;

    [Tooltip("1Pの文字の色(1Pは枠の色が白なので、代わりにこの色を使う)")]
    [SerializeField] private Color player1Color = new Color(1f, 0.45f, 0.7f);

    [Header("消すタイミング")]
    [Tooltip("オン=ミニゲーム用。準備確認のボタン(「準備中」)が出たら消す(準備確認が無いミニゲームは「スタート！」の合図で消す)。\nオフ=ずっと出す(待機所用)")]
    [FormerlySerializedAs("hideOnStartSignal")]
    [SerializeField] private bool hideInMinigame = true;

    // ミニゲームが始まるので消したか(一度消したら、このシーンではもう出さない)
    private bool hiddenForGame;

    // 見つけた自分のキャラクター(毎フレーム探さないように覚えておく)
    private PlayerSlot localSlot;

    private void Awake()
    {
        ApplyText();
    }

    // インスペクターで「↓を出す」を切り替えたら、その場で文字を変える
    private void OnValidate()
    {
        ApplyText();
    }

    private void ApplyText()
    {
        if (label != null) label.text = showArrow ? TextWithArrow : TextWithoutArrow;
    }

    private void OnEnable()
    {
        // 「スタート！」の合図はカウントダウンの 0 で来る
        MinigameInfo.OnCountdown += HandleCountdown;
    }

    private void OnDisable()
    {
        MinigameInfo.OnCountdown -= HandleCountdown;
    }

    // 準備確認が無いミニゲーム用:「スタート！」の合図で消す
    private void HandleCountdown(int secondsLeft)
    {
        if (!hideInMinigame) return;
        if (secondsLeft != 0) return; // 0 = スタートの合図
        hiddenForGame = true;
    }

    // キャラクターが動いた後に位置を合わせたいので、Update ではなく LateUpdate で動かす
    private void LateUpdate()
    {
        // 準備確認のボタン(「準備中」)が出たら消す。
        // CanSendReady は「準備確認中で、ボタンを出す時刻を過ぎた」= ボタンが画面に出ている間だけ true になる
        if (hideInMinigame && MinigameInfo.CanSendReady) hiddenForGame = true;

        PlayerSlot slot = FindLocalPlayerSlot();

        // 自分のキャラがまだいない・枠が決まっていない・ミニゲームが始まった のときは出さない
        bool show = slot != null && slot.SlotIndex >= 0 && !hiddenForGame;
        label.enabled = show;
        if (!show) return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        // 頭のてっぺんの位置。
        // 左右・前後は頭の骨に合わせる(待機アニメーションで頭が揺れても、ちゃんと頭の真上に来るように)。
        // 頭の骨が見つからないキャラクターなら、足元の真上を使う。
        Vector3 headTop = slot.transform.position + Vector3.up * (headTopHeight * slot.transform.lossyScale.y);
        Transform headBone = FindHeadBone(slot);
        if (headBone != null)
        {
            headTop.x = headBone.position.x;
            headTop.z = headBone.position.z;
        }

        // 頭のてっぺんから「画面の上方向(カメラの上方向)」にずらす。
        // 世界の真上にずらすと、カメラが斜め上から見下ろすミニゲームでは、画面の端にいる人ほど
        // 文字が頭の真上から横にずれて見える。カメラの上方向にずらせば、画面上の左右の位置が頭とぴったり同じになる。
        float bob = Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * bobAmount;
        transform.position = headTop + mainCamera.transform.up * (gapAboveHead + bob);

        // いつもカメラの方を向く(文字が裏返ったり斜めにならないように)
        transform.rotation = mainCamera.transform.rotation;

        // 自分の枠の色にする
        label.color = GetMarkerColor(slot.SlotColor);
    }

    // この PC で操作しているキャラクターの PlayerSlot を探す(見つからなければ null)。
    // ネットワークの「プレイヤーオブジェクト」はセッション管理用(ClientSessionHandler)で、
    // キャラクターは別のオブジェクトなので、「自分が持ち主(IsOwner)の PlayerSlot」を探す。
    // 毎フレーム探すと重いので、一度見つけたら覚えておく(消えたらまた探す)。
    private PlayerSlot FindLocalPlayerSlot()
    {
        if (localSlot != null && localSlot.IsSpawned && localSlot.IsOwner) return localSlot;

        localSlot = null;
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening) return null;

        foreach (PlayerSlot slot in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            if (slot.IsSpawned && slot.IsOwner)
            {
                localSlot = slot;
                break;
            }
        }
        return localSlot;
    }

    // 自分のキャラクターの頭の骨を探す(人型のリグなら Animator から取れる)。見つからなければ null。
    // 一度探したキャラクターは覚えておき、毎フレーム探さないようにする。
    private PlayerSlot headBoneOwner;
    private Transform headBone;

    private Transform FindHeadBone(PlayerSlot slot)
    {
        if (headBoneOwner == slot) return headBone;

        headBoneOwner = slot;
        headBone = null;
        Animator animator = slot.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        }
        return headBone;
    }

    // 枠の色が白(=1P)なら、ピンクに置き換える
    private Color GetMarkerColor(Color slotColor)
    {
        bool isAlmostWhite = slotColor.r > 0.95f && slotColor.g > 0.95f && slotColor.b > 0.95f;
        return isAlmostWhite ? player1Color : slotColor;
    }
}
