using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;

public class GhostReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 5007; // Dedicated port for body tracking
    
    [Header("IK Targets (The Magnets)")]
    public Transform leftWristTarget;
    // You can add rightWristTarget, ankleTargets, etc. as you expand the Python script

    [Header("Movement Scaling")]
    public Vector3 movementScale = new Vector3(1f, 1f, 1f);
    public float smoothTime = 0.05f;

    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = true;

    // Thread Safety
    private Vector3 _stagedLeftWrist = Vector3.zero;
    private readonly object _lock = new object();
    private Vector3 leftWristVelocity = Vector3.zero;

    void Start()
    {
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
        Debug.Log("Ghost Coach listening for body IK on port " + port);
    }

    void Update()
    {
        if (leftWristTarget == null) return;

        Vector3 targetPos;
        lock (_lock)
        {
            targetPos = _stagedLeftWrist;
        }

        // Apply scale and map to Unity world space
        Vector3 finalPos = new Vector3(
            targetPos.x * movementScale.x,
            targetPos.y * movementScale.y,
            targetPos.z * movementScale.z
        );

        // Smoothly move the IK Magnet to the Python coordinates
        leftWristTarget.localPosition = Vector3.SmoothDamp(
            leftWristTarget.localPosition, 
            finalPos, 
            ref leftWristVelocity, 
            smoothTime
        );
    }

    void ReceiveData()
    {
        udpClient = new UdpClient(port);
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);
                string[] parts = text.Split(',');

                // Assuming Python sends: LeftWristX, LeftWristY, LeftWristZ
                if (parts.Length >= 3)
                {
                    float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[2], CultureInfo.InvariantCulture);

                    lock (_lock)
                    {
                        // Invert axes here if the Coach moves backwards relative to you
                        _stagedLeftWrist = new Vector3(-x, y, z);
                    }
                }
            }
            catch {}
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (udpClient != null) udpClient.Close();
    }
}