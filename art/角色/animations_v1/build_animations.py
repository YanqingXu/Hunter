"""Package transparent body-only sheets; do not remove backgrounds or synthesize poses.

Default source_sheet.png grid: 8 columns x 4 rows.
Optional ACTION_source.png grid: 4 columns x 2 rows.
Canonical frames: 192 x 256; resize source crops with NEAREST.
Auto crop detects alpha>128 components only to choose rectangles; alpha is never erased.
Set auto_crop:false at root or action level for exact grid crops.
Optional layout.json:
 {"actions":{"walk":{"grid":[4,2],"source":"walk_source.png",
   "crop_boxes":[[0,0,384,512],...],"translations":[[0,0],...],
   "paste":[[0,0],...],"scale":0.5,"frames":[{"crop":[...],"paste":[...]}]}}}
Crop boxes use source pixels; paste and translations use final canvas pixels.
Per-frame entries override action defaults. No alpha threshold is applied.
Optional sockets.json:
 {"actions":{"walk":[{"grip":[x,y],"front_hand":[x,y],"rear_hand":[x,y],
   "angle":0,"muzzle_direction":[1,0]},...]}}
Socket coordinates are canonical canvas pixels from top left; clockwise degrees.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
SIZE = (192, 256)
ACTIONS = {"walk": (10, True), "run": (14, True), "jump": (10, False), "crawl": (8, True)}

def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else {}

def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def checkerboard(size, tile=16):
    y, x = np.indices((size[1], size[0]))
    v = np.where((x // tile + y // tile) % 2, 45, 58).astype(np.uint8)
    return Image.fromarray(np.stack((v, v + 3, v + 8, np.full_like(v, 255)), axis=2))

def bounds(mask):
    y, x = np.nonzero(mask)
    return [int(x.min()), int(y.min()), int(x.max()+1), int(y.max()+1)] if x.size else None

def indexed(values, index, fallback):
    return values[index] if isinstance(values, list) and index < len(values) else fallback

def socket_for(sockets, action, index):
    values = sockets.get("actions", sockets).get(action, [])
    return indexed(values, index, None) if isinstance(values, list) else values.get(str(index), values.get(f"{index:02d}")) if isinstance(values, dict) else None

def components(mask):
    """8-connected components from row runs; returns x0,y0,x1,y1,area."""
    parents, segments, previous = [], [], []
    def find(node):
        while parents[node] != node:
            parents[node] = parents[parents[node]]
            node = parents[node]
        return node
    for y, row in enumerate(mask):
        diff = np.diff(np.r_[False, row, False].astype(np.int8))
        lefts, rights = np.flatnonzero(diff == 1), np.flatnonzero(diff == -1)
        current = []
        for left, right in zip(lefts, rights):
            node = len(parents)
            parents.append(node)
            segments.append([int(left), y, int(right), y+1, int(right-left)])
            current.append((int(left), int(right), node))
            for old_left, old_right, old_node in previous:
                if old_right >= left and old_left <= right:
                    parents[find(node)] = find(old_node)
        previous = current
    groups = {}
    for node, segment in enumerate(segments):
        key = find(node)
        if key not in groups:
            groups[key] = list(segment)
        else:
            box = groups[key]
            box[0] = min(box[0], segment[0]); box[1] = min(box[1], segment[1])
            box[2] = max(box[2], segment[2]); box[3] = max(box[3], segment[3])
            box[4] += segment[4]
    return sorted(groups.values(), key=lambda box: box[4], reverse=True)

def detect_source(raw, columns, rows, padding=4):
    """Find each main silhouette; only return rectangles, never a pixel mask."""
    alpha = np.asarray(raw.getchannel("A"))
    detected = [[] for _ in range(rows)]
    candidates = []
    for column in range(columns):
        x0, x1 = round(column*raw.width/columns), round((column+1)*raw.width/columns)
        found = components(alpha[:, x0:x1] > 128)
        main = sorted(found[:rows], key=lambda box: box[1])
        if len(main) != rows or any(box[4] < 500 for box in main):
            raise ValueError(f"Could not detect {rows} complete poses in column {column}; provide crop_boxes or disable auto_crop.")
        union = [list(box) for box in main]
        for small in found[rows:]:
            if small[4] < 2:
                continue
            distances = [max(0,box[0]-small[2],small[0]-box[2]) + max(0,box[1]-small[3],small[1]-box[3]) for box in main]
            nearest = int(np.argmin(distances))
            if distances[nearest] <= 16:
                box = union[nearest]
                box[0]=min(box[0],small[0]);box[1]=min(box[1],small[1])
                box[2]=max(box[2],small[2]);box[3]=max(box[3],small[3])
        for row, box in enumerate(union):
            safe_top = 0 if row == 0 else (union[row-1][3] + box[1])//2
            safe_bottom = raw.height if row == rows-1 else (box[3] + union[row+1][1])//2
            crop = [x0+max(0,box[0]-padding),max(safe_top,box[1]-padding),
                    x0+min(x1-x0,box[2]+padding),min(safe_bottom,box[3]+padding)]
            detected[row].append(crop)
        candidates.append(main)
    return [box for row in detected for box in row], candidates

def package_character(character):
    directory = ROOT / character
    source = directory / "source_sheet.png"
    if not source.exists():
        raise FileNotFoundError(source)
    layout = read_json(directory / "layout.json")
    sockets = read_json(directory / "sockets.json")
    (directory / "frames").mkdir(exist_ok=True)
    manifest = {"version": 1, "character": character, "canvas": list(SIZE),
        "bodyOnly": True, "bodyonly": True, "separateGun": True,
        "coordinateSystem": "top-left origin; x right, y down; angle clockwise degrees",
        "pivot": {"normalized": [0.5,0], "pixelsTopLeft": [96,256], "unity": "bottom-center"},
        "weaponAttachment": {"bodyOnly": True, "separateGun": True,
            "socketSource": "sockets.json" if sockets else None,
            "gunPivot": "Align the separate gun grip pivot with each frame grip socket.",
            "facingFlip": "Mirror body and gun together. Pixel socket x becomes canvasWidth-x; muzzle_direction.x changes sign. In Unity mirror the common parent and retain local socket coordinates and angle.",
            "gunScale": "Gun pixels must use the canonical body pixel scale; the exported frames contain no gun."},
        "sources": {}, "actions": {}}
    report = {"character":character,"canvas":list(SIZE),"warnings":[],"errors":[],"actions":{},"detectedSources":{}}
    source_cache = {}
    sheet = Image.new("RGBA", (SIZE[0]*8, SIZE[1]*4))
    contact = checkerboard((SIZE[0]*8, (SIZE[1]+28)*4))
    draw = ImageDraw.Draw(contact)
    font = ImageFont.load_default(size=16)
    for row, (action, (fps, loop)) in enumerate(ACTIONS.items()):
        config = layout.get("actions", {}).get(action, {})
        override = directory / f"{action}_source.png"
        selected = directory / config["source"] if "source" in config else override if override.exists() else source
        is_override = selected != source
        columns, rows = config.get("grid", [4,2] if is_override else [8,4])
        with Image.open(selected) as original:
            original.load()
            source_mode = original.mode
            raw = original.convert("RGBA")
        if raw.width % columns or raw.height % rows:
            report["warnings"].append(f"{selected.name}: grid is not exactly divisible; boundaries rounded")
        cw, ch = raw.width/columns, raw.height/rows
        auto_crop = config.get("auto_crop", layout.get("auto_crop", True))
        cache_key = (str(selected), columns, rows)
        if cache_key not in source_cache:
            if auto_crop:
                boxes, candidates = detect_source(raw, columns, rows, config.get("padding",4))
                standing_height = float(np.median([boxes[i][3]-boxes[i][1]-8 for i in range(columns)]))
                desired_scale = config.get("standing_height",224)/standing_height
                # Use one scale for this entire source, reduced when the widest
                # running/crawling silhouette would otherwise clip the canvas.
                fit_scale = (SIZE[0]-8)/max(box[2]-box[0] for box in boxes)
                source_cache[cache_key] = (boxes, candidates, min(desired_scale, fit_scale))
            else:
                source_cache[cache_key] = (None, None, None)
        detected_boxes, component_candidates, automatic_scale = source_cache[cache_key]
        if detected_boxes:
            report["detectedSources"][selected.name] = {"grid":[columns,rows],"cropBoxes":detected_boxes,
                "mainComponents":component_candidates,"automaticScale":automatic_scale}
        manifest["sources"][selected.name] = {"sha256":hashlib.sha256(selected.read_bytes()).hexdigest(),"size":list(raw.size),"mode":source_mode,"grid":[columns,rows]}
        strip = Image.new("RGBA", (SIZE[0]*8,SIZE[1]))
        frames, records, checks, hashes = [], [], [], []
        for index in range(8):
            frame_config = indexed(config.get("frames", []), index, {})
            cell = index if is_override else row*8+index
            gx, gy = cell%columns, cell//columns
            default = detected_boxes[cell] if detected_boxes else [round(gx*cw),round(gy*ch),round((gx+1)*cw),round((gy+1)*ch)]
            box = frame_config.get("crop",indexed(config.get("crop_boxes",[]),index,default))
            if len(box)!=4 or box[2]<=box[0] or box[3]<=box[1]:
                raise ValueError(f"Invalid crop {character}/{action}/{index}: {box}")
            crop = raw.crop(tuple(box))
            scale = frame_config.get("scale",config.get("scale",automatic_scale if automatic_scale else [SIZE[0]/cw,SIZE[1]/ch]))
            sx, sy = (scale,scale) if isinstance(scale,(int,float)) else scale
            target = (max(1,round(crop.width*sx)),max(1,round(crop.height*sy)))
            if crop.size != target:
                crop = crop.resize(target,Image.Resampling.NEAREST)
            translation = frame_config.get("translation",indexed(config.get("translations",[]),index,[0,0]))
            source_vertical_offset = 0
            default_y = (SIZE[1]-crop.height)//2
            if auto_crop:
                default_y = config.get("baseline",248)-crop.height
                if action == "jump":
                    row_boxes = detected_boxes[gy*columns:(gy+1)*columns]
                    source_vertical_offset = max(b[3] for b in row_boxes)-box[3]
                    default_y -= round(source_vertical_offset*sy)
                    # Preserve the source lift where the canonical canvas allows it.
                    # Runtime position drives actual jump height; never clip the hat.
                    default_y = max(4, default_y)
            paste = frame_config.get("paste",indexed(config.get("paste",[]),index,[(SIZE[0]-crop.width)//2+translation[0],default_y+translation[1]]))
            paste = [round(paste[0]),round(paste[1])]
            frame = Image.new("RGBA",SIZE)
            frame.paste(crop,tuple(paste))  # Copy RGBA directly; an alpha mask would change alpha.
            frame.save(directory/"frames"/f"{action}_{index:02d}.png")
            strip.paste(frame,(index*SIZE[0],0))
            sheet.paste(frame,(index*SIZE[0],row*SIZE[1]))
            contact.alpha_composite(frame,(index*SIZE[0],row*(SIZE[1]+28)+28))
            draw.text((index*SIZE[0]+7,row*(SIZE[1]+28)+5),f"{action.upper()} {index:02d}",font=font,fill=(235,240,247))
            alpha = np.asarray(frame.getchannel("A"))
            present = alpha>0
            edges = {"top":int(present[0].sum()),"bottom":int(present[-1].sum()),"left":int(present[:,0].sum()),"right":int(present[:,-1].sum())}
            nonzero = int(present.sum())
            clipped = int(np.count_nonzero(np.asarray(crop.getchannel("A"))))-nonzero
            digest = hashlib.sha256(frame.tobytes()).hexdigest()
            hashes.append(digest)
            check = {"frame":index,"alphaMin":int(alpha.min()),"alphaMax":int(alpha.max()),"nonzeroPixels":nonzero,"bounds":bounds(present),"opaqueBoundsCandidate":bounds(alpha>128),"edgeOccupancy":edges,"clippedNonzeroPixels":clipped,"sha256Pixels":digest}
            checks.append(check)
            records.append({"index":index,"file":f"frames/{action}_{index:02d}.png","durationMs":1000/fps,"cropBox":box,"scale":[sx,sy],"paste":paste,"sourceVerticalOffset":source_vertical_offset,"bounds":check["bounds"],"pivot":[0.5,0],"weaponSocket":socket_for(sockets,action,index)})
            frames.append(frame)
            if not nonzero:
                report["errors"].append(f"{action}_{index:02d}: empty frame")
            if clipped:
                report["warnings"].append(f"{action}_{index:02d}: {clipped} nonzero alpha pixels clipped")
            if any(edges.values()):
                report["warnings"].append(f"{action}_{index:02d}: alpha touches canvas edge")
        strip.save(directory/f"{action}.png")
        gif_frames = []
        for frame in frames:
            background = checkerboard(SIZE)
            background.alpha_composite(frame)
            gif_frames.append(background.convert("RGB").convert("P",palette=Image.Palette.ADAPTIVE,colors=256))
        gif_options = {"loop":0} if loop else {}
        gif_frames[0].save(directory/f"{action}_preview.gif",save_all=True,append_images=gif_frames[1:],duration=round(1000/fps),disposal=2,optimize=False,**gif_options)
        unique = len(set(hashes))
        if unique != 8:
            report["warnings"].append(f"{action}: {unique}/8 unique RGBA frames")
        report["actions"][action] = {"uniqueFrames":unique,"nonnullFrames":sum(c["nonzeroPixels"]>0 for c in checks),"frames":checks}
        manifest["actions"][action] = {"fps":fps,"loop":loop,"frameCount":8,"strip":f"{action}.png","preview":f"{action}_preview.gif","frames":records}
    sheet.save(directory/"spritesheet.png")
    contact.convert("RGB").save(directory/"contact_sheet.png")
    report["nonnullFrames"] = sum(a["nonnullFrames"] for a in report["actions"].values())
    report["passed"] = not report["errors"] and all(a["uniqueFrames"]==8 and all(f["clippedNonzeroPixels"]==0 for f in a["frames"]) for a in report["actions"].values())
    write_json(directory/"manifest.json",manifest)
    write_json(directory/"validation.json",report)
    return {"character":character,"nonnullFrames":report["nonnullFrames"],"uniqueFrames":{k:v["uniqueFrames"] for k,v in report["actions"].items()},"warnings":report["warnings"],"errors":report["errors"],"passed":report["passed"]}

GALLERY = r"""<!doctype html>
<html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Hunter 角色帧动画预览</title>
<style>body{font:16px system-ui,sans-serif;background:#151b25;color:#e6edf8;margin:24px}main{max-width:1120px;margin:auto}h1{font-size:24px}.controls{display:flex;flex-wrap:wrap;gap:12px;align-items:center;margin:18px 0}select,button,input{font:inherit}button,select{padding:8px;background:#293647;color:#eef4ff;border:1px solid #506079;border-radius:5px}canvas{display:block;width:min(100%,576px);height:auto;image-rendering:pixelated;border:1px solid #60708b;background:#343b48}p{line-height:1.7;color:#b9c9df}.assets{display:flex;flex-wrap:wrap;gap:16px}a{color:#9ac8ff}.status{min-height:26px}label{display:flex;align-items:center;gap:6px}.note{font-size:14px}</style>
<main><h1>Hunter · 角色动作帧预览</h1><p>人物保留透明背景，枪械作为独立资源叠加。跳跃到落地后停止；点击“重播”查看完整过程。</p>
<div class="controls"><label>角色<select id="character"></select></label><label>动作<select id="action"><option value="walk">走</option><option value="run">跑</option><option value="jump">跳</option><option value="crawl">匍匐前进</option></select></label><button id="play">暂停</button><button id="replay">重播</button><label>速度<select id="speed"><option value="0.5">0.5×</option><option selected value="1">1×</option><option value="1.5">1.5×</option><option value="2">2×</option></select></label><label><input id="flip" type="checkbox">朝左</label></div>
<div class="controls"><button id="previous">上一帧</button><label>帧<input id="frameSlider" type="range" min="0" max="7" step="1" value="0"></label><button id="next">下一帧</button></div>
<canvas id="view" width="384" height="288"></canvas><p class="status" id="status"></p>
<div class="controls"><label>独立枪 PNG<input id="gunFile" type="file" accept="image/png"></label><label>枪缩放<input id="gunScale" type="number" value="1" min="0.01" step="0.1" style="width:60px"></label><label>握把 Pivot X<input id="gunPivotX" type="number" value="0.2" min="0" max="1" step="0.05" style="width:60px"></label><label>Y<input id="gunPivotY" type="number" value="0.6" min="0" max="1" step="0.05" style="width:60px"></label><label><input id="showSocket" type="checkbox">显示握把锚点</label></div>
<p class="note">锚点由手部肤色估计，需用实际枪械 PNG 校准。先填写枪图内实际握把 Pivot 的归一化 X/Y，再调枪缩放；检查握把与后手、护木与前手是否重合。枪图仅用于本地预览。未配置 sockets.json 的帧显示“锚点未配置”。</p><div class="assets" id="assets"></div>
</main><script>
const DATA=__DATA__;
const $=id=>document.getElementById(id),canvas=$('view'),ctx=canvas.getContext('2d');ctx.imageSmoothingEnabled=false;
let images=[],playing=true,elapsed=0,last=0,frame=0,gun=null,loaded=false,generation=0;
const names={'01':'01 · 蓝衣猎人','02':'02 · 羽冠猎人','03':'03 · 青衣行者'};
for(const key of Object.keys(DATA)){const option=document.createElement('option');option.value=key;option.textContent=names[key]||key;$('character').append(option)}
function current(){return DATA[$('character').value].actions[$('action').value]}
function loadImage(src){return new Promise((resolve,reject)=>{const im=new Image();im.onload=()=>resolve(im);im.onerror=()=>reject(new Error(src));im.src=src})}
async function select(){const token=++generation;loaded=false;playing=true;elapsed=0;frame=0;$('play').textContent='暂停';const char=$('character').value,action=$('action').value;
try{const next=await Promise.all(current().frames.map(f=>loadImage(char+'/'+f.file)));if(token!==generation)return;images=next;loaded=true;$('assets').innerHTML=[['透明动作条',char+'/'+action+'.png'],['整张透明图集',char+'/spritesheet.png'],['透明度检查图',char+'/contact_sheet.png'],['帧与锚点清单',char+'/manifest.json']].map(([label,url])=>'<a href="'+url+'" target="_blank">'+label+'</a>').join('');draw()}catch(error){$('status').textContent='无法读取帧图片：'+error.message}}
function checker(){for(let y=0;y<288;y+=16)for(let x=0;x<384;x+=16){ctx.fillStyle=((x/16+y/16)%2)?'#2d3035':'#3a3d42';ctx.fillRect(x,y,16,16)}}
function draw(){checker();if(!loaded)return;$('frameSlider').value=frame;const a=current(),f=a.frames[frame],s=f.weaponSocket;ctx.save();if($('flip').checked){ctx.translate(384,0);ctx.scale(-1,1)}ctx.translate(96,16);ctx.drawImage(images[frame],0,0);
if(gun&&s&&Array.isArray(s.grip)){ctx.save();ctx.translate(s.grip[0],s.grip[1]);ctx.rotate((s.angle||0)*Math.PI/180);const scale=Math.max(.01,Number($('gunScale').value)||1),w=gun.width*scale,h=gun.height*scale;ctx.drawImage(gun,-w*Number($('gunPivotX').value),-h*Number($('gunPivotY').value),w,h);ctx.restore()}
if($('showSocket').checked&&s&&Array.isArray(s.grip)){ctx.strokeStyle='#ffcc66';ctx.lineWidth=1;ctx.beginPath();ctx.moveTo(s.grip[0]-4,s.grip[1]);ctx.lineTo(s.grip[0]+4,s.grip[1]);ctx.moveTo(s.grip[0],s.grip[1]-4);ctx.lineTo(s.grip[0],s.grip[1]+4);ctx.stroke()}ctx.restore();
$('status').textContent='第 '+(frame+1)+' / '+a.frameCount+' 帧 · '+a.fps+' FPS · '+(a.loop?'循环':'单次跳跃')+' · '+(s?(s.estimated?'锚点为估计值，需配枪校准':'握把锚点已配置'):'锚点未配置')+(!playing&&frame===a.frameCount-1&&!a.loop?' · 已落地':'')}
function tick(now){const dt=last?Math.min(now-last,100):0;last=now;if(loaded&&playing){const a=current();elapsed+=dt*Number($('speed').value);const next=Math.floor(elapsed/(1000/a.fps));if(a.loop)frame=next%a.frameCount;else{frame=Math.min(next,a.frameCount-1);if(next>=a.frameCount){playing=false;$('play').textContent='播放'}}}draw();requestAnimationFrame(tick)}
$('play').onclick=()=>{if(!playing&&!current().loop&&frame===current().frameCount-1)elapsed=0;playing=!playing;$('play').textContent=playing?'暂停':'播放'};$('replay').onclick=()=>{elapsed=0;frame=0;playing=true;$('play').textContent='暂停'};
function exactFrame(index){frame=Math.max(0,Math.min(7,index));elapsed=frame*1000/current().fps;playing=false;$('play').textContent='播放';draw()}
$('frameSlider').oninput=event=>exactFrame(Number(event.target.value));$('previous').onclick=()=>exactFrame(frame-1);$('next').onclick=()=>exactFrame(frame+1);
$('character').onchange=select;$('action').onchange=select;$('gunFile').onchange=async event=>{const file=event.target.files[0];if(!file){gun=null;return}const url=URL.createObjectURL(file);try{gun=await loadImage(url)}finally{URL.revokeObjectURL(url)}};
select();requestAnimationFrame(tick);
</script></html>"""

def gallery():
    manifests={path.parent.name:read_json(path) for path in sorted(ROOT.glob("*/manifest.json"))}
    payload=json.dumps(manifests,ensure_ascii=False).replace("</","<\\/")
    (ROOT/"preview.html").write_text(GALLERY.replace("__DATA__",payload),encoding="utf-8")

def main():
    parser=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--characters",nargs="+",default=["01","02","03"])
    parser.add_argument("--strict",action="store_true",help="Fail for empty or duplicate frames")
    args=parser.parse_args()
    reports=[package_character(c) for c in args.characters]
    gallery()
    print(json.dumps({"characters":reports,"gallery":str(ROOT/"preview.html")},ensure_ascii=False,indent=2))
    return 2 if args.strict and any(not r["passed"] for r in reports) else 0

if __name__=="__main__":
    raise SystemExit(main())

