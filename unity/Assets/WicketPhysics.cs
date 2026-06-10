using UnityEngine;

public class WicketPhysics : MonoBehaviour
{
    [Header("Wicket Settings")]
    public float minimumHitSpeed = 2.0f; 
    public float dramaticPopMultiplier = 1.5f;

    private Rigidbody rb;
    private bool isKnockedOut = false;
    private CricketMatchManager manager; // Manager ko yahan save kar liya

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true; 
        
        // WARNING FIX: Naya Unity function
        manager = FindFirstObjectByType<CricketMatchManager>(); 
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isKnockedOut) return; // Agar wicket pehle hi gir chuki hai toh kuch mat karo

        // 1. HIT WICKET: Agar batsman ka bat khud stumps par lag jaye
        if (collision.gameObject.CompareTag("Bat"))
        {
            isKnockedOut = true;
            Debug.Log("OUT! HIT WICKET! Bat stumps se takra gaya.");
            if (manager != null) manager.WicketDown();
            KnockOutPhysics(collision.relativeVelocity.normalized, collision.contacts[0].point, 5f);
            return;
        }

        // 2. BALL HITS WICKET
        if (collision.gameObject.CompareTag("Ball"))
        {
            float impactSpeed = collision.relativeVelocity.magnitude;

            if (impactSpeed >= minimumHitSpeed)
            {
                BallTracker tracker = collision.gameObject.GetComponent<BallTracker>();
                bool isRunOutAttempt = (tracker != null && tracker.touchedByFielder);

                if (isRunOutAttempt)
                {
                    // Fielder ne throw ki hai -> Crease Check Karo!
                    if (manager != null && manager.isBatsmanSafe)
                    {
                        Debug.Log("SAFE! Wicket toh giri lekin Batsman Crease ke andar tha.");
                        // Safe hai, toh WicketDown() call NAHI karenge.
                    }
                    else
                    {
                        Debug.Log("OUT! RUN OUT! Batsman Crease se bahar tha.");
                        if (manager != null) manager.WicketDown();
                        isKnockedOut = true;
                    }
                }
                else
                {
                    // Direct Bowler ki ball aayi hai -> CLEAN BOWLED! (Koi Crease Check nahi)
                    Debug.Log("OUT! CLEAN BOWLED! Direct hit lag gayi.");
                    if (manager != null) manager.WicketDown();
                    isKnockedOut = true;
                }

                // Wicket girne ki physical animation (Agar Out/Hit hua hai toh)
                KnockOutPhysics(collision.relativeVelocity.normalized, collision.contacts[0].point, impactSpeed);
            }
        }
    }

    // Physics ko alag function mein rakh diya taake code saaf rahay
    void KnockOutPhysics(Vector3 direction, Vector3 point, float speed)
    {
        rb.isKinematic = false;
        float forceToApply = speed * dramaticPopMultiplier;
        rb.AddForceAtPosition(direction * forceToApply, point, ForceMode.Impulse);
        
        FixedJoint joint = GetComponent<FixedJoint>();
        if (joint != null) Destroy(joint);
    }
}