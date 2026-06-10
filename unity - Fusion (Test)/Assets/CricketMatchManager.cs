using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

[System.Serializable]
public class BowlerProfile
{
    public string bowlerName;
    public Transform startPoint;
    public float minSpeed;
    public float maxSpeed;
    public string animationStateName;
}

public enum MatchState 
{
    WaitingForBowler, 
    BowlerRunningUp,  
    BallInPlay,       
    BallDead,         
    MatchOver         
}

public class CricketMatchManager : MonoBehaviour
{
    // --- TIER 3: THE BULLETPROOF SINGLETON ---
    public static CricketMatchManager Instance { get; private set; }

    public MatchState currentState = MatchState.WaitingForBowler;
    public List<BallRecord> matchHistory = new List<BallRecord>();

    [Header("Match State")]
    public int totalOvers = 1;
    public int currentBall = 0;
    public int completedOvers = 0;
    public int runsScored = 0;
    public int wicketsFallen = 0;
    public int targetScore = 50; // Set by opponent's innings or practice target

    [HideInInspector] public bool isWicketDownThisBall = false;
    [HideInInspector] public bool isBatsmanSafe = false;

    [Header("UI Controls")]
    public TextMeshProUGUI scoreTextUI;
    public TextMeshProUGUI oversTextUI;
    public GameObject nextBallButton;
    public GameObject bowlerSelectionMenu;
    public TextMeshProUGUI coachFeedbackText;
    public Animator coachAnimator;
    public TextMeshProUGUI practiceShotText;

    [Header("Bowler Database")]
    public BowlerProfile[] bowlers;
    private int currentBowlerIndex = 0;

    [Header("Bowling Target & Prefabs")]
    public Transform targetWickets;
    public float lineVariation = 0.5f;
    public float lengthVariation = 2.0f;
    public GameObject ballPrefab;
    public Transform releasePoint;
    public GameObject wicketPrefab;
    public Transform wicketSpawnPoint;

    public enum PracticeShot { CoverDrive, Sweep, UpperCut }
    [Header("Practice Shot Selection")]    
    public PracticeShot currentPracticeShot = PracticeShot.CoverDrive;
    // Hook these up to the OnClick() events of your 3 new UI Buttons
    public void SelectCoverDrive() 
{ 
    currentPracticeShot = PracticeShot.CoverDrive; 
    if (practiceShotText != null) practiceShotText.text = "PRACTICING: COVER DRIVE";
}
public void SelectSweep()      
{ 
    currentPracticeShot = PracticeShot.Sweep; 
    if (practiceShotText != null) practiceShotText.text = "PRACTICING: SWEEP";
}
public void SelectUpperCut()   
{ 
    currentPracticeShot = PracticeShot.UpperCut; 
    if (practiceShotText != null) practiceShotText.text = "PRACTICING: UPPER CUT";
}


    // -----------------------------------------------------------------
    // COACH API
    // -----------------------------------------------------------------
    [Header("Coach Integration (ASE Project)")]
    [Tooltip("URL of your Flask app. e.g. http://localhost:5000/api/analyze")]
    public string coachApiUrl = "http://localhost:5000/api/analyze";
    public bool coachEnabled  = true;           // Toggle off if Flask app not running

    private GameObject currentWicketSet;
    private Vector3 initialWicketPos;
    private bool isResetting = false;
    private FieldingManager fieldingManager;

    void Awake()
    {
        // FIX 3: Singleton survives scene reloads properly
        if (Instance == null) 
        { 
            Instance = this; 
            DontDestroyOnLoad(gameObject); 
        }
        else 
        { 
            Destroy(gameObject); 
        }
    }

    void Start()
    {
        initialWicketPos = wicketSpawnPoint.position;
        fieldingManager = FindFirstObjectByType<FieldingManager>();

        UpdateScoreboard();

        if (bowlerSelectionMenu != null) bowlerSelectionMenu.SetActive(true);
        if (nextBallButton != null) nextBallButton.SetActive(true);

        SpawnWickets();
        ApplyBowlerProfile(0);
    }

    // YAHAN WOH NAAM GAYAB THA JO THEEK KAR DIYA GAYA HAI
    
    public void ApplyBowlerProfile(int index)
    {
        if (bowlers == null || bowlers.Length <= index) return;

        currentState = MatchState.WaitingForBowler;
        currentBowlerIndex = index;
        BowlerProfile profile = bowlers[index];

        Animator anim = GetComponent<Animator>();

        // --- THE MAGIC FIX: Animator ko completely reset karo ---
        if (anim != null)
        {
            anim.enabled = false;
            anim.Rebind(); // Yeh function purani movement ki memory completely wash kar deta hai!
        }

        // --- Position & Rotation Setup ---
        if (profile.startPoint != null && targetWickets != null)
        {
            transform.position = profile.startPoint.position;
            Vector3 lookDirection = (targetWickets.position - profile.startPoint.position).normalized;
            lookDirection.y = 0;
            transform.rotation = Quaternion.LookRotation(lookDirection);
        }

        // --- Animator ko wapas zinda karo aur frame 0 par lock karo ---
        if (anim != null)
        {
            anim.enabled = true;
            if (!string.IsNullOrEmpty(profile.animationStateName))
            {
                string safeAnimName = profile.animationStateName.Trim();
                anim.Play(safeAnimName, 0, 0f);
                anim.Update(0f); // Animator ko immediately first frame par snap karwao
            }
            anim.speed = 0f;
        }

        if (fieldingManager != null)
        {
            fieldingManager.UpdateKeeperPosition(profile.maxSpeed);
        }

        // --- NAYA CAMERA RESET ---
        // Nayi ball par wapas bowler ke peechay wale camera par aao
        if (CameraManager.Instance != null) 
        {
            CameraManager.Instance.SwitchToMain();
        }
    }

    public void OnPlayNextBallButtonClicked()
    {
        if (nextBallButton != null) nextBallButton.SetActive(false);
        isWicketDownThisBall = false;
	ClearCoachFeedback();

	// --- TURN LASER OFF ---
        BatPointer batLaser = Object.FindFirstObjectByType<BatPointer>();
        if (batLaser != null) batLaser.SetLaserActive(false);

        currentState = MatchState.BowlerRunningUp;

        Animator anim = GetComponent<Animator>();
        if (anim != null) anim.speed = 1f;

        currentBall++;
        UpdateScoreboard();
    }

    public void ReleaseBall()
    {
        if (ballPrefab == null || releasePoint == null || targetWickets == null) return;

        currentState = MatchState.BallInPlay;

        GameObject newBall = Instantiate(ballPrefab, releasePoint.position, releasePoint.rotation);
        Rigidbody rb = newBall.GetComponent<Rigidbody>();

        if (rb != null)
        {
            Vector3 randomTarget = targetWickets.position;

            // --- THE SHOT CALLER TARGETING ---
            switch (currentPracticeShot)
            {
                case PracticeShot.CoverDrive:
                    // Full length, outside off stump
                    randomTarget.x += Random.Range(0.2f, 0.8f); 
                    randomTarget.z += Random.Range(-2.5f, -1.0f); 
                    break;

                case PracticeShot.Sweep:
                    // Good/Full length, on the leg stump
                    randomTarget.x -= Random.Range(0.0f, 0.5f); 
                    randomTarget.z += Random.Range(-3.0f, -1.5f);
                    break;

                case PracticeShot.UpperCut:
                    // Short pitched (bouncer), wide outside off stump
                    randomTarget.x += Random.Range(0.8f, 1.5f); 
                    randomTarget.z += Random.Range(-6.0f, -4.0f);
                    break;
            }

            Vector3 fireDirection = (randomTarget - releasePoint.position).normalized;

            if (bowlers == null || bowlers.Length == 0 || currentBowlerIndex >= bowlers.Length)
            {
                Destroy(newBall);
                return;
            }

            float randomSpeed = Random.Range(bowlers[currentBowlerIndex].minSpeed, bowlers[currentBowlerIndex].maxSpeed);
            rb.linearVelocity = fireDirection * randomSpeed;

            int noBallChance = Random.Range(0, 100);
            if (noBallChance < 5)
            {
                CallExtra(1, "NO BALL (Overstepping)");
            }
        }
        Destroy(newBall, 20f);
    }

    public void UpdateScoreboard()
    {
        if (scoreTextUI != null) scoreTextUI.text = $"SCORE: {runsScored} - {wicketsFallen}";
        if (oversTextUI != null) oversTextUI.text = $"OVERS: {completedOvers}.{currentBall}";
    }

    public void AddScore(int runsToAdd)
    {
        runsScored += runsToAdd;
        UpdateScoreboard();
	GameObject ball = GameObject.FindGameObjectWithTag("Ball");
        if (ball != null)
        {
            if (WagonWheelManager.Instance != null)
            {
                WagonWheelManager.Instance.RecordShot(ball.transform.position, runsToAdd);
            }
        }
	if (targetScore > 0 && runsScored >= targetScore)
{
    currentState = MatchState.MatchOver;
    Debug.Log("TARGET CHASED! You WIN!");
}

    }

    public void CallExtra(int penaltyRuns, string extraType)
    {
        runsScored += penaltyRuns;
        if (currentBall > 0) currentBall--;
        Debug.Log($"🚨 {extraType}! {penaltyRuns} Run Added. Ball Reversed.");
        UpdateScoreboard();
    }

    public void WicketDown()
    {
        if (isWicketDownThisBall) return;
        isWicketDownThisBall = true;
        wicketsFallen++;
        UpdateScoreboard();
	
	// NEW: 10 wickets = innings over
    if (wicketsFallen >= 10)
    {
        TriggerResetSequence();
        currentState = MatchState.MatchOver;
        Debug.Log("ALL OUT! Match Over.");
        return;
    }

        TriggerResetSequence();
    }

    public void TriggerResetSequence()
    {
        if (!isResetting) StartCoroutine(ResetRoutine());
    }

    void SpawnWickets()
    {
        if (currentWicketSet != null) Destroy(currentWicketSet);
        Vector3 safeSpawnPos = initialWicketPos + new Vector3(0f, 0.05f, 0f);
        currentWicketSet = Instantiate(wicketPrefab, safeSpawnPos, wicketSpawnPoint.rotation);
    }

    IEnumerator ResetRoutine()
    {
        isResetting = true;
        currentState = MatchState.BallDead;
	yield return new WaitForSeconds(10.5f);
        // --- NAYA JADOO: SCORE COUNT HUA, TV CAMERA DIKHAO ---
        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.SwitchToBroadcast();
            Debug.Log("📺 TV Broadcast Angle ON for 5 seconds!");
            
            // YAHAN HUM BAAD MEIN SCORE KA POPUP (SIX! / FOUR!) SHOW KARENGE
        }

        Animator anim = GetComponent<Animator>();
        if (anim != null) anim.speed = 0f;

        yield return new WaitForSeconds(5f);

        GameObject[] oldBalls = GameObject.FindGameObjectsWithTag("Ball");
        foreach (GameObject ball in oldBalls) Destroy(ball);
	

        SpawnWickets();

        if (currentBall >= 6)
        {
            completedOvers++;
            currentBall = 0;
            UpdateScoreboard();

            if (completedOvers >= totalOvers)
            {
                currentState = MatchState.MatchOver;
                Debug.Log("MATCH FINISHED! Summary Screen Dikhao!");
                if (nextBallButton != null) nextBallButton.SetActive(false);
                if (bowlerSelectionMenu != null) bowlerSelectionMenu.SetActive(false);
            }
            else
            {
                // --- NAYI LINE: Camera wapas VR par lao taake menu nazar aaye! ---
                if (CameraManager.Instance != null) CameraManager.Instance.SwitchToMain();
                
                if (nextBallButton != null) nextBallButton.SetActive(false);
                if (bowlerSelectionMenu != null) bowlerSelectionMenu.SetActive(true);
		// --- TURN LASER BACK ON ---
                BatPointer batLaser = Object.FindFirstObjectByType<BatPointer>();
                if (batLaser != null) batLaser.SetLaserActive(true);
                Debug.Log("OVER KHATAM! Naya Bowler Select Karein.");
            }
        }
        else
        {
            ApplyBowlerProfile(currentBowlerIndex);
            if (nextBallButton != null) nextBallButton.SetActive(true);
	    // --- TURN LASER BACK ON ---
                BatPointer batLaser = Object.FindFirstObjectByType<BatPointer>();
                if (batLaser != null) batLaser.SetLaserActive(true);
        }

        isResetting = false;
    }

    public void SelectBowlerFromMenu(int index)
    {
        if (bowlerSelectionMenu != null) bowlerSelectionMenu.SetActive(false);
        ApplyBowlerProfile(index);
        if (nextBallButton != null) nextBallButton.SetActive(true);
    }

    
// NAYA FUNCTION: Ab yeh Tappa aur Bat Hit ki location bhi lega
    public void LogBallData(string outcome, float speed, Vector3 pitchPos, Vector3 shotDir, Vector2 batHitPos)
    {
        if (bowlers == null || bowlers.Length == 0 || currentBowlerIndex >= bowlers.Length) return;

        BallRecord newRecord = new BallRecord 
        {
            ballNumber = (completedOvers * 6) + currentBall,
            bowlerName = bowlers[currentBowlerIndex].bowlerName,
            deliverySpeed = speed,
            outcome = outcome,
            
            // TV ANALYTICS DATA
            pitchLocation = pitchPos,
            shotDirection = shotDir,
            batLocalHitPosition = batHitPos,
            didHitBat = (batHitPos != Vector2.zero), // Agar 0,0 nahi hai toh matlab bat laga hai
	    intendedShot = currentPracticeShot.ToString()
        };
        
        matchHistory.Add(newRecord);
        Debug.Log($"📊 STATS SAVED! Tappa: {pitchPos.z:F1}m, Shot Z: {shotDir.z:F1}");
	if (coachEnabled) StartCoroutine(SendToCoach(newRecord));
    }
    // =====================================================================
    // COACH APP INTEGRATION (ASE Project)
    // =====================================================================
    // FIX 2: coachFeedbackText is now a proper field (see UI section above).
    // FIX 3: coroutine is now called from LogBallData — no longer orphaned.
    //
    // HOW IT WORKS:
    //   Unity  -->  POST JSON  -->  Flask (appv4.py on localhost:5000)
    //   Flask  -->  analysis   -->  JSON response
    //   Unity  -->  shows feedback text in coachFeedbackText UI element
    //
    // TO USE:
    //   1. Run your Flask app:  python appv4.py
    //   2. Make sure coachApiUrl points to the right address
    //   3. Assign coachFeedbackText in Inspector
    //   4. Tick coachEnabled checkbox in Inspector
    // =====================================================================
    IEnumerator SendToCoach(BallRecord record)
    {
        // Serialize the ball record to JSON
        string json = JsonUtility.ToJson(record);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
 
        using UnityWebRequest req = new UnityWebRequest(coachApiUrl, "POST");
        req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
 
        yield return req.SendWebRequest();
 
        if (req.result == UnityWebRequest.Result.Success)
        {
            string feedback = req.downloadHandler.text;
            ShowCoachFeedback(feedback);
            Debug.Log($"🧑‍🏫 Coach says: {feedback}");
        }
        else
        {
            // Coach app not running — fail silently, don't crash the game
            Debug.LogWarning($"Coach API unreachable ({req.error}). " +
                              "Is appv4.py running on localhost:5000?");
        }
    }
 
    void ShowCoachFeedback(string feedback)
    {
        if (coachFeedbackText == null) return;
 
        try
        {
            CoachResponse parsed = JsonUtility.FromJson<CoachResponse>(feedback);
            coachFeedbackText.text = parsed.feedback ?? feedback;

            // --- THE GHOST COACH TRIGGER ---
            if (coachAnimator != null && parsed.feedback != null)
            {
                // If Python found an error, make the Coach demonstrate the right way!
                if (parsed.feedback.Contains("Stride") || parsed.feedback.Contains("elbow"))
                {
                    coachAnimator.SetTrigger("ShowCoverDrive");
                }
                // You can add 'else if' for the Sweep or Uppercut here later
            }
        }
        catch
        {
            coachFeedbackText.text = feedback;
        }
    }
 
    void ClearCoachFeedback()
    {
        if (coachFeedbackText != null)
            coachFeedbackText.text = "";
    }
 
    // =====================================================================
    // HELPER: JSON response shape from Flask
    // =====================================================================
    [System.Serializable]
    private class CoachResponse
    {
        public string feedback;
        public float  score;
        public string shot_type;
    }
}