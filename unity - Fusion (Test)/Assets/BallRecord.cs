[System.Serializable]
public struct BallRecord
{
    
    public int ballNumber;
    public string bowlerName;
    public float deliverySpeed;
    public string outcome; // "Dot", "Boundary", "Wicket"
    
    // Future Hardware Data Slots
    public float batSwingSpeed;
    public float impactAngle;

    // --- NAYA DATA: TV ANALYTICS KE LIYE ---
    public UnityEngine.Vector3 pitchLocation; // Manhattan (Tappa kahan laga)
    public UnityEngine.Vector3 shotDirection; // Wagon Wheel (Shot kahan gaya)

    public bool didHitBat;
    public UnityEngine.Vector2 batLocalHitPosition; // Bat ke upar nishan kahan laga
    public string intendedShot;
}