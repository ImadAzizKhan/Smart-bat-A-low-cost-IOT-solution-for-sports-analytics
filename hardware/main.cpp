#include <Arduino.h>
#include <Adafruit_BNO08x.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <WiFiManager.h>

// --- Network Settings ---

// We will calculate the broadcast IP dynamically based on the network
IPAddress broadcastIP;
const int targetPort = 5005; //Unity port
const int PYTHON_IMU_PORT = 5004; // New port for the Python script
const int MOTOR_PIN = 15; 
const int LISTEN_PORT = 4210;
unsigned long lastUdpTime = 0;

WiFiUDP udp;
Adafruit_BNO08x bno08x;

// --- Global Memory for Sensor Data ---
float qX = 0, qY = 0, qZ = 0, qW = 1;
float aX = 0, aY = 0, aZ = 0;
float gX = 0, gY = 0, gZ = 0;

// --- Non-Blocking Haptic State ---
unsigned long lastMotorTime = 0;
bool motorActive = false;

// --- WiFi Recovery State ---
unsigned long lastWiFiCheck = 0;

void setup() {
  Serial.begin(115200);
  while (!Serial) delay(10);



  pinMode(MOTOR_PIN, OUTPUT);
  digitalWrite(MOTOR_PIN, LOW);

  // --- 1. MAGIC PLUG-AND-PLAY WIFI ---
  WiFiManager wifiManager;
  
  // Uncomment the line below ONCE if you ever need to wipe the bat's memory to test the portal
  // wifiManager.resetSettings(); 

  Serial.println("Starting SwingLab Network Manager...");
  
  // This is the magic. It tries to connect to the last saved WiFi.
  // If it fails (or is in a new location), it turns the bat into a hotspot named "AuraCoach_Setup"
  if (!wifiManager.autoConnect("SwingLab_Setup")) {
    Serial.println("Failed to connect and hit timeout. Restarting...");
    delay(3000);
    ESP.restart();
  }

  // --- 2. CALCULATE UDP BROADCAST ---
  // We made it here, which means we are connected to the router/laptop hotspot!
  Serial.println("\nWiFi Connected!");
  Serial.print("Bat IP: ");
  Serial.println(WiFi.localIP());

  // // Connect to Wi-Fi
  // WiFi.mode(WIFI_STA);
  // WiFi.setSleep(false);
  // WiFi.begin(ssid, password);


  // new
  // while (WiFi.status() != WL_CONNECTED) {
  //   delay(500);
  //   Serial.print(".");
  // }
  
  // Calculate the broadcast IP for whatever network we joined!
  broadcastIP = WiFi.localIP();
  broadcastIP[3] = 255; // e.g., turns 192.168.1.45 into 192.168.1.255
  
  Serial.println("\nConnected!");
  Serial.print("Broadcasting to: ");
  Serial.println(broadcastIP);
  // End new

  
  // Serial.print("Connecting to Wi-Fi");
  // while (WiFi.status() != WL_CONNECTED) {
  //   delay(500);
  //   Serial.print(".");
  // }
  Serial.println("\nWi-Fi Connected!");
  udp.begin(LISTEN_PORT); 

  // Start the sensor
  if (!bno08x.begin_I2C()) {
    Serial.println("ERROR: Could not find the BNO085 sensor.");
    while (1) { delay(10); } 
  }
  
  bno08x.enableReport(SH2_GAME_ROTATION_VECTOR);
  bno08x.enableReport(SH2_LINEAR_ACCELERATION);
  bno08x.enableReport(SH2_GYROSCOPE_CALIBRATED);
  
  Serial.println("All sensors enabled. Broadcasting UDP...");
  Serial.println(WiFi.localIP());
}

void loop() {
  // 1. NON-BLOCKING WIFI RECOVERY
  if (WiFi.status() != WL_CONNECTED) {
    if (millis() - lastWiFiCheck > 5000) { // Check every 5 seconds
      Serial.println("WiFi lost! ESP32 will auto-reconnect...");
      
      // If you want to force the setup portal to open again when WiFi is lost:
      // WiFiManager wm;
      // wm.setConfigPortalTimeout(120); // Open portal for 2 mins, then resume loop
      // wm.startConfigPortal("AuraCoach_Setup"); 
      
      lastWiFiCheck = millis();
    }
  }

  // 2. SENSOR READING & UDP BROADCAST
  if (bno08x.wasReset()) {
    bno08x.enableReport(SH2_GAME_ROTATION_VECTOR, 10000); 
    bno08x.enableReport(SH2_LINEAR_ACCELERATION, 10000);
    bno08x.enableReport(SH2_GYROSCOPE_CALIBRATED, 10000);
  }

  sh2_SensorValue_t sensorValue;
  if (bno08x.getSensorEvent(&sensorValue)) {
    
    // Update Accel (Stored in memory)
    if (sensorValue.sensorId == SH2_LINEAR_ACCELERATION) {                 // ← FIXED
      aX = sensorValue.un.linearAcceleration.x;                           // ← FIXED
      aY = sensorValue.un.linearAcceleration.y;                           // ← FIXED
      aZ = sensorValue.un.linearAcceleration.z;                           // ← FIXED
    }
    // Update Gyro (Stored in memory)
    else if (sensorValue.sensorId == SH2_GYROSCOPE_CALIBRATED) {
      gX = sensorValue.un.gyroscope.x;
      gY = sensorValue.un.gyroscope.y;
      gZ = sensorValue.un.gyroscope.z;
    }
    // Update Rotation & FIRE PACKET
    else if (sensorValue.sensorId == SH2_GAME_ROTATION_VECTOR) {
      qX = sensorValue.un.gameRotationVector.i;
      qY = sensorValue.un.gameRotationVector.j;
      qZ = sensorValue.un.gameRotationVector.k;
      qW = sensorValue.un.gameRotationVector.real;
    }
  }
    // 3. THE FIX: CONTINUOUS UDP BROADCAST (100Hz)


  if (WiFi.status() == WL_CONNECTED) {
    if (millis() - lastUdpTime >= 10) {
        lastUdpTime = millis();
        
        char dataString[128];
        snprintf(dataString, sizeof(dataString), 
                 "%.4f,%.4f,%.4f,%.4f,%.2f,%.2f,%.2f,%.2f,%.2f,%.2f", 
                 qX, qY, qZ, qW, aX, aY, aZ, gX, gY, gZ);
                            
        // Fire to Unity (Rotation)
        udp.beginPacket(broadcastIP, targetPort);
        udp.print(dataString);
        udp.endPacket();

        // Fire to Python (IMU Position Math)
        udp.beginPacket(broadcastIP, PYTHON_IMU_PORT);
        udp.print(dataString);
        udp.endPacket();
    }
  }

  // 3. NON-BLOCKING UDP RECEIVE FOR HAPTICS
  int packetSize = udp.parsePacket();
  if (packetSize) {
    char incomingPacket[256];
    int len = udp.read(incomingPacket, 255);
    
    // Prevent Buffer Overflow
    if (len > 0 && len < 255) {
      incomingPacket[len] = '\0'; // Null-terminate
      
      String msg = String(incomingPacket);
      if (msg.indexOf("HIT") >= 0) {
        motorActive = true;
        lastMotorTime = millis();
        digitalWrite(MOTOR_PIN, HIGH); // Turn motor ON
      }
    }
  }

  // 4. NON-BLOCKING HAPTIC TIMER (Turns motor off after 150ms)
  if (motorActive && (millis() - lastMotorTime > 150)) {
    digitalWrite(MOTOR_PIN, LOW); // Turn motor OFF
    motorActive = false;
  }
}
