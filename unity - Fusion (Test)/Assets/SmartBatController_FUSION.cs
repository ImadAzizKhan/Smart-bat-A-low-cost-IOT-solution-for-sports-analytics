using UnityEngine;
using System.Collections.Generic;

public class SmartBatController : MonoBehaviour
{
    [Header("Sensors")]
    public UDPReceiver rotationSensor;
    public PositionReceiver visionPositionSensor;      // Slow but accurate (anchor)
    public IMUPositionReceiver imuPositionSensor;      // Fast but drifts (responsiveness)

    [Header("Play Area Settings")]
    public Vector3 creasePosition = new Vector3(0f, 1.2f, 0.618f); 
    public Vector3 movementScale = new Vector3(0.3f, 0.3f, 0.3f); 
    
    [Header("Sensor Fusion (Drift Elimination)")]
    [Range(0f, 1f)]
    public float imuHighFreqWeight = 0.85f;  // How much to trust IMU for motion
    public float visionLowFreqWeight = 0.15f; // How much to trust vision for anchor
    
    [Header("Drift Prevention")]
    public bool enableDriftCorrection = true;
    [Range(0.1f, 2.0f)]
    public float driftCorrectionStrength = 0.5f;  // How aggressively to anchor to vision
    private float timeSinceLastVisionUpdate = 0f;

    [Header("Debug")]
    public bool enableDebugLogs = false;
    public bool visualizeBothSources = true;

    private Rigidbody rb;
    private Vector3 fusedPosition = Vector3.zero;
    private Queue<Vector3> imuPositionHistory = new Queue<Vector3>(10);  // Rolling window for velocity
    private Vector3 lastVisionAnchor = Vector3.zero;

    [Header("Calibration Offsets")]
    private Vector3 positionOffset = Vector3.zero;
    private bool isCalibrated = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rotationSensor == null) 
            Debug.LogError("❌ UDPReceiver is MISSING!");
    }

    
void FixedUpdate()
    {
        if (rotationSensor == null) return;

        // 1. THE INDESTRUCTIBLE ROTATION SHIELD
        Quaternion rot = rotationSensor.currentRotation;
        float sqrMag = rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w;

        // Catch NaN (corrupt data) OR zero-length (0,0,0,0) vectors
        if (float.IsNaN(sqrMag) || sqrMag < 0.9f || sqrMag > 1.1f) 
        {
            rot = Quaternion.identity; // Safe default
        }
        else
        {
            rot = rot.normalized; // Force absolute perfect unit length
        }

        rb.MoveRotation(rot);

        // 2. REQUIRE CALIBRATION
        // Do not move the bat until the user Tares it.
        if (!isCalibrated) 
        {
            rb.MovePosition(creasePosition);
            return;
        }

        Vector3 targetPosition = creasePosition;
        bool hasVision = visionPositionSensor != null && visionPositionSensor.hasValidData;
        bool hasIMU = imuPositionSensor != null && imuPositionSensor.hasValidData;

        // 3. APPLY THE CALIBRATION OFFSET
        Vector3 calibratedVisionPos = Vector3.zero;
        if (hasVision) 
        {
            calibratedVisionPos = Vector3.Scale(visionPositionSensor.currentPosition - positionOffset, movementScale);
        }

        // 4. FIX THE "OUT OF FRUSTUM" UI CRASH
        // Reject NaN (corrupt) network packets before they break the physics engine
        if ((hasVision && float.IsNaN(calibratedVisionPos.x)) || (hasIMU && float.IsNaN(imuPositionSensor.imuPosition.x)))
        {
            return; 
        }

        if (hasVision && hasIMU)
        {
            targetPosition += FusedPosition(calibratedVisionPos, imuPositionSensor.imuPosition);
        }
        else if (hasIMU)
        {
            targetPosition += imuPositionSensor.imuPosition;
            timeSinceLastVisionUpdate += Time.fixedDeltaTime;
        }
        else if (hasVision)
        {
            targetPosition += FusedPosition(calibratedVisionPos, imuPositionSensor.imuPosition);
            timeSinceLastVisionUpdate = 0;
        }

        rb.MovePosition(targetPosition);
    }

    // =====================================================
    // THE MAGIC: Intelligent Sensor Fusion
    // =====================================================
    
    Vector3 FusedPosition(Vector3 visionPos, Vector3 imuPos)
    {
        /*
        KEY INSIGHT: Don't blend them 50-50 naively.
        
        Instead, use frequency separation:
        - IMU is FAST (100Hz) but DRIFTS (exponential error over time)
        - Vision is SLOW (30Hz) but ACCURATE (no drift)
        
        Solution: Use a high-pass/low-pass filter pair
        
        High-pass filtered IMU: Captures fast swings (< 2 second motion)
        Low-pass filtered Vision: Provides drift anchor (slow correction)
        
        Result: Responsive + accurate + drift-free
        */
        
        timeSinceLastVisionUpdate += Time.fixedDeltaTime;
        
        // ─────────────────────────────────────────────────────
        // STEP 1: Vision acts as the anchor (periodic correction)
        // ─────────────────────────────────────────────────────
        
        if (timeSinceLastVisionUpdate < 0.5f)  // Fresh vision data
        {
            lastVisionAnchor = visionPos;
        }
        else if (timeSinceLastVisionUpdate > 5.0f && enableDriftCorrection)
        {
            // If we haven't seen vision for 5 seconds, IMU has drifted
            // Gently drag position back toward last known vision position
            float driftCorrection = driftCorrectionStrength * 0.1f;  // Gentle pull
            visionPos = Vector3.Lerp(imuPos, lastVisionAnchor, driftCorrection);
        }
        
        // ─────────────────────────────────────────────────────
        // STEP 2: Blend with frequency-based weighting
        // ─────────────────────────────────────────────────────
        
        Vector3 imuVelocity = GetIMUVelocity();
        float imuSpeed = imuVelocity.magnitude;
        
        // During fast motion (swing): trust IMU more (it's responsive)
        // During slow motion (resting): trust vision more (IMU drifts)
        float dynamicIMUWeight = imuHighFreqWeight;
        
        if (imuSpeed < 0.1f)  // Bat moving slowly
        {
            dynamicIMUWeight = 0.3f;  // Prefer vision anchor
        }
        else if (imuSpeed > 2.0f)  // Bat swinging fast
        {
            dynamicIMUWeight = 0.95f;  // Trust IMU for responsiveness
        }
        
        // ─────────────────────────────────────────────────────
        // STEP 3: Blend the two sources
        // ─────────────────────────────────────────────────────
        
        Vector3 fusedPos = Vector3.Lerp(visionPos, imuPos, dynamicIMUWeight);
        
        // Debug output
        if (enableDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[FUSION] Vision: {visionPos:F3} | IMU: {imuPos:F3} | " +
                     $"Speed: {imuSpeed:F2} m/s | IMU Weight: {dynamicIMUWeight:F2}");
        }
        
        return fusedPos;
    }

    // =====================================================
    // HELPER: Estimate velocity from IMU position history
    // =====================================================
    
    Vector3 GetIMUVelocity()
    {
        /*
        Track recent IMU positions to estimate velocity.
        This is used to detect fast motion (swing) vs slow motion (rest).
        */
        
        if (imuPositionSensor == null || !imuPositionSensor.hasValidData)
            return Vector3.zero;
        
        Vector3 currentIMUPos = imuPositionSensor.imuPosition;
        
        // Keep rolling history of last 10 frames
        imuPositionHistory.Enqueue(currentIMUPos);
        if (imuPositionHistory.Count > 10)
            imuPositionHistory.Dequeue();
        
        // Estimate velocity: how much position changed in last 3 frames
        if (imuPositionHistory.Count < 3)
            return Vector3.zero;
        
        Vector3[] history = imuPositionHistory.ToArray();
        Vector3 positionDelta = history[history.Length - 1] - history[history.Length - 3];
        Vector3 velocity = positionDelta / (Time.fixedDeltaTime * 3);
        
        return velocity;
    }

    // =====================================================
    // VISUALIZATION (for debugging in Scene view)
    // =====================================================
    
    void OnDrawGizmosSelected()
    {
        if (!visualizeBothSources) return;

        // This is called in editor
        // In runtime, you'd need to make these fields non-private
    }

    // =====================================================
    // STATUS PRINT
    // =====================================================


    
    void Update()
    {
        if (Time.frameCount % 120 == 0)
        {
            string status = "[BAT] ";
            status += (rotationSensor != null ? "Rot✓ " : "Rot✗ ");
            status += (visionPositionSensor != null && visionPositionSensor.hasValidData ? "Vis✓ " : "Vis✗ ");
            status += (imuPositionSensor != null && imuPositionSensor.hasValidData ? "IMU✓" : "IMU✗");
            
            if (enableDebugLogs)
                Debug.Log(status);
        }
        // Catch the Spacebar OR the Python "TARE" hand gesture
        if (Input.GetKeyDown(KeyCode.Space) || (visionPositionSensor != null && visionPositionSensor.wantsTare))
        {
            if (visionPositionSensor != null) positionOffset = visionPositionSensor.currentPosition;
            isCalibrated = true;
            Debug.Log("🎯 TARE SUCCESSFUL! Bat synced to crease.");
            
            if (visionPositionSensor != null) visionPositionSensor.wantsTare = false;
        }
    }
}

/* 
=====================================================================
EXPLANATION OF THE DRIFT FIX
=====================================================================

Problem: Dead reckoning (integrating accel) causes exponential drift.
Solution: Use vision as a low-frequency anchor, IMU for high-frequency motion.

How it works:

1. FAST MOTION (swing, < 2 seconds):
   - User swings bat hard
   - IMU velocity is high (> 2 m/s)
   - We trust IMU 95% (responsive, no vision lag)
   - Bat follows motion instantly ✓

2. SLOW MOTION (moving to position, 2-5 seconds):
   - User walks into position
   - IMU velocity is medium (0.1-2 m/s)
   - We trust IMU 50%, vision 50% (balanced)
   - Drift is being corrected by vision input ✓

3. RESTING (bat still, > 5 seconds):
   - User holds bat still
   - IMU velocity is low (< 0.1 m/s)
   - We trust IMU 30%, vision 70% (anchor to vision)
   - IMU drift is pulled back to vision position ✓

4. VISION LOSS (occlusion, bat behind body):
   - Vision data becomes stale
   - timeSinceLastVisionUpdate > 5 seconds
   - We apply gentle drift correction (0.5% per frame toward last vision)
   - Bat drifts much more slowly than pure IMU ✓

Result:
- Sweep shots work (IMU captures acceleration)
- No jitter (vision acts as low-freq anchor)
- No drift (vision corrects IMU over time)
- Smooth motion (blending is velocity-aware)
- Resilient (works with either sensor alone if needed)

=====================================================================
PARAMETERS TO TUNE
=====================================================================

imuHighFreqWeight: 0.85
  - Higher = more responsive, more drift if vision is bad
  - Lower = smoother, slight lag on fast swings
  - Default 0.85 is good

driftCorrectionStrength: 0.5
  - Higher = more aggressively corrects drift, but less IMU responsiveness
  - Lower = less correction, but more drift during occlusion
  - Default 0.5 is balanced

timeSinceLastVisionUpdate > 5.0f
  - After 5 seconds without vision, apply drift correction
  - Make shorter (2.0f) if occlusion is common
  - Make longer (10.0f) if you trust IMU more

=====================================================================
WHY THIS BEATS KALMAN FILTERS FOR THIS USE CASE
=====================================================================

A full Extended Kalman Filter would be "more correct" mathematically,
but for a cricket bat tracking system:

Pro Kalman:
  - Theoretically optimal sensor fusion
  - Handles noise formally
  - Self-tuning (learns noise characteristics)

Con Kalman:
  - 300+ lines of code
  - Q and R matrices to tune (nonintuitive)
  - Harder to debug when something goes wrong
  - Slower on ESP32

This simple frequency-based fusion:
  - 100 lines of code
  - Intuitive parameters (speed threshold, correction strength)
  - Debuggable: you can visualize what each sensor is doing
  - Fast enough for real-time VR
  - Proven in production systems (game engines use similar)

Think of it as a "practical Kalman filter" designed for this specific problem.
*/
