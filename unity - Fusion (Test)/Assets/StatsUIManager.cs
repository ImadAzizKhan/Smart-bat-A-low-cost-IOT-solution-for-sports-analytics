using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class StatsUIManager : MonoBehaviour
{
    [Header("UI Areas (Canvas Images)")]
    public RectTransform pitchMapUI;    // Tappay wali 2D Tasweer
    public RectTransform wagonWheelUI;  // Ground ki 2D Tasweer
    public RectTransform batFaceUI;     // Bat ki 2D Tasweer

    [Header("Dot Prefab")]
    public GameObject redDotPrefab;     // Wo chota sa (🔴) Prefab

    [Header("Real World Scale (Adjust in Inspector)")]
    public float pitchLength3D = 20f;   // Pitch ki lambai Unity mein (meters)
    public float pitchWidth3D = 3f;     // Pitch ki chorai
    public float groundRadius3D = 70f;  // Boundary ka radius
    public float batWidthLocal = 0.1f;  // Bat ki asli width
    public float batHeightLocal = 0.5f; // Bat ki asli height

    // Yeh list un saare dots ko yaad rakhegi taake baad mein clear kar sakein
    private List<GameObject> spawnedDots = new List<GameObject>();

    public void DrawStats()
    {
        ClearStats(); 

        if (CricketMatchManager.Instance == null) return;

        foreach (BallRecord record in CricketMatchManager.Instance.matchHistory)
        {
            // 1. PITCH MAP (Manhattan)
            if (pitchMapUI != null && record.pitchLocation != Vector3.zero)
            {
                float xPct = record.pitchLocation.x / pitchWidth3D;
                float zPct = record.pitchLocation.z / pitchLength3D;
                DrawDot(pitchMapUI, xPct, zPct);
            }

            // 2. WAGON WHEEL (Ground - NAYA LOGIC)
            if (wagonWheelUI != null && record.shotDirection != Vector3.zero)
            {
                // Shot ki direction (velocity) ko normalize karke dot lagayen
                Vector3 dir = record.shotDirection.normalized;
                DrawDot(wagonWheelUI, dir.x, dir.z);
            }

            // 3. BAT HIT MARK (Fix)
            if (batFaceUI != null && record.didHitBat)
            {
                float xPct = record.batLocalHitPosition.x / batWidthLocal;
                float yPct = record.batLocalHitPosition.y / batHeightLocal;
                
                // BUG FIX: Value ko -1 aur 1 ke darmiyan lock karein taake UI se baher na jaye
                xPct = Mathf.Clamp(xPct, -1f, 1f);
                yPct = Mathf.Clamp(yPct, -1f, 1f);
                
                DrawDot(batFaceUI, xPct, yPct);
            }
        }
    }

    private void DrawDot(RectTransform parentUI, float percentX, float percentY)
    {
        GameObject newDot = Instantiate(redDotPrefab, parentUI);
        RectTransform dotRect = newDot.GetComponent<RectTransform>();

        // Center point se hisaab lagao
        float uiX = percentX * (parentUI.rect.width / 2f);
        float uiY = percentY * (parentUI.rect.height / 2f);

        dotRect.anchoredPosition = new Vector2(uiX, uiY);
        spawnedDots.Add(newDot);
    }

    public void ClearStats()
    {
        foreach (GameObject dot in spawnedDots) Destroy(dot);
        spawnedDots.Clear();
    }
}