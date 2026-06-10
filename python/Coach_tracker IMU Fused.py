import os
os.environ['TF_ENABLE_ONEDNN_OPTS'] = '0'

import json
import threading
import socket
import time
import math
import cv2
import joblib
import mediapipe as mp
import numpy as np
from flask import Flask, render_template, request, jsonify
from flask_socketio import SocketIO
from dataclasses import dataclass


sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# ================= 1. CONFIGURATION =================
@dataclass
class Config:
    # Unity Tracking Settings (From vision_tracker)
    UDP_IP: str = "Your Unity device IP"  # Sending to Unity on localhost
    UDP_PORT: int = 5006
    SCALE_FACTOR: float = 1.0  
    MIN_VISIBILITY: float = 0.45
    DEPTH_MULTIPLIER: float = 3.0
    MAX_RANGE: float = 3.0
    TARGET_FPS: float = 60.0
    SAFE_ZONE_MIN_X: float = 0.25
    SAFE_ZONE_MAX_X: float = 0.75

    # Filter Settings (From vision_tracker)
    MIN_CUTOFF: float = 0.8
    BETA: float = 0.5
    D_CUTOFF: float = 1.0

    # ESP32 & AI Settings (From appv4)
    ESP32_IP: str = "Your ESP IP"  # Update to your ESP32's actual IP
    ESP_PORT: int = 4210
    MODEL_FILE = r"D:\Personal Projects\VR Bat\cricket-pose-detection-analysis-main\CricShot10k_Dataset\CricShot10k_ Shot Dataset\Cricket_AI_Training\4_Model_Training\cricket_ai_model.pkl"
    META_FILE  = r"D:\Personal Projects\VR Bat\cricket-pose-detection-analysis-main\CricShot10k_Dataset\CricShot10k_ Shot Dataset\Cricket_AI_Training\4_Model_Training\model_meta.json"

config = Config()

# ============================================================================
# IMU MATH CLASSES (Madgwick & Integrator)
# ============================================================================
from collections import deque

class MadgwickFilter:
    def __init__(self, beta=1.0):
        self.beta = beta
        self.q = np.array([1.0, 0.0, 0.0, 0.0]) 
    
    def update(self, gx, gy, gz, ax, ay, az, dt):
        a_norm = np.linalg.norm([ax, ay, az])
        if a_norm == 0: return  
        ax /= a_norm; ay /= a_norm; az /= a_norm
        q0, q1, q2, q3 = self.q
        
        gx_est = 2 * (q1 * q3 - q0 * q2)
        gy_est = 2 * (q0 * q1 + q2 * q3)
        gz_est = q0*q0 - q1*q1 - q2*q2 + q3*q3
        
        ex = ay * gz_est - az * gy_est
        ey = az * gx_est - ax * gz_est
        ez = ax * gy_est - ay * gx_est
        
        gx += self.beta * ex; gy += self.beta * ey; gz += self.beta * ez
        
        dq = np.array([
            -q1*gx - q2*gy - q3*gz,
             q0*gx + q2*gz - q3*gy,
             q0*gy - q1*gz + q3*gx,
             q0*gz + q1*gy - q2*gx
        ]) * dt * 0.5
        self.q += dq
        self.q /= np.linalg.norm(self.q)
    
    def getRotationMatrix(self):
        q0, q1, q2, q3 = self.q  
        return np.array([
            [1 - 2*(q2*q2 + q3*q3),     2*(q1*q2 - q0*q3),     2*(q1*q3 + q0*q2)],
            [    2*(q1*q2 + q0*q3), 1 - 2*(q1*q1 + q3*q3),     2*(q2*q3 - q0*q1)],
            [    2*(q1*q3 - q0*q2),     2*(q2*q3 + q0*q1), 1 - 2*(q1*q1 + q2*q2)]
        ])

class PositionIntegrator:
    def __init__(self, velocity_damping=0.90):
        self.position = np.array([0.0, 0.0, 0.0])
        self.velocity = np.array([0.0, 0.0, 0.0])
        self.damping = velocity_damping
    
    def update(self, accel_world, dt):
        self.velocity *= self.damping
        self.velocity += accel_world * dt
        
        vel_mag = np.linalg.norm(self.velocity)
        if vel_mag > 10.0:
            self.velocity = (self.velocity / vel_mag) * 10.0
        
        self.position += self.velocity * dt
        
        pos_mag = np.linalg.norm(self.position)
        if pos_mag > 50.0:
            print(f"🚨 IMU POSITION EXPLODED: [{self.position[0]:.2f}, {self.position[1]:.2f}, {self.position[2]:.2f}] — RESETTING!")
            self.position = np.array([0.0, 0.0, 0.0])
            self.velocity = np.array([0.0, 0.0, 0.0])


# ================= 2. GLOBALS & INITIALIZATION =================
# Load Machine Learning Model
rf_model = joblib.load(config.MODEL_FILE)
with open(config.META_FILE) as f: 
    CLASS_NAMES = json.load(f)['classes']

# Initialize Flask & SocketIO
app = Flask(__name__)
socketio = SocketIO(app, cors_allowed_origins="*", async_mode='threading')

# Unified UDP Socket (For both Unity Pos and ESP32 Haptics)
udp_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
udp_sock.setblocking(False)

# MediaPipe Setup
mp_pose = mp.solutions.pose
pose = mp_pose.Pose(min_detection_confidence=0.6, min_tracking_confidence=0.6, model_complexity=1)
mp_draw = mp.solutions.drawing_utils

# HCI State Variables
_lock = threading.Lock()
_target_shot = "Cover Drive"
_eval_state = "idle"  
_user_baseline = None
_anim_start = 0
_is_left_handed = False
latest_angles_dict = {}



# ================= 3. UTILITIES & SMOOTHING =================

# Add to Utilities section:
def deadzone(v, dz=0.015):
    return 0 if abs(v) < dz else v

class StableLandmark:
    def __init__(self):
        self.last = None

    def update(self, lm, min_vis=0.5):
        if lm and lm.visibility > min_vis:
            self.last = lm
        return self.last

def get_landmark_if_visible(landmarks, landmark_id, min_vis):
    lm = landmarks[landmark_id.value]
    return lm if lm.visibility > min_vis else None

def clamp(value: float, limit: float) -> float:
    return max(-limit, min(limit, value))

def safe_send(data: bytes, ip, port):
    try:
        udp_sock.sendto(data, (ip, port))
    except OSError:
        pass

class OneEuroFilter:
    def __init__(self, mincutoff, beta, dcutoff):
        self.mincutoff = mincutoff
        self.beta = beta
        self.dcutoff = dcutoff
        self.x_prev = None
        self.dx_prev = 0.0
        self.t_prev = None

    def alpha(self, cutoff, dt):
        tau = 1.0 / (2 * math.pi * cutoff)
        return 1.0 / (1.0 + tau / dt)

    def __call__(self, t, x):
        if self.t_prev is None:
            self.x_prev, self.t_prev = x, t
            return x
        dt = t - self.t_prev
        if dt <= 0 or dt > 1.0:
            self.x_prev, self.dx_prev, self.t_prev = x, 0.0, t
            return x
        dx = (x - self.x_prev) / dt
        edx = self.dx_prev + self.alpha(self.dcutoff, dt) * (dx - self.dx_prev)
        cutoff = self.mincutoff + self.beta * abs(edx)
        a = self.alpha(cutoff, dt)
        hatx = self.x_prev + a * (x - self.x_prev)
        self.x_prev, self.dx_prev, self.t_prev = hatx, edx, t
        return hatx

class SmoothPosition3D:
    def __init__(self, cfg):
        self.fx = OneEuroFilter(cfg.MIN_CUTOFF, cfg.BETA, cfg.D_CUTOFF)
        self.fy = OneEuroFilter(cfg.MIN_CUTOFF, cfg.BETA, cfg.D_CUTOFF)
        self.fz = OneEuroFilter(cfg.MIN_CUTOFF, cfg.BETA, cfg.D_CUTOFF)

    def update(self, x, y, z):
        t = time.monotonic()
        return self.fx(t, x), self.fy(t, y), self.fz(t, z)

smoother = SmoothPosition3D(config)

class CalibrationState:
    def __init__(self):
        self.is_calibrated = False
        self.base_hip_x = self.base_torso_size = self.base_wrist_x = self.base_wrist_y = self.base_wrist_z = 0.0

    def calibrate(self, hip_x, torso_size, wrist_x, wrist_y, wrist_z):
        self.base_hip_x, self.base_torso_size = hip_x, torso_size
        self.base_wrist_x, self.base_wrist_y, self.base_wrist_z = wrist_x, wrist_y, wrist_z
        self.is_calibrated = True

calibration = CalibrationState()

# ================= 4. AI COACHING MATH =================
def mirror_angles(angles):
    return {
        'l_el': angles.get('r_el', 0), 'r_el': angles.get('l_el', 0),
        'l_knee': angles.get('r_knee', 0), 'r_knee': angles.get('l_knee', 0),
        'wrist_nose': angles.get('wrist_nose', 0),
        'stride_length': angles.get('stride_length', 0),
        'hands_gap': angles.get('hands_gap', 0),
    }

def set_target(shot): 
    global _target_shot; _target_shot = shot

def get_target(): 
    with _lock: return _target_shot

def joint_angle(a, b, c):
    a, b, c = np.array(a), np.array(b), np.array(c)
    v1, v2 = a - b, c - b
    cos_a = np.dot(v1, v2) / (np.linalg.norm(v1) * np.linalg.norm(v2) + 1e-9)
    return float(np.degrees(np.arccos(np.clip(cos_a, -1, 1))))

def dist(p1, p2): return float(np.sqrt((p1[0]-p2[0])**2 + (p1[1]-p2[1])**2))

def tilt_deg(l, r): return float(np.degrees(np.arctan2(r[1] - l[1], r[0] - l[0])))

def extract_feature_vector(lms):
    PL = mp.solutions.pose.PoseLandmark
    def pt(lm): return [lms.landmark[lm.value].x, lms.landmark[lm.value].y]
    r_sh, l_sh = pt(PL.RIGHT_SHOULDER), pt(PL.LEFT_SHOULDER)
    r_el, l_el = pt(PL.RIGHT_ELBOW), pt(PL.LEFT_ELBOW)
    r_wr, l_wr = pt(PL.RIGHT_WRIST), pt(PL.LEFT_WRIST)
    r_hip, l_hip = pt(PL.RIGHT_HIP), pt(PL.LEFT_HIP)
    r_knee, l_knee = pt(PL.RIGHT_KNEE), pt(PL.LEFT_KNEE)
    r_ank, l_ank = pt(PL.RIGHT_ANKLE), pt(PL.LEFT_ANKLE)
    nose = pt(PL.NOSE)

    mid_sh, mid_hip = [(r_sh[0]+l_sh[0])/2, (r_sh[1]+l_sh[1])/2], [(r_hip[0]+l_hip[0])/2, (r_hip[1]+l_hip[1])/2]
    
    frame_features = [
        joint_angle(r_sh, r_el, r_wr), joint_angle(l_sh, l_el, l_wr),
        joint_angle(r_hip, r_knee, r_ank), joint_angle(l_hip, l_knee, l_ank),
        dist(r_wr, nose)*100, dist(r_ank, l_ank)*100,
        dist(r_wr, r_hip)*100, dist(l_wr, l_hip)*100,
        tilt_deg(l_sh, r_sh), tilt_deg(l_hip, r_hip),
        tilt_deg(mid_hip, mid_sh), dist(mid_sh, mid_hip)*100,
    ]
    return np.array(frame_features * 3).reshape(1, -1)

def build_angle_dict(lms):
    PL = mp.solutions.pose.PoseLandmark
    def pt(lm): return [lms.landmark[lm.value].x, lms.landmark[lm.value].y]
    return {
        'r_el': joint_angle(pt(PL.RIGHT_SHOULDER), pt(PL.RIGHT_ELBOW), pt(PL.RIGHT_WRIST)),
        'l_el': joint_angle(pt(PL.LEFT_SHOULDER), pt(PL.LEFT_ELBOW), pt(PL.LEFT_WRIST)),
        'r_knee': joint_angle(pt(PL.RIGHT_HIP), pt(PL.RIGHT_KNEE), pt(PL.RIGHT_ANKLE)),
        'l_knee': joint_angle(pt(PL.LEFT_HIP), pt(PL.LEFT_KNEE), pt(PL.LEFT_ANKLE)),
        'wrist_nose': dist(pt(PL.RIGHT_WRIST), pt(PL.NOSE)) * 100,
        'stride_length': dist(pt(PL.RIGHT_HEEL), pt(PL.LEFT_HEEL)) * 100, 
        'hands_gap': dist(pt(PL.RIGHT_INDEX), pt(PL.LEFT_INDEX)) * 100,   
    }

_shots_cache = None
_shots_cache_time = 0

def load_coach_shots():
    global _shots_cache, _shots_cache_time
    if _shots_cache is not None and (time.time() - _shots_cache_time < 30):
        return _shots_cache

    import glob
    shots_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "shots_db")
    shots = {}
    for fp in glob.glob(os.path.join(shots_dir, "*.json")):
        try:
            with open(fp) as f:
                d = json.load(f)
            shots[d["name"]] = d.get("blocks", [])
        except Exception: pass
        
    _shots_cache = shots
    _shots_cache_time = time.time()
    return shots

def grade_coach_shot(angles, blocks):
    key_map = {
        "right_elbow": "r_el", "left_elbow": "l_el", "right_knee": "r_knee",
        "left_knee": "l_knee", "wrist_nose": "wrist_nose", "leg_dist": "stride_length",
        "body_lean": "body_lean", "shoulder_tilt":"shoulder_tilt",
    }
    errors, failed_keys = [], [] 
    for b in blocks:
        if b.get("type") == "action": continue
        key = key_map.get(b["key"], b["key"])
        val = float(b.get("val", 0))
        label = b.get("label", b["key"])
        unit = b.get("unit", "")
        measured = angles.get(key)
        if measured is None: continue
        
        if b.get("type") == "tilt":
            if abs(measured - val) > 10:
                errors.append(f"{label} should be ~{val}{unit} (yours: {int(measured)}{unit})")
        else:
            op = b.get("op", ">")
            if op == ">" and measured < val:
                errors.append(f"{label} must be > {val}{unit} (yours: {int(measured)}{unit})")
                failed_keys.append(b["key"])
            elif op == "<" and measured > val: 
                errors.append(f"{label} must be < {val}{unit} (yours: {int(measured)}{unit})")
    return errors, failed_keys

def send_haptic(pattern="BUZZ"):
    try:
        udp_sock.sendto(pattern.encode(), (config.ESP32_IP, config.ESP_PORT))
    except: pass

def grade_shot(shot_name, angles):
    global latest_angles_dict
    latest_angles_dict = angles
    errors, failed_keys = [], []

    if _is_left_handed:
        angles = mirror_angles(angles)

    coach_shots = load_coach_shots()
    if shot_name in coach_shots: return grade_coach_shot(angles, coach_shots[shot_name])

    if angles['hands_gap'] > 12: 
        errors.append("Keep your hands closer together on the bat handle."); failed_keys.append("hands_gap")
        
    if shot_name == "Cover Drive":
        if angles['l_el'] < 120: errors.append("Keep your front elbow pointing high."); failed_keys.append("l_el")
        if angles['l_knee'] > 160: errors.append("Bend your front knee to lean your weight forward."); failed_keys.append("l_knee")
        if angles['stride_length'] < 15: errors.append("Take a bigger stride toward the pitch of the ball."); failed_keys.append("stride_length")
    elif shot_name == "Sweep":
        if angles['r_knee'] > 110: errors.append("Get lower! Drop your back knee to the ground."); failed_keys.append("r_knee")
        if angles['l_el'] < 90: errors.append("Extend your arms fully through the sweep."); failed_keys.append("l_el")
    elif shot_name == "Upper Cut":
        if angles['wrist_nose'] < 15: errors.append("Wait for it. Play it late over your shoulder."); failed_keys.append("wrist_nose")
        if angles['r_el'] > 150: errors.append("Keep your elbows slightly bent to guide the ball."); failed_keys.append("r_el")
        
    return errors, failed_keys


# ================= 5. DRAWING & VISUALS =================
def extract_ghost_dict(lms):
    PL = mp.solutions.pose.PoseLandmark
    def pt(lm): return [lms.landmark[lm.value].x, lms.landmark[lm.value].y]
    return {
        "head": pt(PL.NOSE), "r_sh": pt(PL.RIGHT_SHOULDER), "l_sh": pt(PL.LEFT_SHOULDER),
        "r_el": pt(PL.RIGHT_ELBOW), "l_el": pt(PL.LEFT_ELBOW), "r_wr": pt(PL.RIGHT_WRIST), "l_wr": pt(PL.LEFT_WRIST),
        "r_hip": pt(PL.RIGHT_HIP), "l_hip": pt(PL.LEFT_HIP), "r_knee": pt(PL.RIGHT_KNEE), "l_knee": pt(PL.LEFT_KNEE),
        "r_ank": pt(PL.RIGHT_ANKLE), "l_ank": pt(PL.LEFT_ANKLE),
        "r_heel": pt(PL.RIGHT_HEEL), "l_heel": pt(PL.LEFT_HEEL),
        "r_toe": pt(PL.RIGHT_FOOT_INDEX), "l_toe": pt(PL.LEFT_FOOT_INDEX),
        "r_index": pt(PL.RIGHT_INDEX), "l_index": pt(PL.LEFT_INDEX)
    }

TARGET_POSES = {
    "Stance": { "head": [0.5, 0.25], "r_sh": [0.45, 0.35], "l_sh": [0.55, 0.35], "r_el": [0.45, 0.45], "l_el": [0.6, 0.45], "r_wr": [0.45, 0.55], "l_wr": [0.55, 0.55], "r_hip": [0.48, 0.6], "l_hip": [0.52, 0.6], "r_knee": [0.48, 0.75], "l_knee": [0.52, 0.75], "r_ank": [0.48, 0.9], "l_ank": [0.52, 0.9], "r_heel": [0.46, 0.92], "l_heel": [0.54, 0.92], "r_toe": [0.5, 0.95], "l_toe": [0.5, 0.95], "r_index": [0.45, 0.58], "l_index": [0.55, 0.58] },
    "Cover Drive": { "head": [0.45, 0.25], "r_sh": [0.4, 0.35], "l_sh": [0.55, 0.35], "r_el": [0.35, 0.45], "l_el": [0.65, 0.25], "r_wr": [0.4, 0.55], "l_wr": [0.5, 0.45], "r_hip": [0.45, 0.6], "l_hip": [0.55, 0.6], "r_knee": [0.4, 0.75], "l_knee": [0.65, 0.7], "r_ank": [0.4, 0.9], "l_ank": [0.7, 0.9], "r_heel": [0.38, 0.92], "l_heel": [0.72, 0.92], "r_toe": [0.42, 0.95], "l_toe": [0.68, 0.95], "r_index": [0.42, 0.57], "l_index": [0.48, 0.47] },
    "Sweep": { "head": [0.5, 0.45], "r_sh": [0.4, 0.55], "l_sh": [0.6, 0.55], "r_el": [0.35, 0.6], "l_el": [0.7, 0.55], "r_wr": [0.3, 0.65], "l_wr": [0.8, 0.65], "r_hip": [0.45, 0.7], "l_hip": [0.55, 0.7], "r_knee": [0.45, 0.9], "l_knee": [0.7, 0.8], "r_ank": [0.4, 0.95], "l_ank": [0.7, 0.95], "r_heel": [0.38, 0.97], "l_heel": [0.72, 0.9], "r_toe": [0.42, 0.98], "l_toe": [0.68, 0.98], "r_index": [0.28, 0.66], "l_index": [0.78, 0.66] },
    "Upper Cut": { "head": [0.55, 0.25], "r_sh": [0.45, 0.35], "l_sh": [0.6, 0.35], "r_el": [0.5, 0.45], "l_el": [0.7, 0.2], "r_wr": [0.6, 0.15], "l_wr": [0.7, 0.15], "r_hip": [0.5, 0.6], "l_hip": [0.55, 0.6], "r_knee": [0.5, 0.75], "l_knee": [0.65, 0.75], "r_ank": [0.5, 0.9], "l_ank": [0.65, 0.9], "r_heel": [0.48, 0.92], "l_heel": [0.68, 0.92], "r_toe": [0.52, 0.95], "l_toe": [0.62, 0.95], "r_index": [0.62, 0.13], "l_index": [0.68, 0.13] }
}

def draw_ghost(img, pose_dict, color=(0,229,160), thickness=4):
    h, w = img.shape[:2]
    overlay = img.copy()
    glow = img.copy()
    def line(j1, j2):
        if j1 not in pose_dict or j2 not in pose_dict: return
        p1 = (int(pose_dict[j1][0]*w), int(pose_dict[j1][1]*h))
        p2 = (int(pose_dict[j2][0]*w), int(pose_dict[j2][1]*h))
        cv2.line(glow, p1, p2, color, thickness*5)   
        cv2.line(overlay, p1, p2, color, thickness)  
        
    line("head", "r_sh"); line("head", "l_sh"); line("r_sh", "l_sh"); line("r_sh", "r_hip"); line("l_sh", "l_hip"); line("r_hip", "l_hip")
    line("r_sh", "r_el"); line("r_el", "r_wr"); line("l_sh", "l_el"); line("l_el", "l_wr")
    line("r_hip", "r_knee"); line("r_knee", "r_ank"); line("l_hip", "l_knee"); line("l_knee", "l_ank")

    for joint, coords in pose_dict.items():
        px = (int(coords[0]*w), int(coords[1]*h))
        cv2.circle(glow, px, 6, color, -1)
        cv2.circle(overlay, px, 8, color, 1)

    cv2.addWeighted(glow, 0.15, img, 0.85, 0, img)
    cv2.addWeighted(overlay, 0.75, img, 0.25, 0, img)

def interpolate_ghost(start_pose, end_pose, progress):
    current = {}
    for joint in start_pose.keys():
        x = start_pose[joint][0] + (end_pose[joint][0] - start_pose[joint][0]) * progress
        y = start_pose[joint][1] + (end_pose[joint][1] - start_pose[joint][1]) * progress
        current[joint] = [x, y]
    return current



def is_fully_in_frame(lms):
    PL = mp.solutions.pose.PoseLandmark
    l_ank = lms.landmark[PL.LEFT_ANKLE.value]
    r_ank = lms.landmark[PL.RIGHT_ANKLE.value]
    if l_ank.visibility < 0.5 and r_ank.visibility < 0.5: return False
        
    l_wr = lms.landmark[PL.LEFT_WRIST.value]
    r_wr = lms.landmark[PL.RIGHT_WRIST.value]
    if l_wr.visibility < 0.5 or r_wr.visibility < 0.5: return False
        
    return True

def draw_crease(img):
    """
    Renders the Pitch/Crease to match your Unity setup:
    Wickets on Right, Crease on Left.
    """
    h, w = img.shape[:2]
    # Crease (Left side)
    cv2.line(img, (int(w*0.25), int(h*0.85)), (int(w*0.25), int(h*0.95)), (255,255,255), 3)
    # Stumps (Right side)
    stump_x = int(w*0.75)
    cv2.line(img, (stump_x, int(h*0.85)), (stump_x, int(h*0.95)), (255,220,100), 5) # Changed 0.4/0.6 to 0.85/0.95
    cv2.putText(img, "CREASE", (int(w*0.22), int(h*0.8)), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255,255,255), 2)
    cv2.putText(img, "STUMPS", (int(w*0.72), int(h*0.8)), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255,220,100), 2) # Changed 0.25 to 0.8

# ================= 6. MASTER CAMERA LOOP =================
def run_camera():
    global _eval_state, _user_baseline, _anim_start
    global sock
    cap = cv2.VideoCapture(0)
    
    # Force 720p Resolution
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
    
    # Create a nice big window!
    cv2.namedWindow("Master Coach Studio & VR Tracker", cv2.WINDOW_NORMAL)
    cv2.resizeWindow("Master Coach Studio & VR Tracker", 1280, 720)
    
    tare_timer_start = None
    last_emit = 0

    trk_wrist_2d_stable = StableLandmark()
    trk_wrist_3d_stable = StableLandmark()
    prev_time = time.time()
    prev_sz = 0.0

    while True:
        ret, img = cap.read()
        if not ret: break

        # img = cv2.rotate(img, cv2.ROTATE_90_CLOCKWISE)
        img = cv2.flip(img, 1) 
        img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
        result = pose.process(img_rgb)
        target = get_target()
        
        status_text = "VR Tracker: Waiting..."
        
        draw_crease(img)

        if result.pose_landmarks and result.pose_world_landmarks:
            lm = result.pose_landmarks.landmark
            
            # The 6 Critical IK Targets for Unity:
            # 15: Left Wrist, 16: Right Wrist
            # 23: Left Hip, 24: Right Hip (We can average these for the Pelvis)
            # 27: Left Ankle, 28: Right Ankle
            
            # Create a comma-separated string of these 6 points (18 numbers total)
            mocap_data = f"{lm[15].x},{lm[15].y},{lm[15].z}," \
                         f"{lm[16].x},{lm[16].y},{lm[16].z}," \
                         f"{lm[23].x},{lm[23].y},{lm[23].z}," \
                         f"{lm[24].x},{lm[24].y},{lm[24].z}," \
                         f"{lm[27].x},{lm[27].y},{lm[27].z}," \
                         f"{lm[28].x},{lm[28].y},{lm[28].z}"
            
            # Blast the full body frame to Unity on Port 5007
            try:
                sock.sendto(mocap_data.encode('utf-8'), ("127.0.0.1", 5007))
            except Exception as e:
                pass
            lm_2d = result.pose_landmarks.landmark
            lm_3d = result.pose_world_landmarks.landmark
            
            def get_lm(id):
                l = lm_2d[id.value]
                return l if l.visibility > config.MIN_VISIBILITY else None
            
            # --- FIXED HAND LOGIC: RIGHT = TARE, LEFT = MOVE ---
            
            # 1. Fetch 2D Landmarks
            cal_wrist = get_lm(mp_pose.PoseLandmark.RIGHT_WRIST)      # Right Hand for Calibration
            cal_shldr = get_lm(mp_pose.PoseLandmark.RIGHT_SHOULDER)
            trk_wrist_2d = get_lm(mp_pose.PoseLandmark.LEFT_WRIST)    # Left Hand for Tracking
            
            l_hip = get_lm(mp_pose.PoseLandmark.LEFT_HIP)
            r_hip = get_lm(mp_pose.PoseLandmark.RIGHT_HIP)

            # 2. Fetch 3D Landmarks for depth
            trk_wrist_3d = get_landmark_if_visible(lm_3d, mp_pose.PoseLandmark.LEFT_WRIST, config.MIN_VISIBILITY)
            l_hip_3d = get_landmark_if_visible(lm_3d, mp_pose.PoseLandmark.LEFT_HIP, config.MIN_VISIBILITY)
            r_hip_3d = get_landmark_if_visible(lm_3d, mp_pose.PoseLandmark.RIGHT_HIP, config.MIN_VISIBILITY)
            l_ank_3d = get_landmark_if_visible(lm_3d, mp_pose.PoseLandmark.LEFT_ANKLE, 0.4)
            r_ank_3d = get_landmark_if_visible(lm_3d, mp_pose.PoseLandmark.RIGHT_ANKLE, 0.4)

            if l_hip and r_hip and cal_wrist and cal_shldr and trk_wrist_2d and trk_wrist_3d and l_hip_3d and r_hip_3d:
                
                # --- CALCULATION (Absolute Room Position) ---
                hip_x = (l_hip.x + r_hip.x) / 2.0
                torso_size = abs(cal_shldr.y - l_hip.y)
                
                # NEW (correct — ankle-referenced, captures full body crouch):
                if l_ank_3d and r_ank_3d:
                    ground_y = (l_ank_3d.y + r_ank_3d.y) / 2.0
                else:
                    # Fallback if ankles not visible (e.g. camera cut-off)
                    ground_y = (l_hip_3d.y + r_hip_3d.y) / 2.0 - 0.9  # estimate ground
                    wrist_height = trk_wrist_3d.y - ground_y  # wrist above ground

                if calibration.is_calibrated:
                    ## --- FUSION MOVEMENT (Body Core + Left Hand) ---
                    # Unity Z (Pitch Length) = Camera X (Left/Right)
                    body_z = calibration.base_hip_x - hip_x
                    hand_z = calibration.base_wrist_x - trk_wrist_2d.x
                    raw_z = (body_z * 0.65 + hand_z * 0.35) * config.SCALE_FACTOR

                    # Unity X (Crease Width) = Camera Depth (Torso Size + 3D Wrist Z)
                    body_x = torso_size - calibration.base_torso_size
                    hand_x = trk_wrist_3d.z - calibration.base_wrist_z
                    raw_x = (hand_x * 0.45 + body_x * 0.55) * config.DEPTH_MULTIPLIER

                    # Unity Y (Height)
                    raw_y = -(wrist_height - calibration.base_wrist_y)  # negative = lower than calibration = bat goes down

                    sx, sy, sz = smoother.update(raw_x, raw_y, raw_z)
                    
                    # Apply deadzone to kill resting micro-jitters
                    sx = deadzone(sx)
                    sy = deadzone(sy)
                    sz = deadzone(sz)

                    packet = f"{clamp(sx, config.MAX_RANGE):.4f},{clamp(sy, config.MAX_RANGE):.4f},{clamp(sz, config.MAX_RANGE):.4f}"
                    
                    safe_send(packet.encode(), config.UDP_IP, config.UDP_PORT)
                    status_text = "VR Tracker: LIVE (LEFT HAND TRACKING)"
                    # --- SWING DETECTION LOGIC ---
                    current_time = time.time()
                    dt = current_time - prev_time
                    if dt > 0:
                        # Calculate velocity on the Z axis (forward/backward swing plane)
                        swing_velocity = abs(sz - prev_sz) / dt
                        
                        # If the bat is moving faster than 4 meters per second AND we are waiting for a swing
                        if swing_velocity > 4.0 and _eval_state == "evaluating":
                            print(f"🏏 SWING DETECTED! Speed: {swing_velocity:.1f} m/s")
                            # Force the timer to expire immediately so it grades the shot NOW
                            _anim_start = 0 
                            
                    prev_sz = sz
                    prev_time = current_time

                # --- GESTURE: CALIBRATION (Using the RIGHT HAND) ---
                if cal_wrist.y < (cal_shldr.y - 0.15):
                    if tare_timer_start is None: 
                        tare_timer_start = time.monotonic()
                    elif time.monotonic() - tare_timer_start > 1.5:
                        # We lock the calibration to the LEFT HAND'S current position
                        calibration.calibrate(hip_x, torso_size, trk_wrist_2d.x, wrist_height, trk_wrist_3d.z)
                        safe_send(b"TARE", config.UDP_IP, config.UDP_PORT)
                        print("🔥 TARE LOCKED! Left Hand Synced via Right Hand Gesture.")
                        tare_timer_start = None
                else:
                    tare_timer_start = None


                if tare_timer_start is not None:
                    cv2.putText(img, "HOLD HAND Right UP TO SYNC VR...", (50, 100), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 255), 2)

            # --- B. AI COACHING (Dashboard Logic) ---
            if _eval_state == "capture_next" and is_fully_in_frame(result.pose_landmarks):
                _user_baseline = extract_ghost_dict(result.pose_landmarks)
                _eval_state = "locked"
                socketio.emit('speak_command', {'text': 'Stance locked. Ready.'})

            if _eval_state == "locked" and _user_baseline:
                draw_ghost(img, _user_baseline, color=(0, 255, 255)) 

            elif _eval_state == "animating" and _user_baseline:
                progress = min(1.0, (time.time() - _anim_start) / 6.0) 
                current_pose = interpolate_ghost(_user_baseline, TARGET_POSES.get(target, TARGET_POSES["Cover Drive"]), progress)
                draw_ghost(img, current_pose, color=(255, 255, 0)) 
                
                if progress >= 1.0:
                    _eval_state = "evaluating"
                    _anim_start = time.time() 
                    socketio.emit('speak_command', {'text': 'Swing!'})

            elif _eval_state == "evaluating":
                draw_ghost(img, TARGET_POSES.get(target, TARGET_POSES["Cover Drive"]), color=(0, 255, 0)) 
                cv2.putText(img, "SWING NOW!", (50, 150), cv2.FONT_HERSHEY_SIMPLEX, 2, (0, 0, 255), 4)

                # Send live angles to UI
                if (time.time() - last_emit) > 0.1:
                    last_emit = time.time()
                    live = build_angle_dict(result.pose_landmarks)
                    ghost = extract_ghost_dict(result.pose_landmarks)
                    
                    if _is_left_handed:
                        for key in ghost: ghost[key][0] = 1.0 - ghost[key][0]
                    
                    socketio.emit('live_angles', {
                        'l_el': round(live.get('l_el', 0), 1),
                        'r_el': round(live.get('r_el', 0), 1),
                        'l_knee': round(live.get('l_knee', 0), 1),
                        'stride': round(live.get('stride_length', 0), 1),
                        'ghost': ghost,
                        'is_left_handed': _is_left_handed
                    })
                
                # Grade the shot after 5 seconds
                if time.time() - _anim_start > 5.0:
                    _eval_state = "idle"
                    fv = extract_feature_vector(result.pose_landmarks)
                    pred_idx = rf_model.predict(fv)[0]
                    pred_name = CLASS_NAMES[pred_idx]
                    angles = build_angle_dict(result.pose_landmarks)
                    errors, failed_keys = grade_shot(target, angles)
                    confidence = round(float(max(rf_model.predict_proba(fv)[0])) * 100, 1)
                    
                    if errors:
                        if pred_name != target: errors.append(f"(AI thought you were playing an {pred_name}!)")
                        pattern = "PULSE" if pred_name != target else "BUZZ"
                        payload = {'status': 'error', 'errors': errors, 'points': 0, 'haptic_pattern': pattern, 'confidence': confidence, 'failed_keys': failed_keys}
                        send_haptic(pattern)
                    else:
                        payload = {'status': 'success', 'errors': ["Perfect form! Great shot."], 'points': 10, 'haptic_pattern': 'SUCCESS', 'confidence': confidence}
                        send_haptic("SUCCESS")
                    
                    socketio.emit('shot_review', payload)

            if _eval_state == "idle":
                mp_draw.draw_landmarks(img, result.pose_landmarks, mp_pose.POSE_CONNECTIONS)

        cv2.putText(img, status_text, (20, 40), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0) if calibration.is_calibrated else (0,0,255), 2)
        cv2.putText(img, f"Target: {target}", (20, 80), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 165, 0), 2)
        
        cv2.imshow("Master Coach Studio & VR Tracker", img)
        if cv2.waitKey(1) & 0xFF == ord('q'): break

    cap.release()
    cv2.destroyAllWindows()

# ============================================================================
# BACKGROUND IMU THREAD
# ============================================================================
def imu_background_worker():
    madgwick = MadgwickFilter(beta=1.0)
    integrator = PositionIntegrator(velocity_damping=0.90)
    
    esp32_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    esp32_socket.bind(("0.0.0.0", 5004))
    esp32_socket.settimeout(1.0)
    unity_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    
    print("\n--- IMU BACKGROUND THREAD STARTED ---")
    print("Hold Bat Still for 2 seconds to calibrate gravity...")
    
    # 1. Calibration Phase
    calibration_samples = deque(maxlen=20)
    while len(calibration_samples) < 20:
        try:
            data, _ = esp32_socket.recvfrom(256)
            parts = data.decode('utf-8').strip().split(',')
            if len(parts) >= 10:
                calibration_samples.append(np.array([float(parts[4]), float(parts[5]), float(parts[6])]))
        except: pass
    
    calib_accel = np.mean(list(calibration_samples), axis=0)
    print(f"✓ IMU Gravity Calibrated: {calib_accel}")
    
    # 2. Main Tracking Phase
    while True:
        try:
            data, _ = esp32_socket.recvfrom(256)
            parts = data.decode('utf-8').strip().split(',')
            
            if len(parts) >= 10:
                # 1. Grab hardware quaternion and linear acceleration from ESP32
                qx, qy, qz, qw = float(parts[0]), float(parts[1]), float(parts[2]), float(parts[3])
                ax, ay, az = float(parts[4]), float(parts[5]), float(parts[6])
                
                dt = 0.01  # Strict 100Hz timing
                
                # 2. Bypass Madgwick math and inject the perfect hardware quaternion
                # (Note: Madgwick array expects [w, x, y, z])
                madgwick.q = np.array([qw, qx, qy, qz])
                R = madgwick.getRotationMatrix()
                
                # 3. Rotate the gravity-free acceleration to the world frame
                accel_sensor = np.array([ax, ay, az])
                accel_world = R @ accel_sensor
                
                # CRITICAL FIX: DO NOT SUBTRACT 9.81 HERE!
                # The BNO085 SH2_LINEAR_ACCELERATION already removed it.
                
                integrator.update(accel_world, dt)
                
                px, py, pz = integrator.position
                packet = f"{px:.6f},{py:.6f},{pz:.6f}"
                unity_socket.sendto(packet.encode(), (config.UDP_IP, 5009))
                
        except socket.timeout:
            pass
        except Exception as e:
            pass

# ================= 7. FLASK SERVER ENDPOINTS =================
@app.route('/')
def index(): return render_template('index.html')

# --- ADD THIS NEW LINE ---
@app.route('/dashboard')
def dashboard(): return render_template('dashboard.html')

@app.route('/api/analyze', methods=['POST'])
def analyze_shot():
    data = request.json
    
    # Grab the shot name Unity sent us (Default to Cover Drive)
    raw_shot_name = data.get('intendedShot', 'CoverDrive')
    
    # Format it for your grade_shot function
    if raw_shot_name == "CoverDrive":
        shot_name = "Cover Drive"
    elif raw_shot_name == "UpperCut":
        shot_name = "Upper Cut"
    else:
        shot_name = raw_shot_name # "Sweep"
    
    # Check if we actually have camera data yet
    if not latest_angles_dict:
        return jsonify({"feedback": "Waiting for camera data... Stand in frame!"})

    # NOTE: You need to pass your live MediaPipe angles here!
    # Assuming you save the latest angles in a global variable called `latest_angles_dict`
    errors, failed_keys = grade_shot(shot_name, latest_angles_dict) 
    
    # If there are errors, return the first one. Otherwise, say "Perfect!"
    feedback_text = errors[0] if errors else "Perfect execution!"
    
    return jsonify({"feedback": feedback_text})

@socketio.on('set_target_shot')
def handle_target_shot(data): 
    set_target(data['shot'])

@socketio.on('trigger_ready')
def handle_ready(): 
    global _eval_state; _eval_state = "capture_next"

@socketio.on('trigger_animation')
def handle_anim(): 
    global _eval_state, _anim_start
    if _user_baseline: 
        _eval_state = "animating"
        _anim_start = time.time()

@socketio.on('set_coaching_mode')
def handle_coaching_mode(data): pass

@socketio.on('set_handedness')
def handle_handedness(data):
    global _is_left_handed
    _is_left_handed = data.get('left', False)

if __name__ == '__main__':
    # Start the Flask UI server
    threading.Thread(target=lambda: socketio.run(app, host='0.0.0.0', port=5000, allow_unsafe_werkzeug=True, use_reloader=False), daemon=True).start()
    
    # Start the High-Speed IMU Tracker
    threading.Thread(target=imu_background_worker, daemon=True).start()
    
    # Start the Webcam / Vision Tracker (Blocks main thread)
    run_camera()