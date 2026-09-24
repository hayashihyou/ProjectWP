using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// プレイヤーキャラクターの見た目(アニメーション/色)を管理するクラス。
///
/// アニメーション:
///   スポーンした瞬間に、あらかじめ用意したリストの中から1つ選んで再生する。
///   現在はIdleのみを対象にしているため常にIdleが再生される。
///   誰の画面でも同じアニメーションに見えるように、「どれを選んだか」は
///   サーバーだけが決め、NetworkVariableで全クライアントに自動配信する
///   (各クライアントがバラバラに選ぶと、見ている人によって
///    違うアニメーションに見えてしまうため)。
///
/// 色:
///   今はまだ「色を変える機能」自体は作っていないが、後で
///   「プレイヤーごとに色を変える」を足しやすいように、
///   色を適用するための ApplyColor() だけ先に用意してある。
///   呼び出す側(未実装)がどのタイミング・どのUIで色を決めるかを
///   気にせず、ここに色を渡すだけで見た目に反映できるようにする狙い。
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerCharacterVisual : NetworkBehaviour
{
    // キャラクターの見た目を「隠す」シーン名。
    // このキャラクター自体はシーンを跨いで生き続ける(SceneMigrationSynchronization)ので、
    // 03_StageSelectに移った後もデータ(所属している枠の情報など)は保持したまま、
    // 見た目(Renderer)だけをこのシーンで隠す。
    //
    // 注意: 「02_PlayerJoinの時だけ表示する」という判定にすると、ホスト自身の
    // キャラクターがまだ01_Titleにいる間(シーン移動が完了する前の一瞬)も
    // 非表示判定になってしまい、その間Rendererが無効化されることでAnimatorの
    // 更新が止まってしまう(カリング設定の影響)。そのため「隠したいシーンの時だけ隠す」
    // 判定にして、それ以外(01_Titleを含む)は常に表示したままにしている。
    private const string HiddenInSceneName = "03_StageSelect";

    [Header("スポーン時に再生するアニメーション")]
    [Tooltip("この中から1つ選んで再生する。Animator Controller側のステート名と完全に一致させること。" +
        "\n現在はIdleのみを対象にしている(要望により、参加直後の自動アニメーションはIdle固定)。")]
    [SerializeField]
    private string[] spawnReactionAnimations =
    {
        "Idle",
    };

    // 実際に選ばれたアニメーションの番号(spawnReactionAnimationsのインデックス)。
    // サーバーが決めて、Netcodeが自動で全クライアントに同期してくれる。
    private readonly NetworkVariable<int> selectedAnimationIndex = new NetworkVariable<int>(-1);

    private Animator animator;

    // 将来の色変更用に、対象のRendererをキャッシュしておく
    private Renderer[] renderers;
    private MaterialPropertyBlock materialPropertyBlock;

    private void Awake()
    {
        // 子階層(モデル側)についているAnimator/Rendererをまとめて取得しておく
        animator = GetComponentInChildren<Animator>();
        renderers = GetComponentsInChildren<Renderer>();
        materialPropertyBlock = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        UpdateVisibility(SceneManager.GetActiveScene().name);
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
    {
        UpdateVisibility(newScene.name);
    }

    /// <summary>
    /// 現在のシーンに応じて見た目(Renderer)だけを表示/非表示する。
    /// NetworkObjectやNetworkVariableのデータは消さないので、後で
    /// 「1Pのデータを使う」といった用途にそのまま使い続けられる。
    /// </summary>
    private void UpdateVisibility(string sceneName)
    {
        bool visible = sceneName != HiddenInSceneName;
        if (renderers == null) return;

        foreach (Renderer r in renderers)
        {
            if (r != null) r.enabled = visible;
        }
    }

    public override void OnNetworkSpawn()
    {
        // 値が変わるたびに再生し直す。今はスポーン直後に1回決まるだけだが、
        // 将来「途中で演出用に別のアニメーションへ変える」ような使い方をしても
        // 自動でついてこられるようにOnValueChangedで受けている。
        selectedAnimationIndex.OnValueChanged += OnSelectedAnimationChanged;

        if (IsServer)
        {
            // サーバーだけが抽選する。この結果が全クライアントに配信される。
            selectedAnimationIndex.Value = Random.Range(0, spawnReactionAnimations.Length);
        }

        // 自分がこのオブジェクトを見た時点で、既に値が決まっていれば
        // (サーバー自身や、後から接続してきた人にとってはそうなる)、
        // すぐに反映する。
        if (selectedAnimationIndex.Value >= 0)
        {
            PlaySelectedAnimation();
        }
    }

    public override void OnNetworkDespawn()
    {
        selectedAnimationIndex.OnValueChanged -= OnSelectedAnimationChanged;
    }

    private void OnSelectedAnimationChanged(int previousValue, int newValue)
    {
        PlaySelectedAnimation();
    }

    private void PlaySelectedAnimation()
    {
        int index = selectedAnimationIndex.Value;
        if (animator == null) return;

        // 抽選がまだ済んでいない/範囲外の場合の保険として、常にIdleだけは再生しておく。
        // (「ランダムで再生、無理ならIdleを常に再生」という要望への対応)
        if (index < 0 || index >= spawnReactionAnimations.Length)
        {
            animator.Play("Idle");
            return;
        }

        animator.Play(spawnReactionAnimations[index]);
    }

    /// <summary>
    /// 現在選ばれているアニメーションを再生し直す。
    /// 01_Titleでホストになった直後などシーン移動をまたいでスポーンする
    /// キャラクターは、移動の影響でAnimatorの再生状態が失われることがあるため、
    /// PlayerSlot側が「シーン移動が完了したタイミング」でこれを呼び直す。
    /// </summary>
    public void ReapplyCurrentAnimation()
    {
        PlaySelectedAnimation();
    }

    /// <summary>
    /// プレイヤーの色を変える(将来のカスタマイズ機能のための入り口)。
    /// マテリアル自体を複製せず、MaterialPropertyBlock経由でこの個体だけの
    /// 見た目の色を変える。呼び出し側は「色を決めてこれを呼ぶ」だけでよい。
    /// </summary>
    public void ApplyColor(Color color)
    {
        if (renderers == null) return;

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            r.GetPropertyBlock(materialPropertyBlock);
            // URPのLitシェーダーは_BaseColor、旧来のシェーダー互換用に_Colorも
            // 一緒に設定しておく(マテリアル側の実際のシェーダーが変わっても
            // 色が反映されなくなるのを防ぐため)。
            materialPropertyBlock.SetColor("_BaseColor", color);
            materialPropertyBlock.SetColor("_Color", color);
            r.SetPropertyBlock(materialPropertyBlock);
        }
    }
}
