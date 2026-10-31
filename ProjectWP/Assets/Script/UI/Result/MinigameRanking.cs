using System.Collections.Generic;


/// <summary>
/// 結果から「何位が誰か」「誰が何位か」を調べるための窓口
/// 
/// 並び順(order)と順位(rank)は別物なので注意：
///     ・order：並べる場所の番号(1=一番左の台...4=一番右の台)必ず1,2,3,4と1つずつ増える
///     ・rank：実際の順位。同着があると「1位,1位,3位,4位」の様に飛ぶ
/// 台(ResultRankSpot)はorderで決め、表示する「〇位」の文字はrankで決める
/// </summary>
public static class MinigameRanking
{
    /// <summary>結果に載っている人数</summary>
    public static int Count => MinigameInfo.LastResult != null ? MinigameInfo.LastResult.Count : 0;

    /// <summary>結果が届いているか</summary>
    public static bool HasResult => Count > 0;


    /// <summary>
    /// 並び順(1始まり)の人を取り出す。1=いちばん成績が良い人
    /// LastResultは良い順に並んで届くので、その順番のまま使う
    /// </summary>
    public static bool TryGetByOrder(int order,out MinigameRankEntry entry)
    {
        entry = default;
        IReadOnlyList<MinigameRankEntry> result = MinigameInfo.LastResult;
        if(result == null || order < 1 || order > result.Count) return false;

        entry = result[order - 1];
        return true;
    }


    /// <summary>その枠(0 = 1P)の人の順位。結果に載っていない場合は-1を返す</summary>
    public static int GetRankByOrder(int slotIndex)
    {
        IReadOnlyList<MinigameRankEntry> result = MinigameInfo.LastResult;
        if (result == null) return -1;

        foreach(MinigameRankEntry entry in result)
        {
            if(entry.SlotIndex == slotIndex) return entry.Rank;
        }
        return -1;
    }


    public static List<MinigameRankEntry> GetByRank(int rank)
    {
        List<MinigameRankEntry> list = new List<MinigameRankEntry>();
        IReadOnlyList<MinigameRankEntry> result = MinigameInfo.LastResult;
        if (result == null) return list;


        foreach (MinigameRankEntry entry in result)
        {
            if (entry.Rank == rank) list.Add(entry);
        }
        return list;
    }


    public static int GetLocalPlayerRank()
    {
        if (!MinigameInfo.TryGetLocalPlayer(out MinigamePlayerState player)) return -1;
        return GetRankByOrder(player.SlotIndex);
    }
}
