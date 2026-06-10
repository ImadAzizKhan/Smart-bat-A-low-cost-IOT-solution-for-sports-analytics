using UnityEngine;
using System.Collections;

public class SimpleFielderAI : MonoBehaviour
{
    [Header("Movement & Action Settings")]
    public float runSpeed = 8f;
    public float chaseRadius = 50f;
    public float stopDistance = 0.5f;
    public float throwForce = 18f; // Ball phenkne ki taqat
    public Transform handReleasePoint;

    private Transform targetBall;
    private BallTracker ballTracker;
    private Rigidbody ballRb;
    private Animator anim;
    
    private Vector3 startPosition;
    private Quaternion startRotation;
    
    // States
    private bool isThrowing = false;
    private bool hasFieldedThisBall = false;
    private bool isReturning = false; 

    private Transform wicketKeeper;

    void Start()
    {
        anim = GetComponent<Animator>();
        startPosition = transform.position;
        startRotation = transform.rotation;

        // Hierarchy se Wicket Keeper ko dhoondo taake usay ball throw ki ja sake
        GameObject keeper = GameObject.Find("WicketKeeper"); 
        if (keeper != null) wicketKeeper = keeper.transform;
    }

    void Update()
    {
        // 1. SCENE RESET / CAMERA CUT
        // Agar manager ne ball delete kar di hai, toh foran apni jagah par spawn ho jao
        if (targetBall == null)
        {
            GameObject ball = GameObject.FindGameObjectWithTag("Ball");
            if (ball != null) 
            {
                targetBall = ball.transform;
                ballTracker = ball.GetComponent<BallTracker>();
                ballRb = ball.GetComponent<Rigidbody>();
                
                // Nayi ball aane par saare locks reset
                hasFieldedThisBall = false;
                isThrowing = false;
                isReturning = false;
            }
            else
            {
                // Ball nahi mili = TV camera off ho chuka hai, instant teleport!
                transform.position = startPosition;
                transform.rotation = startRotation;
                if (anim != null) 
                {
                    anim.SetBool("isRunning", false);
                    anim.SetBool("isThrowing", false);
                }
            }
            return; 
        }

        // 2. THROW STATE LOCK
        if (isThrowing) return; 

        // 3. JOGGING BACK (Slow Return)
        if (isReturning)
        {
            Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatStart = new Vector3(startPosition.x, 0, startPosition.z);
            
            if (Vector3.Distance(flatPos, flatStart) > 0.5f)
            {
                Vector3 lookDir = flatStart - flatPos;
                if (lookDir != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 10f);

                transform.position = Vector3.MoveTowards(transform.position, startPosition, (runSpeed * 0.4f) * Time.deltaTime);
                if (anim != null) anim.SetBool("isRunning", true);
            }
            else
            {
                // Wapas pohnch gaya - FIX: Snap to position and turn off loop
                transform.position = startPosition; 
                transform.rotation = startRotation;
                if (anim != null) anim.SetBool("isRunning", false);
                
                isReturning = false; // YEH LINE MISSING THI! Ab yeh hamesha ke liye ruk jayega.
            }
            return; 
        }

        // 4. CHASE LOGIC
        if (hasFieldedThisBall) return;

        if (ballTracker != null && ballTracker.hasHitBat)
        {
            Vector3 flatFielderPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 flatBallPos = new Vector3(targetBall.position.x, 0, targetBall.position.z);
            float distToBall = Vector3.Distance(flatFielderPos, flatBallPos);

            if (distToBall < chaseRadius)
            {
                if (distToBall > stopDistance)
                {
                    Vector3 targetPos = new Vector3(targetBall.position.x, transform.position.y, targetBall.position.z);
                    transform.position = Vector3.MoveTowards(transform.position, targetPos, runSpeed * Time.deltaTime);

                    Vector3 lookDir = flatBallPos - flatFielderPos;
                    lookDir.y = 0;
                    if (lookDir != Vector3.zero)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 15f);

                    if (anim != null) anim.SetBool("isRunning", true);
                }
                else
                {
                    // Ball ke paas pohnch gaya
                    if (anim != null) anim.SetBool("isRunning", false);

                    bool ballMovingTowardFielder = ballRb != null && 
    Vector3.Dot(ballRb.linearVelocity, transform.position - targetBall.position) > 0;

if (targetBall.position.y > 0.6f && targetBall.position.y < 2.5f && ballMovingTowardFielder)
    StartCoroutine(CatchBall());
else
    StartCoroutine(PickupAndThrow());
                }
            }
        }
    }

    IEnumerator CatchBall()
    {
        isThrowing = true; // Update loop block karne ke liye yahi lock use karenge

        // Ball ko mid-air mein freeze karo aur haath mein chipka do
        if (ballRb != null) 
        {
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
            ballRb.isKinematic = true;
            
            // Fielder ke haath mein ball snap ho jayegi!
            if (handReleasePoint != null) ballRb.position = handReleasePoint.position;
        }
        if (ballTracker != null) ballTracker.touchedByFielder = true; // OUT mark karne ke liye!

        // Catch Animation chalao (Animator mein isCatching ka naya Bool banana padega)
        if (anim != null)
        {
            anim.SetBool("isRunning", false);
            anim.SetBool("isCatching", true); 
        }

        // Catching pose ko 1.5 seconds hold karne do
        yield return new WaitForSeconds(1.5f);

        if (anim != null) anim.SetBool("isCatching", false);
        
        isThrowing = false;
        hasFieldedThisBall = true;
        isReturning = true; // Wapas jog karna shuru karo
    }

    IEnumerator PickupAndThrow()
    {
        isThrowing = true;

        // Ball ko pakar kar freeze karo
        if (ballRb != null) 
        {
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
            ballRb.isKinematic = true; // Physics off taake ball fielder ke paas ruki rahe
        }
        if (ballTracker != null) ballTracker.touchedByFielder = true;

        // Throw karne se pehle Wicket Keeper ki taraf ghoomo
        if (wicketKeeper != null)
        {
            Vector3 lookDir = wicketKeeper.position - transform.position;
            lookDir.y = 0;
            transform.rotation = Quaternion.LookRotation(lookDir);
        }

        // Throw animation on
        if (anim != null)
        {
            anim.SetBool("isRunning", false);
            anim.SetBool("isThrowing", true);
        }

        // Animation ko almost pura chalne do (1.99 second), jab tak haath aagay aaye
        yield return new WaitForSeconds(1.99f);

        // --- THE PHYSICAL THROW ---
        if (ballRb != null && wicketKeeper != null)
        {
            ballRb.isKinematic = false; // Physics wapas on!

	    // 2. SNAP TO HAND: Place ball exactly at the HandReleasePoint
            if (handReleasePoint != null)
            {
                ballRb.position = handReleasePoint.position;
            }
            else
            {
                // Fallback just in case you forget to assign it in the Inspector
                ballRb.position = transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;
            }

            // 3. DYNAMIC SPEED (Distance ke hisaab se)
            float distanceToKeeper = Vector3.Distance(transform.position, wicketKeeper.position);
            
            // Formula: Distance ko 0.8 se multiply karein. (Minimum 10 speed, Maximum 40 speed)
            float dynamicForce = Mathf.Clamp(distanceToKeeper * 0.8f, 10f, 40f);
            
            // Wicket keeper ki taraf arc (trajectory) banao
            Vector3 throwDir = (wicketKeeper.position - ballRb.position);
            throwDir.y += distanceToKeeper * 0.05f;
            
            ballRb.linearVelocity = throwDir.normalized * dynamicForce;
        }

        // Baqi ki animation poori hone do (1 second)
        yield return new WaitForSeconds(0.01f);

        if (anim != null) anim.SetBool("isThrowing", false);
        
        // Saari processing ke baad isay end mein true karna jaisa aapne bola
        isThrowing = false;
        hasFieldedThisBall = true; 
        
        // Fielder ko wapas jog karne ki state mein daal do
        isReturning = true; 
    }
}