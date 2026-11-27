#!/usr/bin/env python3
import json, re, urllib.parse, urllib.request
UA={"User-Agent":"os-iso-downloader-probe/0.1"}
def get(u,n=400000):
    return urllib.request.urlopen(urllib.request.Request(u,headers=UA),timeout=60).read(n).decode("utf-8","replace")
for u in ("https://www.helenos.org/releases/","https://pub.sortix.org/sortix/release/","https://9front.org/iso/","https://static.redox-os.org/releases/","https://download.minix3.org/iso/","https://www.riscosopen.org/content/downloads","https://sourceforge.net/projects/reactos/rss?path=/ReactOS&limit=40","https://sourceforge.net/projects/cachyos-arch/rss?path=/&limit=40","https://sourceforge.net/projects/kaosx/rss?path=/ISO&limit=20","https://sourceforge.net/projects/absolute-linux/rss?path=/&limit=20","https://derivelinux.org/releases/","https://getalt.org/ru/","https://api.github.com/repos/klange/toaruos/releases?per_page=2","https://api.github.com/repos/webosose/build-webos/releases?per_page=2"):
    try: b=get(u)
    except Exception as e: print("##",u,"ERR",str(e)[:80]); continue
    if "rss?path" in u: print("##",u,re.findall(r"<link>([^<]+)</link>",b)[:14]); continue
    if "api.github" in u:
        try:
            for r in json.loads(b): print("##",u,r["tag_name"],[a["name"] for a in r["assets"]][:8])
        except Exception: print("##",u,b[:100])
        continue
    hrefs=[h for h in re.findall(r'href=["\']([^"\'#?]+)["\']',b)]
    files=[h for h in hrefs if re.search(r"\.(iso|img|bz2|xz|gz|7z|zip|zst)(\.\w+)?$",h,re.I)]
    print("##",u,"files:",files[-12:],"dirs:",[h for h in hrefs if h.endswith("/")][-6:])
for q in ("BeOS","OS/2 Warp","Symbian","Palm OS","MeeGo","Sailfish OS","Firefox OS","BlackBerry 10","Maemo","webOS","MenuetOS","Inferno","RISC OS","KaiOS","Tizen","Plan 9"):
    u="https://archive.org/advancedsearch.php?q="+urllib.parse.quote(f'title:({q}) AND mediatype:software')+"&fl[]=identifier&fl[]=title&rows=4&output=json&sort[]=downloads+desc"
    try: docs=json.loads(get(u))["response"]["docs"]
    except Exception as e: print("ERR",q,e); continue
    for d in docs:
        try: fs=json.loads(get("https://archive.org/metadata/"+d["identifier"]+"/files",900000))["result"]
        except Exception: continue
        for f in fs[:200]:
            n=f.get("name","")
            if f.get("source")=="original" and re.search(r"\.(iso|img|7z|zip|bin|vdi|vmdk|ova)$",n,re.I):
                print("A",q,"|",d["identifier"],"|",n,"|",f.get("size"))
