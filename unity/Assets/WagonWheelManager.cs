using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// Yeh list humare har shot ka data save karegi
[System.Serializable]
public class ShotData
{
    public float angle;
    public float distance;
    public int runs;
}

public class WagonWheelManager : MonoBehaviour
{
    public static WagonWheelManager Instance;

    [Header("Game World Settings")]
    public Transform batsmanPosition; // Scene mein batsman ki jagah
    public float maxGroundRadius = 80f; // Asli ground kitne meter ka hai (e.g. 80m boundary)

    [Header("UI Settings")]
    public GameObject uiLinePrefab; // Aapka patla sa UI Line prefab
    public RectTransform groundUIContainer; // UI mein ground wali tasweer
    public float maxUIRadius = 200f; // UI ground ka radius pixels mein (size check kar lein)

    public RectTransform uiBatOrigin;

    private List<ShotData> allShots = new List<ShotData>();

    void Awake()
    {
        Instance = this;
    }

    // Yeh function hum tab call karenge jab ball dead ho jayegi
    public void RecordShot(Vector3 finalBallPos, int runsScored)
    {
        // 1. Zameen (Flat) par vector banayen
        Vector3 dir = finalBallPos - batsmanPosition.position;
        dir.y = 0; 

        // 2. Distance calculate karein
        float distance = dir.magnitude;

        // 3. Angle calculate karein (Maan lein k Z-axis straight Bowler ki taraf hai)
        float angle = Vector3.SignedAngle(Vector3.forward, dir, Vector3.up);

        // Data save karein
        ShotData newShot = new ShotData { angle = angle, distance = distance, runs = runsScored };
        allShots.Add(newShot);

        // UI par line draw karein
        DrawLineOnUI(newShot);
    }

    void DrawLineOnUI(ShotData shot)
    {
        if (uiLinePrefab == null || groundUIContainer == null) return;

        // Nayi line banayen aur GroundUI ke andar rakhein
        GameObject lineObj = Instantiate(uiLinePrefab, groundUIContainer);
        RectTransform rect = lineObj.GetComponent<RectTransform>();
        Image img = lineObj.GetComponent<Image>();

        // Runs ke hisaab se color badlein (TV broadcast style)
        if (shot.runs == 6) img.color = Color.green; // Six = Green
        else if (shot.runs == 4) img.color = Color.blue; // Four = Blue
        else img.color = Color.white; // 1,2,3 runs = White

        // Line ko ghumayen (UI mein rotation ulta kaam karta hai isliye -angle hai)
        rect.localRotation = Quaternion.Euler(0, 0, -shot.angle);

        // Line ki lambai (Height) set karein math se
        float lengthRatio = shot.distance / maxGroundRadius;
        float uiLength = lengthRatio * maxUIRadius;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, uiLength);

        // Line ko exact ground ke centre (batsman crease) par rakhein
        rect.anchoredPosition = uiBatOrigin != null ? uiBatOrigin.anchoredPosition : Vector2.zero;
    }
}