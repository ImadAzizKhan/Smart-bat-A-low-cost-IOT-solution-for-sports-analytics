using UnityEngine;
using TMPro;

public class StanceValidator : MonoBehaviour
{
    public TextMeshProUGUI validationText; // UI Text assign karein
    public Transform playerBatOrBody; // Jo cheez Python se track ho rahi hai
    
    [Header("Crease Limits (Virtual)")]
    public float minZ = -1.5f; // Stumps ke paas
    public float maxZ = -0.5f; // Crease line
    public float minX = -0.5f; // Leg stump
    public float maxX = 0.5f;  // Off stump

    public bool isPositionValid = false;

    void Update()
    {
        // Check position
        Vector3 pos = playerBatOrBody.position;
        
        bool inZ = (pos.z >= minZ && pos.z <= maxZ);
        bool inX = (pos.x >= minX && pos.x <= maxX);

        if (inZ && inX)
        {
            isPositionValid = true;
            validationText.text = "<color=green>POSITION PERFECT!\nPress SPACE to Lock Stance</color>";
            validationText.gameObject.SetActive(true);
        }
        else
        {
            isPositionValid = false;
            validationText.gameObject.SetActive(true);

            // Point camera ke hisaab se directions!
            if (pos.z > maxZ) 
                validationText.text = "<color=red>Move Left (Towards Stumps)</color>";
            else if (pos.z < minZ) 
                validationText.text = "<color=red>Move Right (Towards Bowler)</color>";
            else if (pos.x > maxX) 
                validationText.text = "<color=red>Move Closer to Camera</color>";
            else if (pos.x < minX) 
                validationText.text = "<color=red>Move Away from Camera</color>";
        }
    }
}