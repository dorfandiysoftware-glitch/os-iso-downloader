#!/usr/bin/env python3
import re, sys, urllib.request, urllib.parse
UA={"User-Agent":"os-iso-downloader-probe/0.1"}
URLS="""https://cdimage.kali.org/current/
https://mirrors.slackware.com/slackware/slackware-iso/
https://slackware.osuosl.org/slackware64-current-iso/
https://download.manjaro.org/kde/
https://ftp.halifax.rwth-aachen.de/knoppix/
https://distro.ibiblio.org/puppylinux/
https://distro.ibiblio.org/puppylinux/puppy-bookwormpup/BookwormPup64/
https://iso.pop-os.org/24.04/amd64/intel/
https://api.pop-os.org/builds/24.04/intel
https://channels.nixos.org/nixos-25.11/
https://tinycorelinux.net/16.x/x86/release/
https://mirror.yandex.ru/calculate/release/
https://download.sourcemage.org/image/
https://mirrors.o2switch.fr/batocera/x86_64/stable/last/
https://mirror-master.dragonflybsd.org/iso-images/
https://downloads.omnios.org/media/stable/
https://distfiles.adelielinux.org/adelie/1.0/iso/
https://download.haiku-os.org/
https://cdn.haiku-os.org/haiku-release/
https://builds.kolibrios.org/en_US/
https://repo-default.voidlinux.org/live/current/
https://cdimage.ubuntu.com/lubuntu/releases/
https://cdimage.ubuntu.com/kubuntu/releases/
https://cdimage.ubuntu.com/xubuntu/releases/
https://enterprise.proxmox.com/iso/
https://deb.parrot.sh/parrot/iso/
https://cdimage.deepin.com/releases/
https://download.tails.net/tails/stable/
https://mirrors.edge.kernel.org/qubes/iso/
https://iso.artixlinux.org/iso/
https://mirror.alpix.eu/endeavouros/iso/
https://iso.builds.garudalinux.org/iso/latest/garuda/
https://cdn77.cachyos.org/ISO/desktop/
https://ftp.halifax.rwth-aachen.de/blackarch/iso/
https://mirror.accum.se/mirror/arcolinux/iso/
https://download.nobaraproject.org/
https://downloads.getsol.us/isos/
https://mirror.rosalinux.ru/rosa/rosa2021.1/iso/
https://mirrors.kernel.org/mageia/iso/
https://cdimage.debian.org/cdimage/ports/latest/hurd-i386/
https://cdimage.debian.org/cdimage/archive/
https://old-releases.ubuntu.com/releases/
https://releases.ubuntu.com/
https://cdimage.ubuntu.com/releases/
https://download.manjaro.org/xfce/
https://sourceforge.net/projects/mx-linux/rss?path=/Final/Xfce&limit=40
https://sourceforge.net/projects/zorin-os/rss?path=/&limit=40
https://sourceforge.net/projects/kaos/rss?path=/&limit=40
https://sourceforge.net/projects/rebornos/rss?path=/&limit=40
https://sourceforge.net/projects/mabox-linux/rss?path=/&limit=40
https://sourceforge.net/projects/archcraft/rss?path=/&limit=40
https://sourceforge.net/projects/redcorelinux/rss?path=/&limit=40
https://sourceforge.net/projects/nomadbsd/rss?path=/&limit=40
https://mirrors.slackware.com/slackware/slackware-iso/slackware64-15.0-iso/
https://archive.mandriva.com/
https://archives.fedoraproject.org/pub/archive/fedora/linux/releases/""".split()
for u in URLS:
    try:
        req=urllib.request.Request(u,headers=UA)
        b=urllib.request.urlopen(req,timeout=40).read().decode("utf-8","replace")
    except Exception as e:
        print("##",u,"ERR",str(e)[:80]); continue
    if "rss?path" in u:
        links=re.findall(r"<link>([^<]+)</link>",b)[:12]; print("##",u,"RSS",links); continue
    if u.endswith("builds/24.04/intel") or "channels.nixos" in u and False:
        print("##",u,"JSON",b[:300].replace("\n"," ")); continue
    hrefs=[h for h in re.findall(r'href=["\']([^"\'#?]+)["\']',b) if not h.startswith(("http","/","..","mailto"))]
    files=[h for h in hrefs if re.search(r"\.(iso|img|bz2|xz|gz|7z|zip)(\.\w+)?$",h,re.I)]
    dirs=[h for h in hrefs if h.endswith("/")]
    print("##",u,"files:",files[:22],"dirs:",dirs[-12:])
