using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 通信の状態を画面の左上に表示する、調査用の道具。F3 キーで表示/非表示。
/// エディターと Development Build でだけ動く(シーンに置かなくても、起動時に自動で作られる)。
///
/// 「遊ぶほどラグが増える」ときの見分け方:
///   ・FPS が下がっていく → PC の処理が追いついていない
///   ・RTT が増えていく   → 通信が詰まって溜まっている
/// 10秒ごとに Console にも同じ内容を出すので、時間がたつと数値がどう変わったかを後から見比べられる。
/// </summary>
public class NetworkStatsOverlay : MonoBehaviour
{
    private const float SampleSeconds = 1f;       // FPS を数える区切り(秒)
    private const float LogIntervalSeconds = 10f; // Console に出す間隔(秒)

    private bool visible = true;

    private int frames;
    private float sampleTimer;
    private float worstFrameSeconds;
    private float logTimer;

    private string text = "";
    private GUIStyle style;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        GameObject go = new GameObject("[NetworkStatsOverlay]");
        DontDestroyOnLoad(go);
        go.AddComponent<NetworkStatsOverlay>();
    }
#endif

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            visible = !visible;
        }

        float deltaTime = Time.unscaledDeltaTime;

        // 1秒ごとに、FPS と「一番重かったフレーム」をまとめる
        frames++;
        sampleTimer += deltaTime;
        if (deltaTime > worstFrameSeconds) worstFrameSeconds = deltaTime;

        if (sampleTimer >= SampleSeconds)
        {
            text = BuildText(frames / sampleTimer, worstFrameSeconds * 1000f);
            frames = 0;
            sampleTimer = 0f;
            worstFrameSeconds = 0f;
        }

        // 10秒ごとに Console にも残す(時間がたつと数値がどう変わるかを見るため)
        logTimer += deltaTime;
        if (logTimer >= LogIntervalSeconds)
        {
            logTimer = 0f;
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsListening)
            {
                Debug.Log($"[NetStats] t={Time.realtimeSinceStartup:0}s / " + text.Replace("\n", " / "));
            }
        }
    }

    private string BuildText(float fps, float worstFrameMs)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append($"FPS {fps:0}(一番重いフレーム {worstFrameMs:0}ms)");

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening) return sb.ToString();

        NetworkTransport transport = networkManager.NetworkConfig.NetworkTransport;
        sb.Append($"\n{(networkManager.IsHost ? "ホスト" : "参加者")}  Tick {networkManager.NetworkConfig.TickRate}  補間の待ち {NetworkTransform.GetTickLatency():0.0}tick");

        if (networkManager.IsServer)
        {
            // ホスト : 参加者それぞれとの往復時間
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId == networkManager.LocalClientId) continue;
                sb.Append($"\n  参加者(ID {clientId})との RTT {transport.GetCurrentRtt(clientId)}ms");
            }
        }
        else
        {
            // 参加者 : ホストとの往復時間
            sb.Append($"\n  ホストとの RTT {transport.GetCurrentRtt(NetworkManager.ServerClientId)}ms");
        }

        return sb.ToString();
    }

    private void OnGUI()
    {
        if (!visible || string.IsNullOrEmpty(text)) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16 };
            style.normal.textColor = Color.white;
        }

        float height = style.CalcHeight(new GUIContent(text), 380f);
        GUI.Box(new Rect(10f, 10f, 380f, height), text, style);
    }
}