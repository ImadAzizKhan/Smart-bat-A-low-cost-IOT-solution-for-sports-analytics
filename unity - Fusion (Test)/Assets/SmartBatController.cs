	using UnityEngine;

public class SmartBatController : MonoBehaviour
{
    [Header("Sensors")]
    public UDPReceiver rotationSensor;
    public PositionReceiver positionSensor;

    [Header("Play Area Settings")]
    public Vector3 creasePosition = new Vector3(0f, 1.2f, 0f); 
    public Vector3 movementScale = new Vector3(1.0f, 1.0f, 1.0f); 
    
    public float smoothingSpeed = 15f;
    


    [Header("Axis Corrections (Check if backwards)")]
    public bool invertPositionX = false;
    public bool invertPositionY = false;  // Turned off by default to fix MediaPipe's upside-down Y
    public bool invertPositionZ = true;  // Turned ON by default to fix your forward/backward

    public bool invertRotationX = false;
    public bool invertRotationY = true;  // Turned ON by default to fix your left/right twist
    public bool invertRotationZ = false;

    private Rigidbody rb;
    private Vector3 positionVelocity = Vector3.zero;

    [Header("Calibration Offsets")]
    private Quaternion rotationOffset = Quaternion.identity;
    private Vector3 positionOffset = Vector3.zero;
    private bool isCalibrated = false;

    // Warning timer taake console flood na ho
    private float warningTimer = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // 1. Force link to the surviving Singletons!
        if (UDPReceiver.Instance != null) rotationSensor = UDPReceiver.Instance;
        if (PositionReceiver.Instance != null) positionSensor = PositionReceiver.Instance;

        // INITIAL HEALTH CHECK
        if (rotationSensor == null) Debug.LogError("❌ ALARM: ESP32 Rotation Sensor (UDPReceiver) is MISSING or Singleton failed!");
        if (positionSensor == null) Debug.LogWarning("⚠️ WARNING: Python Camera Sensor (PositionReceiver) is MISSING or Singleton failed!");
    }

    void Update()
    {
        // Spacebar applies the "Tare" function to both sensors at once
        if (Input.GetKeyDown(KeyCode.Space) || (positionSensor != null && positionSensor.wantsTare))
        {
            Calibrate();
            if (positionSensor != null) positionSensor.wantsTare = false; // Reset the flag
        }

	// CONTINUOUS HEALTH CHECK (Har 3 second baad batayega)
        warningTimer += Time.deltaTime;
        if (warningTimer > 3f)
        {
	    // FIX: If it is null OR default zero, trigger the alarm!
            bool espNoData = (rotationSensor == null || rotationSensor.currentRotation == Quaternion.identity);
            bool pythonNoData = (positionSensor == null || positionSensor.currentPosition == Vector3.zero);
            
            if (espNoData) 
            {
                Debug.LogError("❌ ESP32 NO DATA: Script connected hai, par data nahi aa raha! IP Address aur Port (5005) check karein.");
            }
            else if (pythonNoData)
            {
                Debug.LogWarning("⚠️ PYTHON NO DATA: Camera data nahi bhej raha. IP Address aur Port (5006) check karein.");
            }
            else if (!isCalibrated) 
            {
                Debug.LogWarning("⏳ ALL GOOD: Data aa raha hai. Bat use karne ke liye SPACEBAR dabayen!");
            }
            
            warningTimer = 0f;
        }
    }

    void FixedUpdate()
    {
        // 1. ROTATION (Mandatory: If no ESP32, do nothing)
        if (rotationSensor == null) return; 

        Quaternion rawRotation = Quaternion.Inverse(rotationOffset) * rotationSensor.currentRotation;
        Vector3 euler = rawRotation.eulerAngles;
        if (invertRotationX) euler.x = -euler.x;
        if (invertRotationY) euler.y = -euler.y;
        if (invertRotationZ) euler.z = -euler.z;
        
        Quaternion finalRotation = Quaternion.Euler(euler);
        rb.MoveRotation(finalRotation);

        // 2. POSITION (Optional: Only run this if the webcam is actually connected)
        if (positionSensor != null && isCalibrated) 
        {
            Vector3 rawStep = positionSensor.currentPosition - positionOffset;
            
            if (invertPositionX) rawStep.x = -rawStep.x;
            if (invertPositionY) rawStep.y = -rawStep.y;
            if (invertPositionZ) rawStep.z = -rawStep.z;

            Vector3 targetPosition = creasePosition + new Vector3(
        rawStep.x * movementScale.x,
        rawStep.y * movementScale.y,
        rawStep.z * movementScale.z
    );

    // NAYA JADOO: SmoothDamp replaces Lerp! (Lower number = faster/snappier)
    float smoothTime = 0.02f; 
    rb.MovePosition(Vector3.SmoothDamp(rb.position, targetPosition, ref positionVelocity, smoothTime));
        }
        else if (positionSensor == null && isCalibrated) 
        {
            // FALLBACK: If webcam is dead/missing, lock the bat securely to the starting crease!
            rb.MovePosition(creasePosition);
        }
    }

    void Calibrate()
{
    if (rotationSensor != null) rotationOffset = rotationSensor.currentRotation;
    if (positionSensor != null) positionOffset = positionSensor.currentPosition;
    
    isCalibrated = true;
    Debug.Log("Bat Calibrated! Sensors locked.");
}
}