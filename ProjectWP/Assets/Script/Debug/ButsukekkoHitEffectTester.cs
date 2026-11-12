using UnityEngine;

/// <summary>
/// 【テスト用】ぶつけっこのヒットエフェクト(BallHitShockwave)を、ボタンで再生して確かめるためのコンポーネント。
/// MG_Butsukekko の HitEffectTest に付けて使う。
///
///   ・「1Pが当てた」        : カカシに 1P の色(ピンク)でエフェクトを出す
///   ・「2P/3P/4Pに当てられた」: 1P にその人の色でエフェクトを出し、画面を揺らす(自分が当てられたとき)
///
/// 当たった位置は、相手の体の表面からランダムに選ぶ。
/// エフェクトの向きは、当てられた側の向きに合わせる。
///
/// 画面右上のパネルで、再生ボタンのほかに「輪の大きさ」をスライダーで変えられる。
/// 気に入った大きさになったら「プレハブに保存」を押すと、エフェクトのプレハブに書き込む(エディタのみ)。
/// 本番前に HitEffectTest ごとシーンから消せばよい。
/// </summary>
public class ButsukekkoHitEffectTester : MonoBehaviour
{
    private const string OuterRingName = "GroundRing";
    private const string InnerRingName = "GroundRingInner";

    [SerializeField] private GameObject effectPrefab;

    [Tooltip("自分(1P)の代わりに置いたキャラ")]
    [SerializeField] private Transform player1;

    [Tooltip("1Pが当てる相手(カカシ)")]
    [SerializeField] private Transform scarecrow;

    [Tooltip("1P〜4Pの色。1Pは白ではなくピンク")]
    [SerializeField] private Color[] playerColors =
    {
        new Color(1f, 0.45f, 0.7f),   // 1P: ピンク
        new Color(0.75f, 0.6f, 1f),   // 2P: 明るい紫
        new Color(0.4f, 0.9f, 1f),    // 3P: 水色
        new Color(0.6f, 1f, 0.2f),    // 4P: 黄緑
    };

    [Tooltip("色を少しだけ混ぜるパーツ(白っぽさを残す)の、色の混ぜ具合")]
    [Range(0f, 1f)]
    [SerializeField] private float lightTintAmount = 0.35f;

    [Tooltip("エフェクトを消すまでの秒数")]
    [SerializeField] private float destroyDelay = 1f;

    [Header("画面揺れ(自分が当てられたときだけ)")]
    [SerializeField] private bool shakeEnabled = true;
    [SerializeField] private float shakeSeconds = 0.18f;
    [SerializeField] private float shakeStrength = 0.15f;

    // スライダーで変える輪の大きさ(直径)。最初はプレハブに入っている値を読み込む
    private float outerRingSize;
    private float innerRingSize;

    // 「元に戻す」用に、最初の揺れの値を覚えておく
    private float defaultShakeStrength;
    private float defaultShakeSeconds;

    private float shakeTimer;
    private Vector3 lastShakeOffset;
    private bool collapsed;
    private string saveMessage = "";

    private void Start()
    {
        outerRingSize = ReadStartSize(OuterRingName, 8.5f);
        innerRingSize = ReadStartSize(InnerRingName, 5.2f);
        defaultShakeStrength = shakeStrength;
        defaultShakeSeconds = shakeSeconds;

        // 1Pの代わりのキャラを、1Pの色(ピンク)に染めておく
        if (player1 != null)
        {
            var block = new MaterialPropertyBlock();
            foreach (Renderer renderer in player1.GetComponentsInChildren<Renderer>())
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", playerColors[0]);
                block.SetColor("_Color", playerColors[0]);
                renderer.SetPropertyBlock(block);
            }
        }
    }

    public void HitScarecrowBy1P()
    {
        PlayHit(scarecrow, playerColors[0]);
    }

    public void Hit1PBy(int attackerSlot)
    {
        PlayHit(player1, playerColors[attackerSlot]);
        StartShake();
    }

    /// <summary>当てられた相手の体のどこかに、当てた人の色でエフェクトを出す</summary>
    private void PlayHit(Transform victim, Color color)
    {
        if (victim == null || effectPrefab == null) return;

        Vector3 hitPoint = RandomPointOnBody(victim);
        float groundY = victim.position.y;

        // 床の輪は足元の高さ、向きは当てられた側の向きに合わせる
        Vector3 rootPosition = new Vector3(hitPoint.x, groundY, hitPoint.z);
        Quaternion rotation = Quaternion.Euler(0f, victim.eulerAngles.y, 0f);
        GameObject effect = Instantiate(effectPrefab, rootPosition, rotation);

        // 光と破片は、当たった高さから出す
        float height = hitPoint.y - groundY;
        SetLocalHeight(effect.transform, "Flash", height);
        SetLocalHeight(effect.transform, "Debris", height);

        Color lightColor = Color.Lerp(Color.white, color, lightTintAmount);
        foreach (ParticleSystem particle in effect.GetComponentsInChildren<ParticleSystem>())
        {
            bool light = particle.name == "Flash" || particle.name == InnerRingName || particle.name == "Debris";
            SetStartColorRgb(particle, light ? lightColor : color);

            // スライダーで決めた輪の大きさを使う
            if (particle.name == OuterRingName) SetStartSize(particle, outerRingSize);
            if (particle.name == InnerRingName) SetStartSize(particle, innerRingSize);

            particle.Play(false);
        }

        Destroy(effect, destroyDelay);
    }

    /// <summary>相手の見た目の大きさから、体の表面のランダムな1点を選ぶ</summary>
    private static Vector3 RandomPointOnBody(Transform victim)
    {
        Renderer[] renderers = victim.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return victim.position + Vector3.up;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);

        float angle = Random.Range(0f, Mathf.PI * 2f);
        // 腕の分だけ横に広いので、狭い方の幅を体の太さとして使う
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.8f;
        float y = Mathf.Lerp(bounds.min.y, bounds.max.y, Random.Range(0.3f, 0.8f));
        return new Vector3(
            victim.position.x + Mathf.Cos(angle) * radius,
            y,
            victim.position.z + Mathf.Sin(angle) * radius);
    }

    private static void SetLocalHeight(Transform root, string childName, float height)
    {
        Transform child = root.Find(childName);
        if (child == null) return;
        Vector3 local = child.localPosition;
        local.y = height;
        child.localPosition = local;
    }

    private static void SetStartColorRgb(ParticleSystem particle, Color color)
    {
        ParticleSystem.MainModule main = particle.main;
        float alpha = main.startColor.color.a;
        main.startColor = new Color(color.r, color.g, color.b, alpha);
    }

    private static void SetStartSize(ParticleSystem particle, float size)
    {
        ParticleSystem.MainModule main = particle.main;
        main.startSize = size;
    }

    /// <summary>プレハブの子(輪)に入っている大きさを読む。見つからなければ fallback を返す</summary>
    private float ReadStartSize(string childName, float fallback)
    {
        if (effectPrefab == null) return fallback;
        Transform child = effectPrefab.transform.Find(childName);
        if (child == null) return fallback;
        ParticleSystem particle = child.GetComponent<ParticleSystem>();
        return particle != null ? particle.main.startSize.constant : fallback;
    }

    private void StartShake()
    {
        if (!shakeEnabled) return;
        shakeTimer = shakeSeconds;
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // 前のフレームで足した揺れを戻してから、新しい揺れを足す(カメラを動かす他の処理とぶつからないように)
        cam.transform.position -= lastShakeOffset;
        lastShakeOffset = Vector3.zero;

        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            float strength = shakeStrength * Mathf.Clamp01(shakeTimer / shakeSeconds);
            lastShakeOffset = cam.transform.right * Random.Range(-1f, 1f) * strength
                            + cam.transform.up * Random.Range(-1f, 1f) * strength;
            cam.transform.position += lastShakeOffset;
        }
    }

    // 画面右上のテスト用パネル(「タイトルに戻る」ボタンと「演出テスト」の下に出す)
    private void OnGUI()
    {
        const float width = 260f;
        float x = Screen.width - width - 10f;
        const float top = 130f;

        if (collapsed)
        {
            if (GUI.Button(new Rect(Screen.width - 150f, top, 140f, 30f), "ヒット確認 ▼")) collapsed = false;
            return;
        }

        GUILayout.BeginArea(new Rect(x, top, width, 400f), GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("ぶつけっこ: ヒット");
        if (GUILayout.Button("たたむ ▲", GUILayout.Width(80))) collapsed = true;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("1Pが当てた(カカシ)")) HitScarecrowBy1P();
        if (GUILayout.Button("2Pに当てられた")) Hit1PBy(1);
        if (GUILayout.Button("3Pに当てられた")) Hit1PBy(2);
        if (GUILayout.Button("4Pに当てられた")) Hit1PBy(3);

        GUILayout.Space(8);
        GUILayout.Label($"外側の輪の大きさ: {outerRingSize:0.0}");
        outerRingSize = GUILayout.HorizontalSlider(outerRingSize, 1f, 15f);
        GUILayout.Label($"内側の輪の大きさ: {innerRingSize:0.0}");
        innerRingSize = GUILayout.HorizontalSlider(innerRingSize, 1f, 15f);

        GUILayout.Space(8);
        shakeEnabled = GUILayout.Toggle(shakeEnabled, " 画面揺れ(当てられたとき)");
        GUI.enabled = shakeEnabled;
        GUILayout.Label($"揺れの強さ: {shakeStrength:0.00}");
        shakeStrength = GUILayout.HorizontalSlider(shakeStrength, 0f, 0.6f);
        GUILayout.Label($"揺れの長さ: {shakeSeconds:0.00} 秒");
        shakeSeconds = GUILayout.HorizontalSlider(shakeSeconds, 0.05f, 0.6f);
        GUI.enabled = true;

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("元に戻す"))
        {
            outerRingSize = ReadStartSize(OuterRingName, 8.5f);
            innerRingSize = ReadStartSize(InnerRingName, 5.2f);
            shakeEnabled = true;
            shakeStrength = defaultShakeStrength;
            shakeSeconds = defaultShakeSeconds;
            saveMessage = "";
        }
#if UNITY_EDITOR
        if (GUILayout.Button("プレハブに保存")) SaveToPrefab();
#endif
        GUILayout.EndHorizontal();
        if (saveMessage != "") GUILayout.Label(saveMessage);

        GUILayout.EndArea();
    }

#if UNITY_EDITOR
    /// <summary>今の輪の大きさを、エフェクトのプレハブに書き込む(Play を止めても残る)</summary>
    private void SaveToPrefab()
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(effectPrefab);
        if (string.IsNullOrEmpty(path)) return;

        GameObject root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
        try
        {
            SetStartSize(root.transform.Find(OuterRingName).GetComponent<ParticleSystem>(), outerRingSize);
            SetStartSize(root.transform.Find(InnerRingName).GetComponent<ParticleSystem>(), innerRingSize);
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            UnityEditor.PrefabUtility.UnloadPrefabContents(root);
        }
        saveMessage = $"保存しました(外 {outerRingSize:0.0} / 内 {innerRingSize:0.0})";
    }
#endif
}
