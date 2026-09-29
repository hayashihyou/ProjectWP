using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ミニゲーム用のカメラを制御するスクリプト
/// </summary>
public class PartyCamera : MonoBehaviour
{
    [Header("画面に収めたいプレイヤーのTransform一覧")]
    [SerializeField] private List<Transform> players = new List<Transform>();

    [Header("カメラが最も寄れる距離")]
    [SerializeField] private float minDistance = 5f;

    [Header("カメラが最も離れる距離")]
    [SerializeField] private float maxDistance = 20f;

    [Header("プレイヤー同士の広がりに対して、どのくらい余白を持たせるか")]
    [SerializeField] private float padding = 3f;

    [Header("見下ろす角度(0で水平、90で真上から)")]
    [SerializeField] private float pitchAngle = 45f;

    [Header("カメラが目標位置に追いつく速さ")]
    [SerializeField] private float followSpeed = 5f;

    
    private void LateUpdate()
    {
        if(players.Count == 0) return;

        Vector3 center = CalculateCenter();
        float distance = CalculateDistance(center);

        MoveCamera(center, distance);
    }

    // 参加中の全プレイヤーの座標を平均し、中央点を求める
    private Vector3 CalculateCenter()
    {
        Vector3 sum = Vector3.zero;

        foreach(Transform player in players)
        {
            if(players == null) continue;
            sum += player.position;
        }


        return sum / players.Count;
    }


    private float CalculateDistance(Vector3 center)
    {
        float maxSpread = 0f;

        foreach(Transform player in players)
        {
            if (player == null) continue;

            float spread = Vector3.Distance(center, player.position);
            if(spread > maxSpread)
            {
                maxSpread = spread;
            }
        }

        float distance = maxSpread + padding;
        return Mathf.Clamp(distance, minDistance, maxDistance);
    }

    // 中心点・距離・角度から目標位置を計算し、カメラを滑らかに移動させる
    private void MoveCamera(Vector3 center , float distance)
    {
        //　見下ろし角度ぶん回転させた「カメラから見た後ろ方向」を求める
        Quaternion angleRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
        Vector3 offsetDirection = angleRotation * Vector3.back;

        Vector3 targetPosition = center + offsetDirection * distance;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            followSpeed * Time.deltaTime
            );

        transform.LookAt(center);
    }


    // ミニゲーム参加時にプレイヤーを追加する
    public void AddPlayer(Transform player)
    {
        if(!players.Contains(player))
        {
            players.Add(player);
        }
    }

    //　脱落・退出時にプレイヤーを除外する
    public void RemovePlayer(Transform player)
    {
        players.Remove(player);
    }
}
