using UnityEngine;
using Unity.Netcode;

/// <summary>
/// プレイヤーキャラクターの見た目(アニメーション/色)を管理するクラス。
///
/// アニメーション:
///   スポーンした瞬間に、あらかじめ用意したリストの中からランダムに1つ選んで再生する。
///   誰の画面でも同じアニメーションに見えるように、「どれを選んだか」は
///   サーバーだけが抽選し、NetworkVariableで全クライアントに自動配信する
///   (各クライアントがバラバラに抽選すると、見ている人によって
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
    [Header("スポーン時にランダム再生するアニメーション")]
    [Tooltip("この中からランダムに1つ選んで再生する。Animator Controller側のステート名と完全に一致させること")]
    [SerializeField]
    private string[] spawnReactionAnimations =
    {
        "Idle",
        "Happy",
        "Yelling",
        "Jumping Up",
        "Pointing Forward",
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
        if (animator == null || index < 0 || index >= spawnReactionAnimations.Length) return;

        animator.Play(spawnReactionAnimations[index]);
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
