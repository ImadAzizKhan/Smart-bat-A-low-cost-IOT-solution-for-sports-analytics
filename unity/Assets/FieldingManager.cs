using UnityEngine;

public class FieldingManager : MonoBehaviour
{
    [Header("Fielding Personnel")]
    public GameObject wicketKeeper;
    public GameObject[] fielders; 

    [Header("Wicket Keeper Positions")]
    public Transform keeperClose;  // Spinners ke liye
    public Transform keeperMedium; // Medium Pacers ke liye
    public Transform keeperFar;    // Fast Bowlers ke liye

    [Header("Fielding Positions")]
    public Transform[] fieldPositions; 

    void Awake()
    {
        SetField();
    }

    public void SetField()
    {
        for (int i = 0; i < fielders.Length; i++)
        {
            if (i < fieldPositions.Length && fielders[i] != null && fieldPositions[i] != null)
            {
                fielders[i].transform.position = fieldPositions[i].position;
                
                // Aapka z=0 wala Masterstroke!
                Vector3 lookPos = new Vector3(0, fielders[i].transform.position.y, 0);
                fielders[i].transform.LookAt(lookPos);
            }
        }
    }

    // --- 3-TIER KEEPER LOGIC ---
    public void UpdateKeeperPosition(float bowlerSpeed)
    {
        if (wicketKeeper == null) return;

        Transform targetPos;

        if (bowlerSpeed > 50f) targetPos = keeperFar;           // Fast
        else if (bowlerSpeed > 30f) targetPos = keeperMedium;   // Medium
        else targetPos = keeperClose;                            // Spin

        if (targetPos != null)
        {
            wicketKeeper.transform.position = targetPos.position;
            wicketKeeper.transform.rotation = targetPos.rotation;
        }
    }
}