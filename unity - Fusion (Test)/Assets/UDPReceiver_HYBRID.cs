using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Globalization;

public class UDPReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 5005;
    
    [Header("Rotation (Always Available)")]
    public Quaternion currentRotation = Quaternion.identity;

    [Header("Raw Sensor Data (NEW HYBRID MODE)")]
    public Vector3 rawAcceleration = Vector3.zero;    // Sensor frame, with gravity
    public Vector3 rawGyroscope = Vector3.zero;        // rad/s
    
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = true;
    public static string espIPAddress = "";
    
    [HideInInspector] public int lastPacketFormat = 0; // 7 = old, 10 = new hybrid

    void Start()
    {
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
        Debug.Log("Listening for ESP32 IMU on port " + port);
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
                espIPAddress = anyIP.Address.ToString();
                
                string text = Encoding.UTF8.GetString(data);
                string[] parts = text.Split(',');

                // NEW HYBRID FORMAT: 10 values
                // "qX,qY,qZ,qW,aX,aY,aZ,gX,gY,gZ"
                if (parts.Length >= 10)
                {
                    float qX = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float qY = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float qZ = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float qW = float.Parse(parts[3], CultureInfo.InvariantCulture);

                    float aX = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float aY = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    float aZ = float.Parse(parts[6], CultureInfo.InvariantCulture);

                    float gX = float.Parse(parts[7], CultureInfo.InvariantCulture);
                    float gY = float.Parse(parts[8], CultureInfo.InvariantCulture);
                    float gZ = float.Parse(parts[9], CultureInfo.InvariantCulture);

                    currentRotation = new Quaternion(qX, qY, qZ, qW);
                    rawAcceleration = new Vector3(aX, aY, aZ);
                    rawGyroscope = new Vector3(gX, gY, gZ);
                    
                    lastPacketFormat = 10;
                }
                // FALLBACK: OLD FORMAT (7 values)
                // "qX,qY,qZ,qW,aX,aY,aZ" (from old main.cpp)
                else if (parts.Length >= 7)
                {
                    float qX = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float qY = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float qZ = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float qW = float.Parse(parts[3], CultureInfo.InvariantCulture);

                    float aX = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float aY = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    float aZ = float.Parse(parts[6], CultureInfo.InvariantCulture);

                    currentRotation = new Quaternion(qX, qY, qZ, qW);
                    rawAcceleration = new Vector3(aX, aY, aZ);
                    
                    lastPacketFormat = 7;
                }
                // ULTRA-FALLBACK: Just quaternion (4 values)
                else if (parts.Length >= 4)
                {
                    float qX = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float qY = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float qZ = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float qW = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    
                    currentRotation = new Quaternion(qX, qY, qZ, qW);
                    lastPacketFormat = 4;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("UDP Receive Error: " + e.Message);
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (udpClient != null) udpClient.Close();
    }
}
