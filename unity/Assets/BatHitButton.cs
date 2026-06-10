using UnityEngine;
using UnityEngine.UI;

public class BatHitButton : MonoBehaviour
{
    public Button myButton; // Inspector mein apna button yahan drag karein

    void OnTriggerEnter(Collider other)
    {
        // Agar takrane wali cheez ka naam "Bat" hai (Make sure aapke VR bat ka tag ya naam set ho)
        if (other.CompareTag("Bat"))
        {
            // Aise behave karo jaise mouse se click hua hai!
            if (myButton != null)
            {
                myButton.onClick.Invoke();
            }
        }
    }
}