"""Sim v5: 'yo-yo' runs. Nose stays on target; the axial (fwd/rev) thrusters approach, brake and back off
along the line of sight while firing. Sideways drift: small nose tilts, or a CORRECT swing when it gets large."""
import numpy as np, math, sys
seed = int(sys.argv[1]) if len(sys.argv) > 1 else 1
TJ = float(sys.argv[2]) if len(sys.argv) > 2 else 3.0     # target jink accel sigma per axis
rng = np.random.default_rng(seed)
dt = 1/60
M, F = 1400.0, 14400.0
aMax = F/M
ProjSpeed, FireRange = 400, 700
Closing, Extend, Orbit = 45, 30, 12
SetupR, Near, Far, LatMax, BreakR, Cone = 650, 140, 480, 25, 60, 55
Kp, Kd, MaxTurn, Gain, MaxSpeed, ANG_ACC = 7, 0.3, 7, 1.6, 98, 25.0
def nz(v): n = np.linalg.norm(v); return v/n if n > 1e-9 else v
def rot(v, w, h):
    a = np.linalg.norm(w)*h
    if a < 1e-12: return v
    k = w/np.linalg.norm(w)
    return nz(v*math.cos(a) + np.cross(k, v)*math.sin(a) + k*np.dot(k, v)*(1-math.cos(a)))
pos = np.zeros(3); vel = np.zeros(3); nose = np.array([0., 0., -1.]); omega = np.zeros(3)
tp = np.array([400., 150., -1500.]); tv = np.array([25., 0., 5.]); ta = np.zeros(3); tr = 12.0
attack = nz(-tp)
state, opening = "SETUP", False
prev_err = np.zeros(3); prev_aim = None; tc = 0; breakdir = None
T = dict(); fire_t = 0; hits = 0; mind = 1e9; corrects = 0
for step in range(int(120/dt)):
    t = step*dt
    if step % int(3/dt) == 0: ta = rng.normal(0, TJ, 3)
    tv = tv + ta*dt; tp = tp + tv*dt
    rel = tp - pos; dist = np.linalg.norm(rel); d = rel/dist
    relv = vel - tv; closing = relv@d; lat = relv - closing*d; latspd = np.linalg.norm(lat)
    mind = min(mind, dist)
    latDir = nz(lat) if latspd > 1 else nz(np.cross(d, [0, 1, 0]))
    stop = max(closing, 0)**2/(2*aMax*0.6)
    if dist < Near + tr + stop: opening = True
    elif dist > Far: opening = False
    vc = -Extend if opening else Closing
    desRel = d*vc + latDir*Orbit
    laterr = np.linalg.norm(lat - latDir*Orbit)
    if state == "SETUP" and (-d)@attack > math.cos(math.radians(Cone+15)) and dist < SetupR*1.15: state = "RUN"
    elif state in ("RUN", "CORRECT") and dist < BreakR + tr: state = "BREAK"; breakdir = latDir
    elif state == "RUN" and laterr > LatMax: state = "CORRECT"; tc = t; corrects += 1
    elif state == "CORRECT" and (laterr < LatMax*0.4 or t - tc > 4): state = "RUN"
    elif state == "BREAK" and dist > Near + tr + 40: state = "RUN"
    T[state] = T.get(state, 0) + dt
    if state == "RUN":
        rp, rv = rel, tv - vel
        a = rv@rv - ProjSpeed**2; b = 2*rp@rv; c = rp@rp; disc = b*b - 4*a*c
        tt = (-b - math.sqrt(disc))/(2*a) if disc >= 0 else dist/ProjSpeed
        if tt <= 0: tt = (-b + math.sqrt(disc))/(2*a)
        for _ in range(4): p = rp + rv*tt + 0.5*ta*tt*tt; tt = np.linalg.norm(p)/ProjSpeed
        aim = nz(p)
        acc = (tv + desRel - vel)*Gain
        axial = acc@aim; latA = acc - axial*aim
        win = max(0.4, math.degrees(math.atan(tr*0.6/dist)))
        inR = tt*ProjSpeed < FireRange
        tilt = min(math.radians(win*0.5) if inR else math.radians(25), np.linalg.norm(latA)/aMax)
        desired = nz(aim + nz(latA)*math.tan(tilt)*np.sign(axial)) if np.linalg.norm(latA) > 1e-2 and abs(axial) > 0.5 else aim
        ff = np.cross(prev_aim, aim)/dt if prev_aim is not None else np.zeros(3)
        if np.linalg.norm(ff) > 2: ff = nz(ff)*2
        prev_aim = aim
        thrust = acc@nose*M
        err = math.degrees(math.acos(np.clip(nose@aim, -1, 1)))
        if inR and err < win:
            fire_t += dt
            hits += dt*10*min(1, (math.degrees(math.atan(tr/dist))/max(err, 0.05))**2)*0.5
    else:
        prev_aim = None; ff = np.zeros(3)
        if state == "SETUP":
            sp = tp + attack*SetupR; dd = sp - pos; L = np.linalg.norm(dd)
            v = min(MaxSpeed, math.sqrt(2*aMax*0.4*L) + Closing); want = tv + (dd/L*v if L > 1 else 0)
        elif state == "CORRECT": want = tv + desRel
        else: want = tv + breakdir*40 - d*20
        if np.linalg.norm(want) > MaxSpeed: want = nz(want)*MaxSpeed
        need = (want - vel)*Gain; mag = np.linalg.norm(need)
        if mag < 0.15: desired = nose; thrust = 0
        else:
            nd = need/mag; desired = -nd if nose@nd < -0.2 else nd
            along = need@nose; gate = np.clip((abs(along)/mag - 0.85)/0.15, 0, 1); thrust = along*M*gate
    ang = math.acos(np.clip(nose@desired, -1, 1)); ax = np.cross(nose, desired)
    e = nz(ax)*ang if np.linalg.norm(ax) > 1e-9 else np.zeros(3)
    de = (e - prev_err)/dt; prev_err = e
    if np.linalg.norm(de) > 20: de = np.zeros(3)
    wc = e*Kp + de*Kd + ff
    if np.linalg.norm(wc) > MaxTurn: wc = nz(wc)*MaxTurn
    dw = wc - omega; lim = ANG_ACC*dt
    omega = omega + (dw if np.linalg.norm(dw) < lim else nz(dw)*lim)
    nose = rot(nose, omega, dt)
    vel = vel + nose*np.clip(thrust, -F, F)/M*dt; pos = pos + vel*dt
print(f"seed {seed} jink {TJ}: firing {fire_t:5.1f}s/120s  est.hits {hits:4.0f}  corrections {corrects:2d}  closest {mind:4.0f} m  "
      + " ".join(f"{k}={v:.0f}s" for k, v in T.items()))
