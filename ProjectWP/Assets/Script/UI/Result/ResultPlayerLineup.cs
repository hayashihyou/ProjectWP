using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// リザルトでプレイヤーのモデルを1位〜4位の順に並べる(リザルトUIシーンに1つ置く)
///
/// ・子に ResultRankSpot(1位〜4位の台)を4つ置く
/// ・表示するときに、このオブジェクトをカメラと同じ位置・向きに合わせる
///   → 台の位置は「カメラから見てどこか」で決まるので、どのミニゲームのステージでも同じ見え方になる
///   (シーンビューで並び方を調整するときは、このオブジェクトを ResultCameraPoint と同じ位置・向きにして見る)
/// ・並べるのはネットワークに関係ない「見た目だけのモデル」。ステージ上の本物のキャラクターは
///   MinigameResultBackground の Hide Players On Result で隠しておく
/// </summary>
public class ResultPlayerLineup : MonoBehaviour
{
    [Header("モデル")]
    [Tooltip("並べるモデル。SM_Chibi_Character.fbx に Animator(ChibiCharacter_Controler)だけ付けたプレハブを作って入れる。" +
             "ネットワーク用のプレハブ(NetworkObject 付き)は使えない")]
    [SerializeField] private GameObject displayModelPrefab;

    [Tooltip("枠(PlayerSlot)が見つからないときに使う色(1P, 2P, 3P, 4P の順)")]
    [SerializeField]
    private Color[] fallbackColors =
    {
        Color.white,
        new Color(0.75f, 0.6f, 1f),
        new Color(0.4f, 0.9f, 1f),
        new Color(0.6f, 1f, 0.2f),
    };

    [Header("カメラ")]
    [Tooltip("並べる基準のカメラ。空なら MainCamera")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("オン = 表示するときにカメラの位置・向きに合わせる。オフ = 置いた場所のまま")]
    [SerializeField] private bool alignToCamera = true;


    private readonly List<ResultRankSpot> spots = new List<ResultRankSpot>();
    private MaterialPropertyBlock propertyBlock;


    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();

        spots.AddRange(GetComponentsInChildren<ResultRankSpot>(true));
        spots.Sort((a, b) => a.Order.CompareTo(b.Order));
    }


    /// <summary>結果(MinigameInfo.LastResult)の順にモデルを並べる</summary>
    public void Show()
    {
        Clear();

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (alignToCamera && cam != null)
        {
            transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
        }

        if (displayModelPrefab == null)
        {
            Debug.LogWarning("[ResultLineup] Display Model Prefab が空なので、キャラクターを並べられません");
            return;
        }

        Dictionary<int, PlayerSlot> slots = FindPlayerSlots();

        foreach (ResultRankSpot spot in spots)
        {
            // order 番目に良い人がいなければ(3人で遊んだときの4位の台など)、空けておく
            if (!MinigameRanking.TryGetByOrder(spot.Order, out MinigameRankEntry entry)) continue;

            GameObject model = CreateDisplayModel(displayModelPrefab, spot.transform);
            ApplyColor(model, GetSlotColor(entry.SlotIndex, slots));
            PlayAnimation(model, spot.AnimationState);

            spot.Show(entry, model, cam);
            Debug.Log($"[ResultLineup] {spot.Order}番目の台: {entry.SlotIndex + 1}P ({entry.Rank}位)");
        }
    }

    /// <summary>並べたモデルと「〇位」の文字を隠す</summary>
    public void Hide()
    {
        foreach (ResultRankSpot spot in spots)
        {
            spot.Hide();
        }
    }

    /// <summary>並べたモデルを消す</summary>
    public void Clear()
    {
        foreach (ResultRankSpot spot in spots)
        {
            spot.Clear();
        }
    }

    /// <summary>その順番(1始まり)の台を返す。無ければ null</summary>
    public ResultRankSpot GetSpot(int order)
    {
        return spots.Find(spot => spot.Order == order);
    }


    // 枠番号 → PlayerSlot の対応表(キャラクターの色を本物とそろえるため)
    private static Dictionary<int, PlayerSlot> FindPlayerSlots()
    {
        Dictionary<int, PlayerSlot> result = new Dictionary<int, PlayerSlot>();
        foreach (PlayerSlot slot in FindObjectsByType<PlayerSlot>(FindObjectsSortMode.None))
        {
            if (slot.SlotIndex >= 0) result[slot.SlotIndex] = slot;
        }
        return result;
    }

    private Color GetSlotColor(int slotIndex, Dictionary<int, PlayerSlot> slots)
    {
        if (slots.TryGetValue(slotIndex, out PlayerSlot slot)) return slot.SlotColor;
        if (slotIndex >= 0 && slotIndex < fallbackColors.Length) return fallbackColors[slotIndex];
        return Color.white;
    }

    // PlayerCharacterVisual.ApplyColor と同じやり方で色を付ける(マテリアルを複製しない)
    private void ApplyColor(GameObject model, Color color)
    {
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            r.SetPropertyBlock(propertyBlock);
        }
    }

    private static void PlayAnimation(GameObject model, string stateName)
    {
        Animator animator = model.GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null) return;

        if (!string.IsNullOrEmpty(stateName) && animator.HasState(0, Animator.StringToHash(stateName)))
        {
            animator.Play(stateName);
        }
        else if (animator.HasState(0, Animator.StringToHash("Idle")))
        {
            animator.Play("Idle");
        }
    }

    // NetworkBehaviour を、ほかのコンポーネントに [RequireComponent] で必要とされていないものから順に消す
    // (先に PlayerCharacterVisual を消そうとすると「PlayerSlot depends on it」のエラーになるため)
    private static void DestroyNetworkBehavioursInOrder(GameObject root)
    {
        List<NetworkBehaviour> remaining = new List<NetworkBehaviour>(root.GetComponentsInChildren<NetworkBehaviour>(true));

        // 1回の周回で1つも消せなければ(循環している場合)、無限ループにならないように抜ける
        bool removedAny = true;
        while (remaining.Count > 0 && removedAny)
        {
            removedAny = false;
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                NetworkBehaviour target = remaining[i];
                if (target == null) { remaining.RemoveAt(i); continue; }
                if (IsRequiredByOthers(target)) continue;

                DestroyImmediate(target);
                remaining.RemoveAt(i);
                removedAny = true;
            }
        }

        if (remaining.Count > 0)
        {
            Debug.LogWarning($"[ResultLineup] 消せない NetworkBehaviour が {remaining.Count} 個残りました(依存関係が循環しています)");
        }
    }

    // 同じ GameObject のほかのコンポーネントが、[RequireComponent] でこのコンポーネントを必要としているか
    private static bool IsRequiredByOthers(Component target)
    {
        System.Type targetType = target.GetType();
        foreach (Component other in target.GetComponents<Component>())
        {
            if (other == null || other == target) continue;

            object[] attrs = other.GetType().GetCustomAttributes(typeof(RequireComponent), true);
            foreach (RequireComponent req in attrs)
            {
                if (Requires(req.m_Type0, targetType) || Requires(req.m_Type1, targetType) || Requires(req.m_Type2, targetType))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool Requires(System.Type required, System.Type targetType)
    {
        return required != null && required.IsAssignableFrom(targetType);
    }

    private GameObject CreateDisplayModel(GameObject networkPrefab, Transform spot)
    {
        // 非アクティブの入れ物の下に作ると、Awake/OnEnable が走らない
        GameObject holder = new GameObject("ModelHolder");
        holder.SetActive(false);
        holder.transform.SetParent(spot, false);

        GameObject model = Instantiate(networkPrefab, holder.transform);

        // NetworkBehaviour(PlayerSlot・PlayerCharacterVisualなど)を先に消し、最後にNetworkObjectを消す
        // [RequireComponent] で依存されているもの(PlayerSlot → PlayerCharacterVisual など)は、依存している側から先に消す
        DestroyNetworkBehavioursInOrder(model);
        foreach (NetworkObject o in model.GetComponentsInChildren<NetworkObject>(true)) DestroyImmediate(o);

        // ネットワーク以外の操作系(PlayerMovementなど)が残っていれば止める
        foreach (MonoBehaviour m in model.GetComponentsInChildren<MonoBehaviour>(true)) m.enabled = false;
        CharacterController cc = model.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        model.transform.SetParent(spot, false);
        Destroy(holder);
        model.SetActive(true);
        return model;
    }
}
