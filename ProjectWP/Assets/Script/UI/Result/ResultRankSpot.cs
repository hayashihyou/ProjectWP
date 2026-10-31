using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// リザルトでプレイヤーを1人立たせる「台」。1位〜4位の4つを ResultPlayerLineup の子に置く。
///
/// ・Order で「何番目に良い人をここに立たせるか」を決める(1 = 1位の台)
/// ・このオブジェクトの位置・向きにキャラクターのモデルが立つ
/// ・「〇位」の文字(SampleScene の No1〜No4)を、キャラクターの頭の上に付いていくように動かす
///
/// 同着のとき(例: 1位が2人)は、2番目の台の文字も「1位」に書き換わる
/// </summary>
public class ResultRankSpot : MonoBehaviour
{
    [Header("識別")]
    [Tooltip("何番目に良い人を立たせる台か(1 = 1位, 2 = 2位, 3 = 3位, 4 = 4位)")]
    [Range(1, 4)]
    [SerializeField] private int order = 1;

    [Header("「〇位」の文字")]
    [Tooltip("キャラクターの頭の上に付いていかせる UI(No1〜No4 の親に空の RectTransform を作って入れる。" +
             "SlideDownFadeIn は置いた位置から動かすので、文字そのものではなく親を動かす)")]
    [SerializeField] private RectTransform labelAnchor;

    [Tooltip("順位を書き換える文字(同着のときに使う)。空なら書き換えない")]
    [SerializeField] private TMP_Text rankLabel;

    [Tooltip("順位の書き方。{0} に順位が入る")]
    [SerializeField] private string labelFormat = "{0}<size=25>位</size>";

    [Tooltip("キャラクターの足元から、どこに文字を出すか")]
    [SerializeField] private Vector3 labelWorldOffset = new Vector3(0f, 1.4f, 0f);

    [Tooltip("オン = 文字の縦の位置を画面の高さの割合で固定する(横はキャラクターに合わせる)。" +
             "オフ = Label World Offset の位置(キャラクターの頭の上・足元など)に出す")]
    [SerializeField] private bool lockLabelScreenHeight = true;

    [Tooltip("文字を出す縦の位置(0 = 画面の一番下, 1 = 一番上)。Lock Label Screen Height がオンのときだけ使う")]
    [Range(0f, 1f)]
    [SerializeField] private float labelScreenHeight01 = 0.12f;

    [Header("キャラクター")]
    [Tooltip("立たせたときに再生する Animator のステート名(例: 1位は喜ぶ、4位は落ち込む)。無ければ Idle")]
    [SerializeField] private string animationState = "Idle";

    [Header("演出")]
    [Tooltip("この台に人が立ったときに呼ぶ(例: No1 の SlideDownFadeIn.Play、TextFlashAnim.Play)")]
    [SerializeField] private UnityEvent onShow = new UnityEvent();


    /// <summary>何番目に良い人の台か(1始まり)</summary>
    public int Order => order;

    /// <summary>今立っている人(立っていなければ IsUsed が false)</summary>
    public MinigameRankEntry Entry { get; private set; }

    /// <summary>この台に人が立っているか</summary>
    public bool IsUsed { get; private set; }

    /// <summary>立っているモデル</summary>
    public GameObject Model { get; private set; }

    public string AnimationState => animationState;


    private Canvas labelCanvas;
    private Camera viewCamera;


    private void Awake()
    {
        if (labelAnchor != null)
        {
            labelCanvas = labelAnchor.GetComponentInParent<Canvas>();
            labelAnchor.gameObject.SetActive(false);
        }
    }


    /// <summary>この台に人を立たせる(ResultPlayerLineup が呼ぶ)</summary>
    public void Show(MinigameRankEntry entry, GameObject model, Camera cam)
    {
        Entry = entry;
        Model = model;
        IsUsed = true;
        viewCamera = cam;

        if (rankLabel != null)
        {
            rankLabel.text = string.Format(labelFormat, entry.Rank);
        }

        if (labelAnchor != null)
        {
            labelAnchor.gameObject.SetActive(true);
            UpdateLabelPosition();
        }

        onShow.Invoke();
    }

    /// <summary>モデルと文字を隠す</summary>
    public void Hide()
    {
        if (Model != null) Model.SetActive(false);
        if (labelAnchor != null) labelAnchor.gameObject.SetActive(false);
    }

    /// <summary>モデルを消して、何も立っていない状態に戻す</summary>
    public void Clear()
    {
        if (Model != null) Destroy(Model);
        Model = null;
        IsUsed = false;
        if (labelAnchor != null) labelAnchor.gameObject.SetActive(false);
    }


    private void LateUpdate()
    {
        if (IsUsed && labelAnchor != null && labelAnchor.gameObject.activeSelf)
        {
            UpdateLabelPosition();
        }
    }

    // キャラクターの頭の上の位置を画面上の位置に直して、文字を置く
    private void UpdateLabelPosition()
    {
        if (viewCamera == null || labelCanvas == null) return;

        Vector3 world = transform.position + transform.rotation * labelWorldOffset;
        Vector3 screen = viewCamera.WorldToScreenPoint(world);

        // 縦の位置を画面の高さの割合で固定する(横はキャラクターに合わせたまま)
        if (lockLabelScreenHeight)
        {
            screen.y = Screen.height * labelScreenHeight01;
        }

        RectTransform parent = labelAnchor.parent as RectTransform;
        Camera uiCamera = labelCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : labelCanvas.worldCamera;

        if (parent != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out Vector2 local))
        {
            labelAnchor.localPosition = local;
        }
    }


#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // シーンビューで台の位置と順番が分かるようにする
        Color[] colors = { new Color(1f, 0.8f, 0.1f), new Color(0.8f, 0.8f, 0.85f), new Color(0.8f, 0.5f, 0.2f), new Color(0.5f, 0.5f, 0.6f) };
        Gizmos.color = colors[Mathf.Clamp(order - 1, 0, colors.Length - 1)];
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, new Vector3(0.5f, 1f, 0.5f));
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.up * 0.5f + transform.forward * 0.5f);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f, $"{order}位の台");
    }
#endif
}
