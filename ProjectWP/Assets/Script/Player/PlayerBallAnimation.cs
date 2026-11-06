using UnityEngine;

public class PlayerBallAnimation : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] Transform rigidHand;
    GameObject heldBall;
    Vector3 lastPos;

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        lastPos = transform.position;
    }


    private void Update()
    {
        // 移動速度をAnimatorに渡す(地面方向の移動のみ)
        Vector3 delta = transform.position - lastPos;
        delta.y = 0;
        animator.SetFloat("Speed", delta.magnitude / Time.deltaTime);
        lastPos = transform.position;


        // 例：左クリックで投げる
        if (heldBall != null && Input.GetMouseButtonDown(0))
        {
            animator.SetTrigger("Throw");
        }
    }


    // ボールを拾った時に呼ぶ
    public void PickUp(GameObject ball)
    {
        heldBall = ball;
        var rb = ball.GetComponent<Rigidbody>();
        if(rb) rb.isKinematic = true;
        ball.transform.SetParent(rigidHand);
        ball.transform.localPosition = Vector3.zero;
        animator.SetBool("IsHolding", true);
    }


    // 実際にボールを手から離す(投げる瞬間に呼ぶ)
    public void ReleseBall()
    {
        if (heldBall == null) return;
        heldBall.transform.SetParent(null);
        var rb = heldBall.GetComponent<Rigidbody>();
        if(rb)
        {
            rb.isKinematic = false;
            rb.AddForce(transform.forward * 10f + Vector3.up * 3f, ForceMode.Impulse); 
        }
        heldBall = null;
        animator.SetBool("IsHolding", false);
    }
}
