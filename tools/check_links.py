#!/usr/bin/env python3
"""Opens every link in the catalog (HEAD, then GET) and for `resolve` entries finds the newest file.
Prints a report; exit code 1 only if a verified entry is broken."""
import json, re, sys, urllib.request, urllib.parse

UA = {"User-Agent": "os-iso-downloader-linkcheck/1"}

def open_url(url, method="HEAD"):
    req = urllib.request.Request(url, headers=UA, method=method)
    return urllib.request.urlopen(req, timeout=40)

def alive(url):
    for m in ("HEAD", "GET"):
        try:
            with open_url(url, m) as r:
                return r.status
        except Exception as e:
            last = e
    return str(last)

def natural(s):
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", s)]

def walk(n):
    for f in n.get("files", []):
        yield n, f
    for c in n.get("children", []):
        yield from walk(c)

d = json.load(open("catalog/catalog.json", encoding="utf-8"))
bad_verified = 0
for s in d["systems"]:
    for node, f in walk(s):
        url, note = f.get("url"), ""
        if not url and f.get("resolve"):
            r = f["resolve"]
            try:
                html = urllib.request.urlopen(urllib.request.Request(r["base"], headers=UA), timeout=40).read().decode("utf-8", "replace")
                names = set()
                for m in re.finditer(r"href=[\"']([^\"'#?]+)[\"']", html, re.I):
                    nm = urllib.parse.unquote(m.group(1).rstrip("/").split("/")[-1])
                    if re.fullmatch(r["pattern"], nm, re.I) and not (r.get("exclude") and re.search(r["exclude"], nm, re.I)):
                        names.add(nm)
                if not names:
                    status = "NO MATCH"
                else:
                    newest = sorted(names, key=natural)[-1]
                    url = r["base"] + urllib.parse.quote(newest)
                    status = alive(url)
                    note = f" -> {newest}"
            except Exception as e:
                status = f"ERR {e}"
        else:
            status = alive(url) if url else "NO URL"
        ok = status in (200, 206, 301, 302)
        flag = "OK " if ok else "BAD"
        print(f"{flag} [{node['id']}] {f['name']}: {status}{note}  (verified={f.get('verified', False)})")
        if not ok and f.get("verified"):
            bad_verified += 1
sys.exit(1 if bad_verified else 0)
