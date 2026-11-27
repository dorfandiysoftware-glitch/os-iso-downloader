#!/usr/bin/env python3
import re, sys, urllib.request, urllib.parse
UA={"User-Agent":"os-iso-downloader-probe/0.1"}
URLS="""https://deb.parrot.sh/parrot/iso/latest/
https://cdimage.deepin.com/releases/25.3.0/amd64/
https://download.tails.net/tails/stable/tails-amd64-7.18/
https://iso.builds.garudalinux.org/iso/latest/garuda/kde-lite/
https://downloads.getsol.us/isos/latest/
https://mirrors.kernel.org/mageia/iso/10/
https://mirrors.kernel.org/mageia/iso/10/Mageia-10-Live-Xfce-x86_64/
https://mirrors.edge.kernel.org/qubes/iso/
https://mirror.alpix.eu/endeavouros/iso/
https://mirror.cachyos.org/ISO/
https://cdn77.cachyos.org/ISO/
https://mirrors.dotsrc.org/artix-linux/iso/
https://mirrors.dotsrc.org/manjaro/
https://download.manjaro.org/
https://mirrors.dotsrc.org/arcolinux/
https://mirror.alpix.eu/
https://download.haiku-os.org/haiku-release/r1beta5/
https://mirrors.dotsrc.org/haiku/
https://cdimage.debian.org/cdimage/ports/latest/hurd-i386/iso-cd/
https://cdimage.debian.org/cdimage/archive/12.11.0/amd64/iso-cd/
https://cdimage.debian.org/cdimage/archive/11.11.0/amd64/iso-cd/
https://cdimage.debian.org/cdimage/archive/10.13.0/amd64/iso-cd/
https://old-releases.ubuntu.com/releases/22.10/
https://releases.ubuntu.com/jammy/
https://releases.ubuntu.com/focal/
https://releases.ubuntu.com/bionic/
https://releases.ubuntu.com/xenial/
https://cdimage.ubuntu.com/lubuntu/releases/26.04/release/
https://cdimage.ubuntu.com/kubuntu/releases/26.04/release/
https://cdimage.ubuntu.com/xubuntu/releases/26.04/release/
https://mirrors.edge.kernel.org/linuxmint/stable/
https://archives.fedoraproject.org/pub/archive/fedora/linux/releases/11/Live/x86_64/
https://archives.fedoraproject.org/pub/archive/fedora/linux/releases/11/Live/i686/
https://dl-cdn.alpinelinux.org/alpine/v3.20/releases/x86_64/
https://mirrors.slackware.com/slackware/slackware-iso/slackware64-14.2-iso/
https://mirrors.slackware.com/slackware/slackware-iso/slackware-15.0-iso/
https://mirrors.slackware.com/slackware/slackware-iso/slackware-14.2-iso/
https://tinycorelinux.net/
http://distro.ibiblio.org/tinycorelinux/16.x/x86/release/
https://distro.ibiblio.org/puppylinux/puppy-bookwormpup/BookwormPup64/10.0.12/
https://ftp.halifax.rwth-aachen.de/knoppix/
https://channels.nixos.org/nixos-26.05/latest-nixos-graphical-x86_64-linux.iso
https://channels.nixos.org/nixos-unstable/latest-nixos-graphical-x86_64-linux.iso.sha256
https://mirror.yandex.ru/rosa/rosa2021.1/iso/
https://download.nobaraproject.org/
https://nomadbsd.org/download/
https://mirror.rosalinux.ru/rosa/
https://mirrors.o2switch.fr/batocera/x86_64/stable/last/
https://mirror.kaosx.us/
https://sourceforge.net/projects/kaos-linux/rss?path=/&limit=10
https://archive.org/advancedsearch.php?q=title%3A%28Mandriva%29+AND+mediatype%3Asoftware&fl%5B%5D=identifier&fl%5B%5D=title&rows=8&output=json
https://archive.org/advancedsearch.php?q=title%3A%28KolibriOS%29+AND+mediatype%3Asoftware&fl%5B%5D=identifier&fl%5B%5D=title&rows=6&output=json
https://archive.org/advancedsearch.php?q=title%3A%28Absolute+Linux%29&fl%5B%5D=identifier&fl%5B%5D=title&rows=6&output=json
https://iso.pop-os.org/24.04/amd64/nvidia/""".split()
for u in URLS:
    try:
        req=urllib.request.Request(u,headers=UA)
        r=urllib.request.urlopen(req,timeout=40)
        b=r.read(400000).decode("utf-8","replace") if not (u.endswith(".iso")) else ""
    except Exception as e:
        print("##",u,"ERR",str(e)[:80]); continue
    if "rss?path" in u:
        links=re.findall(r"<link>([^<]+)</link>",b)[:12]; print("##",u,"RSS",links); continue
    if u.endswith("builds/24.04/intel") or "channels.nixos" in u and False:
        print("##",u,"JSON",b[:300].replace("\n"," ")); continue
    if u.endswith(".iso") or u.endswith(".sha256"):
        print("##",u,"OK-head");continue
    if "advancedsearch" in u:
        print("##",u,b[:700].replace("\n"," "));continue
    hrefs=[h for h in re.findall(r'href=["\']([^"\'#?]+)["\']',b) if not h.startswith(("http","/","..","mailto"))]
    files=[h for h in hrefs if re.search(r"\.(iso|img|bz2|xz|gz|7z|zip)(\.\w+)?$",h,re.I)]
    dirs=[h for h in hrefs if h.endswith("/")]
    print("##",u,"files:",files[-14:],"dirs:",dirs[-8:])
