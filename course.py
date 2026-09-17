import math, sys

def run(course, start_elev, road_w, verge_w, quiet=False):
    def direction(h): r=math.radians(h); return (math.sin(r), math.cos(r))
    def right(h):     r=math.radians(h); return (math.cos(r), -math.sin(r))
    def rotY(v, deg):
        r=math.radians(deg); c,s=math.cos(r),math.sin(r)
        return (v[0]*c + v[1]*s, -v[0]*s + v[1]*c)

    pos=(0.0,0.0); y=start_elev; h=0.0
    pts=[(0.0,y,0.0)]; total=0.0; drop=0.0
    for kind,L,R,A,grade,label in course:
        if kind=="A":
            arc=R*abs(A)*math.pi/180; steps=max(2, math.ceil(abs(A)/12))
            rt=right(h); sign=1 if A>0 else -1
            center=(pos[0]+rt[0]*R*sign, pos[1]+rt[1]*R*sign)
            rel=(pos[0]-center[0], pos[1]-center[1]); y0=y
            for i in range(1,steps+1):
                rr=rotY(rel, A*i/steps)
                pts.append((center[0]+rr[0], y0-(arc*i/steps)*grade*0.01, center[1]+rr[1]))
            seg=arc
        else:
            steps=max(1, math.ceil(L/10)); d=direction(h); y0=y
            for i in range(1,steps+1):
                t=L*i/steps
                pts.append((pos[0]+d[0]*t, y0-t*grade*0.01, pos[1]+d[1]*t))
            seg=L
        pos=(pts[-1][0],pts[-1][2]); y=pts[-1][1]
        if kind=="A": h+=A
        total+=seg; drop+=seg*grade*0.01

    footprint = road_w + 2*verge_w
    worst=1e9; wa=wb=None
    # cumulative arc length at each point, for the "far apart along the course" test
    cum=[0.0]
    for i in range(1,len(pts)):
        cum.append(cum[-1]+math.dist((pts[i-1][0],pts[i-1][2]),(pts[i][0],pts[i][2])))
    for i in range(len(pts)):
        for j in range(i+1,len(pts)):
            if cum[j]-cum[i] < 60: continue
            d=math.dist((pts[i][0],pts[i][2]),(pts[j][0],pts[j][2]))
            if d<worst: worst=d; wa,wb=i,j
    xs=[p[0] for p in pts]; zs=[p[2] for p in pts]
    if not quiet:
        print(f"length {total:.0f} m | drop {drop:.0f} m | avg grade {drop/total*100:.1f}% | knots {len(pts)}")
        print(f"elevation {pts[0][1]:.0f} -> {pts[-1][1]:.0f} m")
        print(f"bounds X {max(xs)-min(xs):.0f} m, Z {max(zs)-min(zs):.0f} m")
        print(f"closest approach between distant parts: {worst:.1f} m  (footprint needs {footprint:.0f} m)")
        print("VERDICT:", "CLEAR" if worst > footprint + 4 else "*** TOO CLOSE - road would overlap itself ***")
    return total, drop, worst, footprint
