using UnityEngine;

using System.Net.Sockets;
using System.Text;



public class BallTracker : MonoBehaviour
{
    [Header("Boundary Settings")]
    public float boundaryRadius = 70f;
    public bool touchedByFielder = false;

    [Header("Wide Ball Settings")]
    public float wideThresholdX = 1.5f;
    public float creaseZ = 0.5f;

    private bool hasBounced = false;
    private bool hasScored = false;
    private bool hasStopped = false;
    public bool hasHitBat = false;
    private bool isWideCalled = false;
    private bool actionCameraTriggered = false;

    private float maxYReached = 0f;
    private Rigidbody rb;
    private Vector3 centerPos3D = new Vector3(0f, 0f, 10f);

    // --- NAYE VARIABLES ANALYTICS KE LIYE ---
    [HideInInspector] public Vector3 firstBouncePos;
    [HideInInspector] public Vector3 ballShotVelocity;
    [HideInInspector] public Vector2 batHitPos = Vector2.zero; // BUG FIX: Isay yahan upar rakhna zaroori hai!

    private void SendHapticToESP()
{
    // Agar IP abhi tak receive nahi hua, toh return kar jao
    if (string.IsNullOrEmpty(UDPReceiver.espIPAddress)) return; 

    try
    {
        UdpClient client = new UdpClient();
        // Automatically connect to whoever is sending the rotation data!
        client.Connect(UDPReceiver.espIPAddress, 4210); 
        byte[] sendBytes = System.Text.Encoding.UTF8.GetBytes("HIT");
        client.Send(sendBytes, sendBytes.Length);
        client.Close();
    }
    catch (System.Exception e) { Debug.Log("Haptic Error: " + e.Message); }
}

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            if (!hasBounced) 
            {
                hasBounced = true;
                firstBouncePos = transform.position; // Tappay ki 3D location save ho gayi!
                Debug.Log($"📍 Tappa Record Hua: X:{firstBouncePos.x:F2}, Z:{firstBouncePos.z:F2}");
            }
        }
        else if (collision.transform.root.CompareTag("Fielder") || collision.transform.root.CompareTag("WicketKeeper"))
        {
            if (!hasStopped)
            {
                hasStopped = true;
                touchedByFielder = true;
                if (rb != null) rb.linearVelocity = Vector3.zero;

                Vector3 flatBallPos = new Vector3(transform.position.x, 0f, transform.position.z);
                Vector3 flatCenter  = new Vector3(centerPos3D.x, 0f, centerPos3D.z);
                float distance = Vector3.Distance(flatBallPos, flatCenter);

                int runsToGive = 0;
                if (distance > 45f)      runsToGive = 2;
                else if (distance > 20f) runsToGive = 1;
                else                     runsToGive = 0;

                string outcomeStr = runsToGive > 0 ? $"{runsToGive} Runs" : "Dot Ball";

                if (CricketMatchManager.Instance != null) 
                {
                    CricketMatchManager.Instance.AddScore(runsToGive);
                    // 1. DATA LOGGING: Ball fielder ne pakar li
                    CricketMatchManager.Instance.LogBallData(outcomeStr, 120f, firstBouncePos, ballShotVelocity, batHitPos);
                    CricketMatchManager.Instance.TriggerResetSequence();
                }

                Debug.Log($"🛑 FIELDED! Distance: {distance:F1}m | Runs Taken: {runsToGive}");
            }
        }
        else if (collision.transform.root.CompareTag("Bat"))
        {
            hasHitBat = true;
	    SendHapticToESP();

            // Bat lagne ke foran baad ball kis speed aur direction mein nikli
            if (rb != null) ballShotVelocity = rb.linearVelocity;
            
            // --- NAYA JADOO: EXACT HIT LOCATION ---
            ContactPoint contact = collision.GetContact(0);
            Vector3 worldHitPoint = contact.point;
            Vector3 localHitPoint = collision.transform.InverseTransformPoint(worldHitPoint);
            
            // Yahan se 'Vector2' hata diya kyunke humne isay upar declare kar diya hai
            batHitPos = new Vector2(localHitPoint.x, localHitPoint.y); 

            if (AudioManager.Instance != null) AudioManager.Instance.PlayBatCrack(transform.position);
            
            if (!actionCameraTriggered && CameraManager.Instance != null)
            {
                actionCameraTriggered = true;
                CameraManager.Instance.SwitchToActionDelayed(3.0f);
                Debug.Log("🎥 CRACK! Action Camera ON (Ball Hit)!");
            }
        }
        else if (collision.gameObject.CompareTag("Wicket") || collision.transform.root.CompareTag("Wicket"))
        {
            if (!hasStopped)
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlayStumpSmash(transform.position);
                Debug.Log("💥 OUT! CLEAN BOWLED! Direct hit lag gayi.");
                
                if (CricketMatchManager.Instance != null) 
                {
                    // 2. DATA LOGGING: Clean Bowled ho gaya!
                    CricketMatchManager.Instance.LogBallData("Wicket", 120f, firstBouncePos, ballShotVelocity, batHitPos);
                    CricketMatchManager.Instance.WicketDown();
                }
                
                hasStopped = true;
                if (rb != null) rb.linearVelocity = Vector3.zero;
            }
        }
    }

    void Update()
    {
        if (transform.position.y > maxYReached) maxYReached = transform.position.y;

        if (!actionCameraTriggered && !hasHitBat && transform.position.z <= creaseZ)
        {
            actionCameraTriggered = true;
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.SwitchToActionDelayed(0.5f);
                Debug.Log("🎥 MISSED! Action Camera tracking to Keeper.");
            }
        }

        if (hasScored || hasStopped) return;
        
        if (CricketMatchManager.Instance != null && CricketMatchManager.Instance.isWicketDownThisBall) return;

        Vector3 flatBallPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatCenter  = new Vector3(centerPos3D.x, 0f, centerPos3D.z);
        float distanceFromCenter = Vector3.Distance(flatBallPos, flatCenter);

        if (distanceFromCenter > boundaryRadius)
        {
            hasScored = true;
            Debug.Log($"🏏 BOUNDARY PAAR! Final Pos -> X: {transform.position.x:F2}, Z: {transform.position.z:F2}");
            if (AudioManager.Instance != null) AudioManager.Instance.PlayCrowdCheer(transform.position);

            if (CricketMatchManager.Instance != null)
            {
                string boundType = hasBounced ? "Four" : "Six";
                if (hasBounced) CricketMatchManager.Instance.AddScore(4);
                else            CricketMatchManager.Instance.AddScore(6);

                // 3. DATA LOGGING: Boundary lag gayi!
                CricketMatchManager.Instance.LogBallData(boundType, 120f, firstBouncePos, ballShotVelocity, batHitPos);
                CricketMatchManager.Instance.TriggerResetSequence();
            }

            if (rb != null) rb.linearVelocity = Vector3.zero;
            return;
        }

        if (hasBounced && rb != null && rb.linearVelocity.magnitude < 0.1f)
        {
            hasStopped = true;
            Debug.Log($"🛑 Ball Zameen Par Ruk Gayi! Max Height: {maxYReached:F2}m");

	    int runsToGive = 0;
            if (distanceFromCenter > 45f)      runsToGive = 3;
            else if (distanceFromCenter > 20f) runsToGive = 2;
            else if (distanceFromCenter > 10f) runsToGive = 1;
            else                               runsToGive = 0;

            string outcomeStr = runsToGive > 0 ? $"{runsToGive} Runs" : "Dot Ball";
            
            if (CricketMatchManager.Instance != null) 
            {
                if (runsToGive > 0) CricketMatchManager.Instance.AddScore(runsToGive);
                // 4. DATA LOGGING: Ball ground par khud ruk gayi (Dot ball)
                CricketMatchManager.Instance.LogBallData(outcomeStr, 120f, firstBouncePos, ballShotVelocity, batHitPos);
                CricketMatchManager.Instance.TriggerResetSequence();
            }
            return; 
        }

        if (!isWideCalled && !hasHitBat && transform.position.z <= creaseZ)
        {
            if (Mathf.Abs(transform.position.x) > wideThresholdX)
            {
                isWideCalled = true;
                Debug.Log($"🚨 WIDE BALL! Ball continue flying...");
                
                if (CricketMatchManager.Instance != null) CricketMatchManager.Instance.CallExtra(1, "WIDE BALL");
            }
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Vector3 center = new Vector3(0f, 0f, 10f);
        Gizmos.DrawWireSphere(center, boundaryRadius);
    }
}