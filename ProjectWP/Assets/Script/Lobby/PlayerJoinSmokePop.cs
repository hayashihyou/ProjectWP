using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 参加演出「煙ポン+登場」。
/// 立ち位置に白い煙をポンと出し、キャラクターを小さい状態からポヨンと大きくして登場させる。
///
/// キャラクターの大きさについて:
///   キャラクターのルート(一番上のオブジェクト)の大きさは NetworkTransform で同期されているので、
///   そこを変えると他の人の画面とズレたり、同期で上書きされたりする。
///   そこで、ルートではなく子の「骨(Hips)」だけを、自分の画面の中だけで拡大縮小する。
///   (Hipsの位置も一緒に縮めることで、足元を中心に大きくなるように見せている)
///   キャラクター側のプレハブやスクリプトは変更しない。
/// </summary>
public class PlayerJoinSmokePop : PlayerJoinPresenterBase
{
    [Tooltip("立ち位置に出す煙のエフェクト")]
    [SerializeField] private PlayerJoinEffect smokePrefab;

    [Tooltip("立ち位置(スポーン位置)からのずらし量。足元の台の上に合わせるのに使う。")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Tooltip("拡大縮小する骨の名前(キャラクターの直下にあるもの)")]
    [SerializeField] private string scaleBoneName = "Hips";

    [Tooltip("煙が出てから、キャラクターが大きくなり始めるまでの秒数")]
    [SerializeField] private float popDelay = 0.12f;

    [Tooltip("キャラクターの大きさの変化(横軸=経過秒数, 縦軸=元の大きさに対する倍率)。1より上に行くとポヨンと弾む。")]
    [SerializeField]
    private AnimationCurve popScaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 8f),
        new Keyframe(0.16f, 1.25f),
        new Keyframe(0.3f, 0.9f),
        new Keyframe(0.42f, 1.05f),
        new Keyframe(0.52f, 1f));

    public override void Present(int slotIndex, Color color, PlayerSlot slot)
    {
        if (PlayerSlotManager.Instance != null && smokePrefab != null)
        {
            Vector3 position = PlayerSlotManager.Instance.GetSpawnPosition(slotIndex) + positionOffset;
            PlayerJoinEffect smoke = Instantiate(smokePrefab, position, Quaternion.identity);
            smoke.Play(color);
        }

        if (slot != null)
        {
            Transform bone = slot.transform.Find(scaleBoneName);
            if (bone != null)
            {
                StartPop(bone);
            }
        }
    }

    /// <summary>ポヨンと大きくしている最中の骨1本分</summary>
    private class ActivePop
    {
        public Transform Bone;
        public Vector3 BaseScale;     // 元の大きさ(最後にここへ戻す)
        public Vector3 BasePosition;  // 元の位置(最後にここへ戻す)
        public float Elapsed;
    }

    private readonly List<ActivePop> activePops = new List<ActivePop>();

    private void StartPop(Transform bone)
    {
        // 同じキャラクターで再生中なら、元の大きさを取り違えないように、その記録を使い回して最初からやり直す
        ActivePop pop = activePops.Find(p => p.Bone == bone);
        if (pop == null)
        {
            pop = new ActivePop { Bone = bone, BaseScale = bone.localScale, BasePosition = bone.localPosition };
            activePops.Add(pop);
        }
        pop.Elapsed = 0f;

        // すぐ0にする(元の大きさで一瞬見えてしまうのを防ぐ)
        ApplyScale(pop, 0f);
    }

    // Animatorが骨を動かした「後」・描画の「前」に上書きしたいので、LateUpdateで書き換える
    private void LateUpdate()
    {
        float endTime = popDelay + popScaleCurve.keys[popScaleCurve.length - 1].time;

        for (int i = activePops.Count - 1; i >= 0; i--)
        {
            ActivePop pop = activePops[i];

            // 途中でキャラクターが消えた(退出など)
            if (pop.Bone == null)
            {
                activePops.RemoveAt(i);
                continue;
            }

            pop.Elapsed += Time.deltaTime;
            if (pop.Elapsed >= endTime)
            {
                // 終わったら元の大きさ・位置に戻す
                pop.Bone.localScale = pop.BaseScale;
                pop.Bone.localPosition = pop.BasePosition;
                activePops.RemoveAt(i);
                continue;
            }

            float scale = pop.Elapsed < popDelay ? 0f : popScaleCurve.Evaluate(pop.Elapsed - popDelay);
            ApplyScale(pop, scale);
        }
    }

    // このコンポーネントが無効にされた/消えたときも、大きさを必ず元に戻す
    private void OnDisable()
    {
        foreach (ActivePop pop in activePops)
        {
            if (pop.Bone == null) continue;
            pop.Bone.localScale = pop.BaseScale;
            pop.Bone.localPosition = pop.BasePosition;
        }
        activePops.Clear();
    }

    private static void ApplyScale(ActivePop pop, float scale)
    {
        // 0ちょうどだと計算が不安定になることがあるので、ごく小さい値にしておく
        float safeScale = Mathf.Max(scale, 0.001f);
        pop.Bone.localScale = pop.BaseScale * safeScale;
        pop.Bone.localPosition = pop.BasePosition * safeScale;
    }
}
