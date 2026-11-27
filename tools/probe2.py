#!/usr/bin/env python3
import json, re, sys, urllib.request, urllib.parse
UA={"User-Agent":"os-iso-downloader-probe/0.1"}
def get(u, method="GET", data=None, head=False, rng=None, n=1500):
    h=dict(UA)
    if rng: h["Range"]=rng
    try:
        req=urllib.request.Request(u,headers=h,data=data,method=method)
        with urllib.request.urlopen(req,timeout=60) as r:
            b=r.read(200000 if not head else 0)
            return r.status, dict(r.headers), b.decode("utf-8","replace")
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read(500).decode("utf-8","replace")
    except Exception as e:
        return 0, {}, str(e)
def show(label,u,**k):
    s,h,b=get(u,**k)
    print("###",label,s,u)
    print("   ct=",h.get("Content-Type"),"cl=",h.get("Content-Length"),"cr=",h.get("Content-Range"),"cd=",h.get("Content-Disposition"))
    print("   ",b[:1200].replace("\n"," ") if b else "")
    return s,h,b
mode=sys.argv[1]
if mode=="uup":
    s,h,b=show("listid 26H2","https://api.uupdump.net/listid.php?search=Windows%2011%2026H2&sortByDate=1")
    s,h,b=show("listid insider","https://api.uupdump.net/listid.php?search=Windows%2011%20Insider%20Preview&sortByDate=1")
    try:
        d=json.loads(b); builds=d["response"]["builds"]; print("insider builds",len(builds)); print(json.dumps(builds[:4],ensure_ascii=False))
        uid=builds[0]["uuid"]
        show("langs","https://api.uupdump.net/listlangs.php?id="+uid)
        show("editions","https://api.uupdump.net/listeditions.php?id="+uid+"&lang=en-us")
        for m,data in (("GET",None),("POST",urllib.parse.urlencode({"autodl":"2","updates":"1","cleanup":"1","netfx":"1","esd":"0"}).encode())):
            s,h,b=get("https://uupdump.net/get.php?id="+uid+"&pack=en-us&edition=professional"+("&autodl=2" if m=="GET" else ""),method=m,data=data)
            print("### package",m,s,h.get("Content-Type"),h.get("Content-Disposition"),h.get("Content-Length"),b[:80].replace("\n"," "))
    except Exception as e:
        print("ERR",e)
    show("fetchupd","https://api.uupdump.net/fetchupd.php?arch=amd64&ring=WIF&build=latest")
if mode=="archive":
    for q in ("Windows Longhorn","Windows Whistler","Windows Vista beta","Windows Chicago","Windows 98 Second Edition","Windows 2000","Windows Neptune","Windows Me", "Windows 95"):
        u="https://archive.org/advancedsearch.php?q="+urllib.parse.quote(f'title:({q}) AND mediatype:software')+"&fl[]=identifier&fl[]=title&fl[]=item_size&rows=8&output=json&sort[]=downloads+desc"
        s,h,b=get(u)
        try:
            for d in json.loads(b)["response"]["docs"]: print(q,"|",d.get("identifier"),"|",d.get("title"),"|",d.get("item_size"))
        except Exception as e: print(q,"ERR",s,b[:200])
if mode=="distro":
    show("gentoo","https://distfiles.gentoo.org/releases/amd64/autobuilds/latest-install-amd64-minimal.txt")
    show("gentoo dir","https://distfiles.gentoo.org/releases/amd64/autobuilds/current-install-amd64-minimal/")
    show("rocky dir","https://download.rockylinux.org/pub/rocky/10/isos/x86_64/")
    show("alma dir","https://repo.almalinux.org/almalinux/10/isos/x86_64/")
    show("centos","https://mirror.stream.centos.org/10-stream/BaseOS/x86_64/iso/")
    show("flex","https://dl.google.com/chromeos-flex/images/flex_image.bin.zip",rng="bytes=0-0")
    show("cros recovery json","https://dl.google.com/dl/edgedl/chromeos/recovery/recovery.json",n=300)
    show("fedora11","https://archives.fedoraproject.org/pub/archive/fedora/linux/releases/11/Fedora/i386/iso/")
    show("rhel","https://developers.redhat.com/products/rhel/download")
