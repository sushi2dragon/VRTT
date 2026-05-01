using UnityEngine;

public class AIRacket : MonoBehaviour
{
    public Transform ball;
    public float moveSpeed = 4f;   // base speed
    public float reactionDelay = 0.1f; // smaller = faster reaction

    private Vector3 targetPos;
    private float timer;

    void Update()
    {
        if (!ball) return;

        // Delay reaction based on difficulty
        timer += Time.deltaTime;
        if (timer >= reactionDelay)
        {
            timer = 0f;
            targetPos = new Vector3(transform.position.x, ball.position.y, ball.position.z);
        }

        // Smoothly move toward the ball
        Vector3 newPos = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        transform.position = newPos;

        // Optional: rotate slightly toward the ball
        Vector3 dir = (ball.position - transform.position).normalized;
        transform.forward = Vector3.Lerp(transform.forward, dir, 5f * Time.deltaTime);
    }
}
