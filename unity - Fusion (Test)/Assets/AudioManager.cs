using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Clips")]
    public AudioClip batCrackSound;
    public AudioClip stumpSmashSound;
    public AudioClip crowdCheerSound;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Yeh function 3D space mein us location par aawaz paida karke khud destroy kar dega
    public void PlayBatCrack(Vector3 hitPosition)
    {
        if (batCrackSound != null)
            AudioSource.PlayClipAtPoint(batCrackSound, hitPosition, 1.0f); // 1.0f is Full Volume
    }

    public void PlayStumpSmash(Vector3 hitPosition)
    {
        if (stumpSmashSound != null)
            AudioSource.PlayClipAtPoint(stumpSmashSound, hitPosition, 1.0f);
    }

    public void PlayCrowdCheer(Vector3 hitPosition)
    {
        if (crowdCheerSound != null)
            AudioSource.PlayClipAtPoint(crowdCheerSound, hitPosition, 0.7f); // Shor thora kam volume pe
    }
}