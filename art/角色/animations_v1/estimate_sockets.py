"""Estimate weapon attachment sockets from skin components; review markers before use.
Only writes JSON and diagnostic overlays. Exported animation pixels are untouched.
"""
import argparse
import json
import math
import sys
sys.dont_write_bytecode = True
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from build_animations import ROOT, SIZE, ACTIONS, checkerboard, components

def centre(mask, box):
    x0,y0,x1,y1=box[:4]
    y,x=np.nonzero(mask[y0:y1,x0:x1])
    return [round(float(x.mean()+x0),1),round(float(y.mean()+y0),1)]

def estimate(frame, action, character='01'):
    a=np.asarray(frame).astype(np.int16)
    r,g,b,alpha=(a[:,:,i] for i in range(4))
    skin=(alpha>128)&(r>160)&(g>95)&(b>65)&(r-g>(8 if character=="03" else 25))&(g-b>(8 if character=="03" else 15))&(r-g<90)
    body=alpha>128
    yy,xx=np.nonzero(body)
    if not len(xx):
        raise ValueError("Empty frame")
    x0,y0,x1,y1=int(xx.min()),int(yy.min()),int(xx.max()),int(yy.max())
    width=x1-x0
    candidates=[box for box in components(skin) if box[4]>=(25 if character=="03" else 65) and box[2]-box[0]<=36 and box[3]-box[1]<=30 and (box[0]+box[2])/2>x0+width*.48 and (action=="crawl" or (box[1]+box[3])/2<y0+(y1-y0)*.7)]
    if action=="crawl":
        region=skin.copy()
        ys,xs=np.indices(skin.shape)
        region&=(xs>x1-38)&(ys>y0+(y1-y0)*.5)
        groups=components(region)
        group=groups[0] if groups else None
        if group:
            gx0,gy0,gx1,gy1,_=group
            cut=gx0+(gx1-gx0)*.55
            front=region&(xs>=cut)
            rear=region&(xs<cut)&(xs>=gx0)&(ys>=gy0)&(ys<gy1)
            front_hand=centre(front,[gx0,gy0,gx1,gy1])
            rear_hand=centre(rear,[gx0,gy0,gx1,gy1])
        else:
            raise ValueError("Could not find crawl hands")
        confidence="low: nearby crawl palms share a skin component"
    else:
        if not candidates:
            raise ValueError("Could not find support palm")
        front_box=max(candidates,key=lambda box:(box[0]+box[2])/2)
        front_hand=centre(skin,front_box)
        rear_candidates=[box for box in candidates if (box[0]+box[2])/2<front_hand[0]-15 and front_hand[1]-8<(box[1]+box[3])/2<front_hand[1]+22]
        if rear_candidates:
            rear_box=max(rear_candidates,key=lambda box:(box[0]+box[2])/2)
            rear_hand=centre(skin,rear_box)
            confidence="candidate: separate fist and support palm skin components"
        else:
            # Bare forearms can join the fist to the torso in one component.
            # Use only a local skin window behind the support palm.
            ys,xs=np.indices(skin.shape)
            window=skin&(xs>=front_hand[0]-42)&(xs<=front_hand[0]-17)&(ys>=front_hand[1]-4)&(ys<=front_hand[1]+17)
            y,x=np.nonzero(window)
            if not len(x):
                raise ValueError("Could not find trigger fist skin window")
            rear_hand=[round(float(x.mean()),1),round(float(y.mean()),1)]
            confidence="low: fist skin is connected to forearm; review local-window centroid"
    dx,dy=front_hand[0]-rear_hand[0],front_hand[1]-rear_hand[1]
    length=math.hypot(dx,dy)
    return {"grip":rear_hand,"rear_hand":rear_hand,"front_hand":front_hand,
        "angle":round(math.degrees(math.atan2(dy,dx)),2),
        "muzzle_direction":[round(dx/length,4),round(dy/length,4)],
        "estimated":True,"angleEstimated":True,"reviewStatus":"candidate","confidence":confidence}

def package(character):
    directory=ROOT/character
    sockets={"version":1,"character":character,"estimated":True,
        "coordinateSystem":"192x256 canonical pixels, top-left origin; clockwise angle degrees",
        "gunPivot":"Use the separate gun grip pivot at grip; front_hand is the support point.",
        "actions":{}}
    contact=checkerboard((SIZE[0]*8,(SIZE[1]+28)*4))
    draw=ImageDraw.Draw(contact)
    font=ImageFont.load_default(size=16)
    for row,action in enumerate(ACTIONS):
        entries=[]
        for i in range(8):
            frame=Image.open(directory/"frames"/f"{action}_{i:02d}.png").convert("RGBA")
            socket=estimate(frame,action,character)
            entries.append(socket)
            ox,oy=i*SIZE[0],row*(SIZE[1]+28)+28
            contact.alpha_composite(frame,(ox,oy))
            draw.text((ox+5,oy-23),f"{action.upper()} {i:02d}",font=font,fill="#edf3ff")
            for key,color in [("rear_hand","#ff6682"),("front_hand","#67e8f9")]:
                x,y=socket[key];x+=ox;y+=oy
                draw.line((x-5,y,x+5,y),fill=color,width=1)
                draw.line((x,y-5,x,y+5),fill=color,width=1)
            rear,front=socket["rear_hand"],socket["front_hand"]
            draw.line((rear[0]+ox,rear[1]+oy,front[0]+ox,front[1]+oy),fill="#e7ce73",width=1)
        sockets["actions"][action]=entries
    (directory/"sockets.json").write_text(json.dumps(sockets,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    contact.convert("RGB").save(directory/"socket_inspection.png")
    return {"character":character,"sockets":sockets["actions"]}

if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--characters",nargs="+",default=["01"])
    args=parser.parse_args()
    results=[package(c) for c in args.characters]
    print(json.dumps([{"character":r["character"],"frames":sum(len(v) for v in r["sockets"].values()),"firstSocketPerAction":{k:v[0] for k,v in r["sockets"].items()}} for r in results],ensure_ascii=False,indent=2))
