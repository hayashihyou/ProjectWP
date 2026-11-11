using UnityEngine;

/// <summary>
/// 待機所(02_PlayerJoin)でプレイヤーが参加したときに出すパーティクルエフェクト本体。
/// エフェクトのプレハブ(例: Prefabs/Effects/PlayerJoinSmoke)のルートに付けて使う。
///
/// このエフェクトは「見た目だけ」のもので、ネットワーク同期はしない。
/// 参加の瞬間は全員の画面でそれぞれ検知されるので(PlayerSlot.Joined)、
/// 各PCが自分の画面に勝手に1個ずつ出して、再生が終わったら自分で消える。
/// </summary>
public class PlayerJoinEffect : MonoBehaviour
{
    [Tooltip("枠の色(1P白/2P紫…)でしっかり染めるパーティクル")]
    [SerializeField] private ParticleSystem[] tintedParticles;

    [Tooltip("枠の色を少しだけ混ぜるパーティクル(キラキラなど)。白っぽさを残して光って見えるようにする。")]
    [SerializeField] private ParticleSystem[] lightlyTintedParticles;

    [Tooltip("キラキラ側に枠の色をどれくらい混ぜるか(0=真っ白, 1=枠の色そのまま)")]
    [Range(0f, 1f)]
    [SerializeField] private float lightTintAmount = 0.35f;

    [Tooltip("再生を始めてから、このオブジェクトを消すまでの秒数。一番長いパーティクルが消え切る時間より長くする。")]
    [SerializeField] private float destroyDelay = 2f;

    /// <summary>
    /// 指定した色でエフェクトを再生する。再生が終わると自動で消える。
    /// </summary>
    public void Play(Color color)
    {
        // パーティクルごとに「最初の色」を枠の色で上書きする。
        // 透明度(a)はプレハブで決めた値を残したいので、RGBだけ差し替える。
        foreach (ParticleSystem particle in tintedParticles)
        {
            SetStartColorRgb(particle, color);
        }

        Color lightColor = Color.Lerp(Color.white, color, lightTintAmount);
        foreach (ParticleSystem particle in lightlyTintedParticles)
        {
            SetStartColorRgb(particle, lightColor);
        }

        // 子のパーティクルもまとめて最初から再生する
        ParticleSystem root = GetComponent<ParticleSystem>();
        if (root != null)
        {
            root.Play(true);
        }
        else
        {
            foreach (ParticleSystem particle in GetComponentsInChildren<ParticleSystem>())
            {
                particle.Play(false);
            }
        }

        Destroy(gameObject, destroyDelay);
    }

    private static void SetStartColorRgb(ParticleSystem particle, Color color)
    {
        if (particle == null) return;

        ParticleSystem.MainModule main = particle.main;
        float alpha = main.startColor.color.a;
        main.startColor = new Color(color.r, color.g, color.b, alpha);
    }
}
