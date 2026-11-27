#!/usr/bin/env python3
import json, urllib.request
UA={"User-Agent":"os-iso-downloader-probe/0.1"}
def get(u):
    try:
        r=urllib.request.urlopen(urllib.request.Request(u,headers=UA),timeout=60); return r.status,r.read().decode("utf-8","replace")
    except Exception as e: return 0,str(e)
for u in ("https://api.ipsw.me/v4/devices","https://api.ipsw.me/v4/device/iPhone1,1?type=ipsw","https://api.ipsw.me/v4/ipsw/7.1.2","https://api.ipsw.me/v4/ipsw/version/7.1.2","https://api.ipsw.me/v4/ipsw/26.0","https://api.ipsw.me/v4/ipsw/16.7.2","https://api.ipsw.me/v4/device/iPhone10,3?type=ipsw","https://api.ipsw.me/v4/device/iPad1,1?type=ipsw","https://api.ipsw.me/v4/ipsw/27.0"):
    s,b=get(u); print("##",u,s,len(b)); print("  ",b[:600].replace("\n"," "))
s,b=get("https://api.ipsw.me/v4/devices")
try:
    d=json.loads(b); print("devices",len(d)); import collections
    print(collections.Counter(x["identifier"].split(",")[0].rstrip("0123456789") for x in d))
except Exception as e: print("ERR",e)
for i in ("iPhone10,3","iPhone17,1","iPhone18,1","iPad13,1","iPad1,1"):
    s,b=get(f"https://api.ipsw.me/v4/device/{i}?type=ipsw")
    try:
        f=json.loads(b)["firmwares"]; print(i,len(f),[ (x["version"],x["signed"]) for x in f[:4]], list(f[0].keys()))
    except Exception as e: print(i,"ERR",s,b[:100])
