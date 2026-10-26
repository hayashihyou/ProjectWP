using UnityEngine;
using Unity.Netcode;

/// <summary>
/// シーンが始まったときに、全員のキャラクターを「そのシーンの初期位置」に並べる。
///
/// キャラクターはシーンをまたいで生き続けるので、何もしないと待機画面で立っていた位置
/// (1P〜4Pの枠の位置)のまま次のシーンが始まってしまう。そこで、ゲーム用のシーンに
/// これを置き、待機画面で決まった枠の番号(1P〜4P)と同じ番号の初期位置に移動させる。
///
/// 使い方: シーンに空のGameObjectを作ってこれを付け、spawnPointsに
/// 1P〜4Pの順で初期位置のTransformを設定する。
/// 動かすのはサーバーだけ。各キャラクターは所有者のPCで位置を決めているので、
/// PlayerSlot.MoveTo経由で所有者に「ここに立って」と頼む。
/// </summary>
public class PlayerSpawnPoints : MonoBehaviour
{
    [Tooltip("1P〜4Pの順に、そのシーンでの初期位置。向きもこのTransformの向きに合わせる")]
    [SerializeField] private Transform[] spawnPoints = new Transform[PlayerSlotManager.SlotCount];

    [Header("向き")]
    [Tooltip("オンにすると、初期位置のTransformの向きではなく、カメラの方を向いて立つ(左右の向きだけ。見上げ/見下ろしはしない)")]
    [SerializeField] private bool faceCamera = false;

    [Tooltip("向く先のカメラ。空のときはメインカメラ(MainCameraタグのカメラ)を使う")]
    [SerializeField] private Camera targetCamera;

    private void Start()
    {
        PlaceAllPlayers();
    }

    /// <summary>
    /// 全員のキャラクターを、枠の番号(1P〜4P)と同じ番号の初期位置に並べる。
    /// シーン開始時のほか、ミニゲームのラウンドのリセット(MinigameBase)からも呼ばれる。
    /// </summary>
    public void PlaceAllPlayers()
    {
        // ネットワークに繋がっていない(シーン単体で試している)場合や、参加者側のPCでは何もしない
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer) return;

        foreach (PlayerSlot player in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            int slot = player.SlotIndex;
            if (slot < 0 || slot >= spawnPoints.Length || spawnPoints[slot] == null) continue;

            Vector3 position = spawnPoints[slot].position;
            player.MoveTo(position, GetStandRotation(position, spawnPoints[slot].rotation));
        }
    }

    /// <summary>
    /// その位置に立つときの向きを返す。
    /// faceCameraがオフ、またはカメラが見つからないときは、初期位置のTransformの向き(fallback)をそのまま使う。
    /// </summary>
    private Quaternion GetStandRotation(Vector3 standPosition, Quaternion fallback)
    {
        if (!faceCamera) return fallback;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return fallback;

        // 高さ(Y)をそろえて、左右の向きだけカメラに合わせる(体が前後に傾かないように)
        Vector3 toCamera = cam.transform.position - standPosition;
        toCamera.y = 0f;

        // カメラの真上/真下に立っているとLookRotationの向きが決まらないので、念のためガード
        if (toCamera.sqrMagnitude < 0.0001f) return fallback;

        return Quaternion.LookRotation(toCamera);
    }
}