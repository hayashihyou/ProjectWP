using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 設定画面などを開いている間、ゲーム側の操作(キャラの移動・ミニゲームの操作・準備OK など)を止める。
///
/// 使い方:
///   GameInputLock.Lock(this);    // 画面を開いたとき
///   GameInputLock.Unlock(this);  // 画面を閉じたとき
///
/// 止めるのは「PlayerInput から読んでいる操作」(PlayerMovement / PlayerMinigameInput など)。
/// メニューを動かす操作(EventSystem)は止めないので、開いた画面はふつうに操作できる。
/// キーボードを直接読んでいるスクリプトは、自分で GameInputLock.IsLocked を見て操作を無視する。
///
/// 何個の画面がロックしていても、全部が Unlock するまで止まったまま(画面を重ねて開いても大丈夫)。
/// </summary>
public static class GameInputLock
{
    // ロックしている画面の一覧
    private static readonly HashSet<Object> Owners = new HashSet<Object>();

    // Unlock されて、フレームの最後に外れるのを待っている画面
    private static readonly HashSet<Object> PendingUnlock = new HashSet<Object>();

    // ロック中に止めた PlayerInput(解除するときに元に戻す)
    private static readonly List<PlayerInput> Deactivated = new List<PlayerInput>();

    private static Runner _runner;

    /// <summary>今ゲーム側の操作が止まっているか</summary>
    public static bool IsLocked => Owners.Count > 0;

    public static void Lock(Object owner)
    {
        if (owner == null) { return; }
        PendingUnlock.Remove(owner); // 閉じてすぐ開き直したときは、外す予定を取り消す
        Owners.Add(owner);
        DeactivatePlayerInputs();
        EnsureRunner();
    }

    /// <summary>
    /// ロックを外す。外すのはそのフレームの最後にする
    /// (閉じるのに押した Esc / Bボタン が、同じフレームでゲーム側にも届いてしまわないように)。
    /// </summary>
    public static void Unlock(Object owner)
    {
        if (owner == null || !Owners.Contains(owner)) { return; }
        EnsureRunner();
        if (PendingUnlock.Add(owner)) { _runner.StartCoroutine(UnlockAtEndOfFrame(owner)); }
    }

    private static IEnumerator UnlockAtEndOfFrame(Object owner)
    {
        yield return new WaitForEndOfFrame();
        if (!PendingUnlock.Remove(owner)) { yield break; } // その間にまた Lock された
        Owners.Remove(owner);
        if (!IsLocked) { RestorePlayerInputs(); }
    }

    // 止めていた PlayerInput を元に戻す
    private static void RestorePlayerInputs()
    {
        foreach (PlayerInput input in Deactivated)
        {
            if (input != null && input.isActiveAndEnabled) { input.ActivateInput(); }
        }
        Deactivated.Clear();
    }

    // 有効な PlayerInput の操作を全部止める(止めたものは覚えておく)
    private static void DeactivatePlayerInputs()
    {
        foreach (PlayerInput input in PlayerInput.all)
        {
            if (input.isActiveAndEnabled && input.inputIsActive)
            {
                input.DeactivateInput();
                if (!Deactivated.Contains(input)) { Deactivated.Add(input); }
            }
        }
    }

    private static void EnsureRunner()
    {
        if (_runner != null) { return; }
        var go = new GameObject("GameInputLock");
        Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<Runner>();
    }

    // ロック中にあとから有効になった PlayerInput(キャラが出てきた等)も止めるため、毎フレーム見張る
    private class Runner : MonoBehaviour
    {
        private void Update()
        {
            // ロックした画面が消えてしまった(シーンごと破棄された等)ときは、ロックを外す
            if (Owners.RemoveWhere(o => o == null) > 0 && !IsLocked) { RestorePlayerInputs(); }

            if (IsLocked) { DeactivatePlayerInputs(); }
        }
    }

    // ドメインリロードなしで Play し直したとき、前回の状態が残らないようにする
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Owners.Clear();
        PendingUnlock.Clear();
        Deactivated.Clear();
        _runner = null;
    }
}
