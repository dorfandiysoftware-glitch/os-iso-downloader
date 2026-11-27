#!/usr/bin/env python3
"""Opens every link in the catalog the way the app would and reports problems.
- resolves `resolve` rules (directory pages and archive.org items);
- makes sure a download link gives a file, not a web page;
- finds the hash in the checksum file (same rules as ChecksumParser.cs).
Exit code 1 only if a verified entry is broken."""
import json, re, sys, urllib.request, urllib.parse

UA = {"User-Agent": "os-iso-downloader/0.1"}

def req(url, method="GET", headers=None):
    h = dict(UA)
    h.update(headers or {})
    import os
    if url.startswith("https://api.github.com/") and os.environ.get("GITHUB_TOKEN"):
        h["Authorization"] = "Bearer " + os.environ["GITHUB_TOKEN"]
    return urllib.request.urlopen(urllib.request.Request(url, headers=h, method=method), timeout=60)

def probe(url, is_download):
    """Returns (ok, text). GETs only the first byte for downloads."""
    try:
        with req(url, "GET", {"Range": "bytes=0-0"} if is_download else None) as r:
            ctype = r.headers.get_content_type()
            if is_download and ctype == "text/html":
                return False, "server returns a web page (text/html), not a file"
            return True, f"{r.status} {ctype}"
    except Exception as e:
        return False, str(e)

def natural(s):
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", s)]

LEN = {'md5': 32, 'sha1': 40, 'sha256': 64, 'sha512': 128}

def find_hash(text, name, algo=None):
    want = LEN.get((algo or '').lower(), 0)
    only, lines, entries, last = None, 0, 0, None
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith(("#", "-----", "Hash:")):
            continue
        m = re.match(r"^[A-Za-z0-9-]+\s*\((.+)\)\s*=\s*([0-9A-Fa-f]{32,})$", line)
        if m and want and len(m.group(2)) != want:
            continue
        if m:
            lines += 1
            entries += 1; last = m.group(2).lower()
            if m.group(1).strip().replace("\\", "/").split("/")[-1].lower() == name.lower():
                return last
            continue
        m = re.match(r"^([0-9A-Fa-f]{32,})\s+\*?(.+)$", line)
        if m and want and len(m.group(1)) != want:
            continue
        if m:
            lines += 1
            entries += 1; last = m.group(1).lower()
            if m.group(2).strip().replace("\\", "/").split("/")[-1].lower() == name.lower():
                return last
            continue
        if re.fullmatch(r"[0-9A-Fa-f]{32,}", line) and (not want or len(line) == want):
            lines += 1
            only = line.lower()
    if lines == 1 and only:
        return only
    return last if entries == 1 and lines == 1 else None

def walk(n):
    for f in n.get("files", []):
        yield n, f
    for c in n.get("children", []):
        yield from walk(c)

def resolve(f):
    """Returns (url, note, sha1_from_archive)."""
    r = f["resolve"]
    pat = re.compile("^(" + r["pattern"] + ")$", re.I)
    exc = re.compile(r["exclude"], re.I) if r.get("exclude") else None
    if r.get("type", "index") == "archive":
        meta = json.load(req("https://archive.org/metadata/" + urllib.parse.quote(r["id"]) + "/files"))
        names = {x["name"]: x for x in meta.get("result", []) if pat.match(x["name"]) and not (exc and exc.search(x["name"]))}
        if not names:
            raise RuntimeError("no file matches " + r["pattern"])
        best = sorted(names, key=natural)[-1]
        url = "https://archive.org/download/" + urllib.parse.quote(r["id"]) + "/" + "/".join(urllib.parse.quote(p) for p in best.split("/"))
        return url, f" -> {best}", names[best].get("sha1")
    if r.get("type") == "github":
        rel = json.load(req("https://api.github.com/repos/" + r["id"] + "/releases/latest"))
        hits = [a for a in rel.get("assets", []) if pat.match(a["name"])]
        if not hits:
            raise RuntimeError("no release asset matches " + r["pattern"])
        return hits[0]["browser_download_url"], f" -> {hits[0]['name']}", None
    html = req(r["base"]).read().decode("utf-8", "replace")
    names = set()
    for m in re.finditer(r"href=[\"']([^\"'#?]+)[\"']", html, re.I):
        nm = urllib.parse.unquote(m.group(1).rstrip("/").split("/")[-1])
        if pat.match(nm) and not (exc and exc.search(nm)):
            names.add(nm)
    if not names:
        raise RuntimeError("no file matches " + r["pattern"])
    best = sorted(names, key=natural)[-1]
    return r["base"] + urllib.parse.quote(best), f" -> {best}", None

d = json.load(open("catalog/catalog.json", encoding="utf-8"))
bad_verified = 0
for s in d["systems"]:
    for node, f in walk(s):
        url, note, ok = f.get("url"), "", True
        status = ""
        try:
            if not url and f.get("resolve"):
                url, note, _ = resolve(f)
            guide = f.get("kind") == "guide"
            ok, status = probe(url, not guide) if url else (False, "NO URL")
            c = f.get("checksum")
            if ok and c and not guide:
                if c.get("value"):
                    note += " [hash: value]"
                elif c.get("url"):
                    fname = urllib.parse.unquote(url.rstrip("/").split("/")[-1])
                    txt = req(c["url"].replace("{file}", fname)).read().decode("utf-8", "replace")
                    h = find_hash(txt, fname, c.get('algo', 'sha256'))
                    if h:
                        note += f" [hash ok {h[:10]}…]"
                    else:
                        ok, status = False, f"checksum file has no hash for {fname}"
            elif ok and not guide and f.get("resolve", {}).get("type") == "archive":
                note += " [hash: archive.org sha1]"
        except Exception as e:
            ok, status = False, str(e)
        flag = "OK " if ok else "BAD"
        print(f"{flag} [{node['id']}] {f['name']}: {status}{note}  (verified={f.get('verified', False)})")
        if not ok and f.get("verified"):
            bad_verified += 1
sys.exit(1 if bad_verified else 0)
