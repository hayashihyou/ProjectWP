using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


/// <summary>
/// リザルト画面の背景を作る(各ミニゲームのシーンに1つ置く)
/// 
/// リザルトになったら
/// 　1.カメラを「リザルト用の位置」(少し斜めのアングルなど)へなめらかに動かし
/// 　2.画面全体にぼかし(被写界深度)をかける
/// ステージそのものが背景になるので、背景用にステージを作り直す必要はない
/// 
/// ミニゲームごとの違いは、このコンポーネントの設定(カメラの位置・ぼかしの強さ)だけで出す
/// 順位などのUIは共通のリザルトUIシーン(MinigameResultUILoaderが読み込む)が担当する
/// </summary>
public class MinigameResultBackground : MonoBehaviour
{
    [Header("カメラ")]
    [Tooltip("動かすカメラ。空ならMainCameraを使う")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("リザルト中のカメラ位置・向き・空のオブジェクトを置いて、Sceneビューで好きなアングルに合わせる")]
    [SerializeField] private Transform resultCameraPoint;

    [Tooltip("今の位置からリザルト用の位置まで動く秒数。0にするとすぐ切り替わる")]
    [SerializeField] private float moveSeconds = 1.2f;

    /// <summary>ぼかしのかけ方</summary>
    public enum BlurMode
    {
        Gaussian,   // 軽い(スマホ向け)。ぼかしの強さは半径1.5まで
        Bokeh       // 少し重いが、強くぼける。ピントの距離より奥ほどぼける(キャラはくっきり)
    }

    [Header("ぼかし")]
    [Tooltip("Gaussian = 軽いがぼかしは弱め / Bokeh = 少し重いが強くぼける(ピントの距離にいるキャラはくっきり)")]
    [SerializeField] private BlurMode blurMode = BlurMode.Gaussian;

    [Tooltip("(Bokeh) ピントを合わせる距離。リザルトのキャラがいる距離にする(MinigameResult の台はカメラから約4m)")]
    [SerializeField, Min(0.1f)] private float bokehFocusDistance = 4f;

    [Tooltip("(Bokeh) 絞り。小さいほど強くぼける(1〜32)。キャラまでぼけるなら大きくする")]
    [SerializeField, Range(1f, 32f)] private float bokehAperture = 2.8f;

    [Tooltip("(Bokeh) レンズの焦点距離(mm)。大きいほど強くぼける(1〜300)")]
    [SerializeField, Range(1f, 300f)] private float bokehFocalLength = 100f;

    [Tooltip("(Gaussian) カメラからこの距離までは、くっきり見える(0なら手前から全部ぼける)")]
    [SerializeField] private float blurStartDistance = 0f;

    [Tooltip("(Gaussian) カメラからこの距離より奥は、最大までぼける(小さい程ステージ全体が強くぼける)")]
    [SerializeField] private float blurEndDistance = 2f;

    [Tooltip("(Gaussian) ぼかしの強さ(0.5～1.5)")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float blurRadius = 1.5f;

    [Tooltip("ぼかしがかかりきるまでの秒数")]
    [SerializeField] private float blurSeconds = 0.8f;

    /// <summary>
    /// リザルトのキャラクターを置くレイヤー。このレイヤーのものだけを、暗くした画面の上に専用のカメラで明るく描き直す。
    /// (Project Settings → Tags and Layers にこの名前のレイヤーが必要)
    /// </summary>
    public const string CharacterLayerName = "ResultCharacter";

    [Header("暗さ")]
    [Tooltip("リザルト中に背景(ステージ)を暗くする濃さ。0 = 暗くしない / 0.45 = 準備確認(ReadyUI)と同じ暗さ。" +
             "画面全体(床も含む)を暗くして、キャラクター(ResultCharacter レイヤー)だけ専用のカメラで明るく描き直す")]
    [SerializeField, Range(0f, 1f)] private float backgroundDimAlpha = 0.45f;

    [Tooltip("ResultCharacter レイヤーが無いときだけ使う: 暗くする幕を置く距離(キャラクターより奥。これより手前の床などは暗くならない)")]
    [SerializeField, Min(0.1f)] private float dimDistance = 4.7f;

    [Header("リザルト中に止めるもの")]
    [Tooltip("リザルト中に止めるスクリプト(カメラを動かすスクリプトなど。止めないとカメラの取り合いになる)")]
    [SerializeField] private Behaviour[] disableOnResult = Array.Empty<Behaviour>();

    [Tooltip("リザルト中に隠すオブジェクト(プレイ中のHUD・操作ボタンなど)")]
    [SerializeField] private GameObject[] hideOnResult = Array.Empty<GameObject>();

    [Tooltip("オン = リザルトになったら、ステージの上のプレイヤーを隠す(リザルトではResultPlayerLineupが順位順に並べ直す)")]
    [SerializeField] private bool hidePlayersOnResult = true;


    /// <summary>背景が出来上がった(カメラが着いて。ぼかしがかかりきった)。共通UIの登場のきっかけにつかえる</summary>
    public static event Action OnReady;

    /// <summary>背景が出来上がっているか(UIしーんが後から読み込まれたときの確認用)</summary>
    public static bool IsReady { get; private set; }


    private Volume blurVolume;
    private VolumeProfile blurProfile;
    private GameObject dimQuad;
    private Material dimMaterial;
    private Camera characterCamera;
    private Camera baseCamera;
    private Coroutine showRoutine;


    private void OnEnable()
    {
        IsReady = false;
        MinigameInfo.OnPhaseChanged += HandlePhaseChanged;
    }

    private void OnDisable()
    {
        MinigameInfo.OnPhaseChanged -= HandlePhaseChanged;
        IsReady = false;
    }

    private void OnDestroy()
    {
        if (blurProfile != null)
        {
            Destroy(blurProfile);
        }
        if (dimQuad != null) Destroy(dimQuad);
        if (dimMaterial != null) Destroy(dimMaterial);
        if (characterCamera != null)
        {
            if (baseCamera != null)
            {
                UniversalAdditionalCameraData baseData = baseCamera.GetUniversalAdditionalCameraData();
                if (baseData != null) baseData.cameraStack.Remove(characterCamera);
            }
            Destroy(characterCamera.gameObject);
        }
    }


    private void HandlePhaseChanged(MinigamePhase phase, int round)
    {
        if (phase != MinigamePhase.Result) return;
        if (showRoutine != null) return;

        showRoutine = StartCoroutine(Show());
    }


    private IEnumerator Show()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[ResultBackground]カメラが見つかりません。TargetCameraを設定してください");
            yield break;
        }


        foreach (Behaviour behaviour in disableOnResult)
        {
            if (behaviour != null) behaviour.enabled = false;
        }
        foreach (GameObject target in hideOnResult)
        {
            if (target != null) target.SetActive(false);
        }
        if(hidePlayersOnResult)
        {
            foreach(PlayerCharacterVisual player in FindObjectsByType<PlayerCharacterVisual>(FindObjectsSortMode.None))
            {
                player.SetHiddenTemporarily(true);
            }
        }

        // ぼかしにはホストプロセスが必要なので、リザルトの間だけオンにする
        UniversalAdditionalCameraData cameraData = cam.GetUniversalAdditionalCameraData();
        if (cameraData != null)
        {
            cameraData.renderPostProcessing = true;
        }

        CreateBlurVolume();
        CreateDimQuad(cam);

        //　カメラ移動とぼかしを同時に進める
        Transform camTransform = cam.transform;
        Vector3 fromPosition = camTransform.position;
        Quaternion fromRotation = camTransform.rotation;

        if(resultCameraPoint == null)
        {
            Debug.LogWarning("[ResultBackground] ResultCameraPointが空なので、カメラは動かさずにぼかしだけかけます");
        }

        float duration = Mathf.Max(moveSeconds, blurSeconds);
        for(float time = 0f; time < duration; time += Time.deltaTime)
        {
            if(resultCameraPoint != null)
            {
                float move = moveSeconds > 0f ? Mathf.SmoothStep(0f,1f,time / moveSeconds) : 1f;
                camTransform.SetPositionAndRotation(
                    Vector3.Lerp(fromPosition,resultCameraPoint.position,move),
                    Quaternion.Lerp(fromRotation,resultCameraPoint.rotation,move));
            }

            blurVolume.weight = blurSeconds > 0f ? Mathf.Clamp01(time / blurSeconds) : 1f;
            SetDim(blurVolume.weight); // 暗さもぼかしと一緒に濃くする
            yield return null;
        }

        if(resultCameraPoint != null)
        {
            camTransform.SetPositionAndRotation(resultCameraPoint.position, resultCameraPoint.rotation);
        }
        blurVolume.weight = 1f;
        SetDim(1f);

        IsReady = true;
        OnReady?.Invoke();
    }


    private void CreateBlurVolume()
    {
        if(blurVolume != null) return;

        blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        DepthOfField dof = blurProfile.Add<DepthOfField>(true);
        if (blurMode == BlurMode.Bokeh)
        {
            // 強くぼける。ピントの距離(キャラ)はくっきり、奥ほどぼける
            dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.value = bokehFocusDistance;
            dof.aperture.value = bokehAperture;
            dof.focalLength.value = bokehFocalLength;
        }
        else
        {
            dof.mode.value = DepthOfFieldMode.Gaussian; //　軽いのでスマホでも使える
            dof.gaussianStart.value = blurStartDistance;
            dof.gaussianEnd.value = Mathf.Max(blurEndDistance, blurStartDistance + 0.01f);
            dof.gaussianMaxRadius.value = blurRadius;
            dof.highQualitySampling.value = true;
        }

        blurVolume = gameObject.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f; //　他のボリュームより優先度を高くする
        blurVolume.weight = 0f;
        blurVolume.sharedProfile = blurProfile;
    }


    // 背景を暗くする黒い幕を、カメラの前(キャラクターの奥)に作る。カメラの子にするので、カメラが動いてもついてくる
    // 画面全体にかぶせると手前のキャラクターまで暗くなるので、UI ではなく3Dの板にしている
    private void CreateDimQuad(Camera cam)
    {
        if (dimQuad != null || backgroundDimAlpha <= 0f) return;

        // キャラクターを専用のカメラで描き直せるなら、幕はカメラのすぐ前に置いて画面全体(手前の床も)を暗くする。
        // できなければ、キャラクターの奥に置く(手前の床などは暗くならない)
        float distance = CreateCharacterCamera(cam) ? cam.nearClipPlane + 0.05f : dimDistance;

        dimQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        dimQuad.name = "ResultBackgroundDim";
        Collider quadCollider = dimQuad.GetComponent<Collider>();
        if (quadCollider != null) Destroy(quadCollider);

        // 半透明の黒(Sprites/Default はビルドにも必ず入っている)。奥行きを書かないので、ぼかしの邪魔をしない
        dimMaterial = new Material(Shader.Find("Sprites/Default"));
        dimMaterial.color = new Color(0f, 0f, 0f, 0f);

        MeshRenderer quadRenderer = dimQuad.GetComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = dimMaterial;
        quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;

        // カメラの前に、画面より少し大きく置く
        Transform quad = dimQuad.transform;
        quad.SetParent(cam.transform, false);
        quad.localPosition = new Vector3(0f, 0f, distance);
        quad.localRotation = Quaternion.identity;
        float height = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.2f;
        quad.localScale = new Vector3(height * Mathf.Max(cam.aspect, 1f) * 1.2f, height, 1f);
    }

    // リザルトのキャラクター(ResultCharacter レイヤー)だけを写すカメラを作り、いつものカメラの上に重ねる(URP のカメラスタック)。
    // キャラクターはいつものカメラにも(暗く・影つきで)写り、その上からこのカメラが明るく描き直す。ぼかしもかからない
    private bool CreateCharacterCamera(Camera cam)
    {
        if (characterCamera != null) return true;

        int layer = LayerMask.NameToLayer(CharacterLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"[ResultBackground] レイヤー「{CharacterLayerName}」が無いので、キャラクターより手前は暗くなりません。Tags and Layers に追加してください");
            return false;
        }

        UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
        if (baseData == null || baseData.renderType != CameraRenderType.Base) return false;

        GameObject cameraObject = new GameObject("ResultCharacterCamera");
        cameraObject.transform.SetParent(cam.transform, false);
        characterCamera = cameraObject.AddComponent<Camera>();
        characterCamera.CopyFrom(cam); // 画角・描画範囲をそろえる
        characterCamera.transform.localPosition = Vector3.zero;
        characterCamera.transform.localRotation = Quaternion.identity;
        characterCamera.cullingMask = 1 << layer;

        UniversalAdditionalCameraData data = characterCamera.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Overlay;
        data.renderPostProcessing = false; // キャラクターはぼかさない
        // Overlay カメラは最初から深度をクリアする(Clear Depth がオン)ので、暗くした画面の上にそのまま描かれる

        baseData.cameraStack.Add(characterCamera);
        baseCamera = cam;
        return true;
    }

    // 暗さ(0 = なし 〜 1 = 設定した濃さ)
    private void SetDim(float amount)
    {
        if (dimMaterial == null) return;
        dimMaterial.color = new Color(0f, 0f, 0f, backgroundDimAlpha * Mathf.Clamp01(amount));
    }


#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (resultCameraPoint == null) return;

        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        Gizmos.matrix = Matrix4x4.TRS(resultCameraPoint.position, resultCameraPoint.rotation, Vector3.one);
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        float fov = cam != null ? cam.fieldOfView : 60f;
        float aspect = cam != null ? cam.aspect : 16f / 9f;
        Gizmos.DrawFrustum(Vector3.zero, fov, 2f, 0.1f, aspect);
    }
#endif
}
