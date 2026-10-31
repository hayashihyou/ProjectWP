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

    [Header("ぼかし")]
    [Tooltip("カメラからこの距離までは、くっきり見える(0なら手前から全部ぼける)")]
    [SerializeField] private float blurStartDistance = 0f;

    [Tooltip("カメラからこの距離より奥は、最大までぼける(小さい程ステージ全体が強くぼける)")]
    [SerializeField] private float blurEndDistance = 2f;

    [Tooltip("ぼかしの強さ(0.5～1.5)")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float blurRadius = 1.5f;

    [Tooltip("ぼかしがかかりきるまでの秒数")]
    [SerializeField] private float blurSeconds = 0.8f;

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
            yield return null;
        }

        if(resultCameraPoint != null)
        {
            camTransform.SetPositionAndRotation(resultCameraPoint.position, resultCameraPoint.rotation);
        }
        blurVolume.weight = 1f;

        IsReady = true;
        OnReady?.Invoke();
    }


    private void CreateBlurVolume()
    {
        if(blurVolume != null) return;

        blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        DepthOfField dof = blurProfile.Add<DepthOfField>(true);
        dof.mode.value = DepthOfFieldMode.Gaussian; //　軽いのでスマホでも使える
        dof.gaussianStart.value = blurStartDistance;
        dof.gaussianEnd.value = Mathf.Max(blurEndDistance, blurStartDistance + 0.01f);
        dof.gaussianMaxRadius.value = blurRadius;
        dof.highQualitySampling.value = true;

        blurVolume = gameObject.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f; //　他のボリュームより優先度を高くする
        blurVolume.weight = 0f;
        blurVolume.sharedProfile = blurProfile;
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
