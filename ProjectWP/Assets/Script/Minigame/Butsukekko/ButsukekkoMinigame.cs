using UnityEngine;
/// <summary>
/// みんなでぶつけっこ : ステージのボールを拾って相手に投げる。
/// 当てた人に1点。30秒で一番点が高い人の勝ち
/// </summary>
public class ButsukekkoMinigame : MinigameBase
{
   // このミニゲームの種類(MinigameType に追加済みの Butsukekko を返す)
    public override MinigameType Type => MinigameType.Butsukekko;
}
