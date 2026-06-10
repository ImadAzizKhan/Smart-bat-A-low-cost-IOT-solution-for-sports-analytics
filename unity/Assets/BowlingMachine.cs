using UnityEngine;

public class BowlingMachine : MonoBehaviour
{
    public GameObject ballPrefab;
    public float bowlSpeed = 25f; // Adjust for faster/slower throws
    public float bowlInterval = 4f; // Time between balls

    private float timer;

    void Update()
    {
        timer += Time.deltaTime;

        // Fire a ball every few seconds
        if (timer >= bowlInterval)
        {
            BowlBall();
            timer = 0f;
        }
    }

    void BowlBall()
    {
        // Spawn the ball at the machine's location
        GameObject newBall = Instantiate(ballPrefab, transform.position, transform.rotation);
        
        // Grab the physics engine and push it forward
        Rigidbody rb = newBall.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = transform.forward * bowlSpeed;
        }

        // Destroy the ball after 10 seconds to keep the game running smoothly
        Destroy(newBall, 10f);
    }
}