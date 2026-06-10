using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(LineRenderer))]
public class BatPointer : MonoBehaviour
{
    [Header("Laser Settings")]
    public Transform laserSpawnPoint; // NEW: The empty point at the toe
    public float laserLength = 10f;
    public float dwellTimeToClick = 2.0f;
    
    private LineRenderer lineRenderer;
    private float currentDwellTime = 0f;
    private GameObject currentTarget = null;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    void Update()
    {
        // Use the new spawn point if assigned, otherwise fallback to the bat
        Transform origin = laserSpawnPoint != null ? laserSpawnPoint : transform;

        // 1. Draw the start of the laser exactly at the toe
        lineRenderer.SetPosition(0, origin.position);

        // Shoot a raycast forward from the toe's blue arrow
        Ray ray = new Ray(origin.position, origin.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, laserLength))
        {
            // 2. Stop the laser where it hits something
            lineRenderer.SetPosition(1, hit.point);

            // 3. Check for UI Button clicks
            Button hitButton = hit.collider.GetComponent<Button>();

            if (hitButton != null)
            {
                if (currentTarget == hit.collider.gameObject)
                {
                    currentDwellTime += Time.deltaTime;
		    // --- NEW: Visual Fill Feedback ---
                    Image buttonImage = hitButton.GetComponent<Image>();
                    if (buttonImage != null)
                    {
                        buttonImage.fillAmount = currentDwellTime / dwellTimeToClick;
                    }
                    // ---------------------------------

                    if (currentDwellTime >= dwellTimeToClick)
                    {
                        hitButton.onClick.Invoke();
                        currentDwellTime = 0f;
			if (buttonImage != null) buttonImage.fillAmount = 0f; // Reset after click
                    }
                }
                else
                {
		    // Reset old button if we look away
                    if (currentTarget != null)
                    {
                        Image oldImage = currentTarget.GetComponent<Image>();
                        if (oldImage != null) oldImage.fillAmount = 0f;
                    }
                    currentTarget = hit.collider.gameObject;
                    currentDwellTime = 0f;
                }
            }
            else
            {
                ResetDwell();
            }
        }
        else
        {
            // Hit nothing, draw laser to max length
            lineRenderer.SetPosition(1, origin.position + origin.forward * laserLength);
            ResetDwell();
        }
    }

    void ResetDwell()
    {
        currentTarget = null;
        currentDwellTime = 0f;
    }

    public void SetLaserActive(bool isActive)
    {
        this.enabled = isActive; // Turns the raycast math on/off
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.enabled = isActive; // Turns the visual orange line on/off
    }
}