#!/usr/bin/env python3
import json, re, urllib.parse, urllib.request
UA={"User-Agent":"os-iso-downloader-probe/0.1"}
def get(u):
    return urllib.request.urlopen(urllib.request.Request(u,headers=UA),timeout=60).read().decode("utf-8","replace")
for q in ("Mandriva Linux 2010","Mandriva Linux 2008","Mandriva Linux 2011","KolibriOS","Haiku R1 beta","Mandrake Linux","Absolute Linux","Windows Me","Windows 95 OSR2"):
    u="https://archive.org/advancedsearch.php?q="+urllib.parse.quote(f'title:({q}) AND mediatype:software')+"&fl[]=identifier&fl[]=title&rows=5&output=json&sort[]=downloads+desc"
    try: docs=json.loads(get(u))["response"]["docs"]
    except Exception as e: print("ERR",q,e); continue
    for d in docs:
        try: fs=json.loads(get("https://archive.org/metadata/"+d["identifier"]+"/files"))["result"]
        except Exception: continue
        for f in fs:
            n=f.get("name","")
            if f.get("source")=="original" and re.search(r"\.(iso|img|7z|zip)$",n,re.I):
                print(q,"|",d["identifier"],"|",n,"|",f.get("size"))
