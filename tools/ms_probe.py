#!/usr/bin/env python3
"""Tries the same requests the Microsoft download page makes (as Fido does) and prints what comes back."""
import json, re, sys, time, uuid, urllib.request, urllib.parse

ORG, PROFILE, INSTANCE = "y6jn8c31", "606624d44113", "560dc9f3-1aa5-4a2f-b63c-9e18f8d0e175"
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0"

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None
opener = urllib.request.build_opener(NoRedirect)

def get(url, referer=None):
    h = {"User-Agent": UA}
    if referer:
        h["Referer"] = referer
    try:
        with opener.open(urllib.request.Request(url, headers=h), timeout=40) as r:
            return r.status, r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")

def session():
    sid = str(uuid.uuid4())
    print("tags", get(f"https://vlscppe.microsoft.com/tags?org_id={ORG}&session_id={sid}")[0])
    st, js = get(f"https://ov-df.microsoft.com/mdt.js?instanceId={INSTANCE}&PageId=si&session_id={sid}")
    w = re.search(r"[?&]w=([A-F0-9]+)", js); rt = re.search(r'rticks\="\+?(\d+)', js)
    print("mdt.js", st, "w" if w else "NO w", "rticks" if rt else "NO rticks", len(js))
    if not (w and rt):
        print(js[:600])
        return None
    st, _ = get(f"https://ov-df.microsoft.com/?session_id={sid}&CustomerId={INSTANCE}&PageId=si&w={w.group(1)}&mdt={int(time.time()*1000)}&rticks={rt.group(1)}")
    print("ov-df reply", st)
    return sid

for name, edition, page in (("Windows 11", 3813, "windows11"), ("Windows 10", 2618, "windows10ISO")):
    print("=====", name)
    sid = session()
    if not sid:
        continue
    url = ("https://www.microsoft.com/software-download-connector/api/getskuinformationbyproductedition"
           f"?profile={PROFILE}&productEditionId={edition}&SKU=undefined&friendlyFileName=undefined&Locale=en-US&sessionID={sid}")
    st, body = get(url)
    print("skus", st, body[:300].replace("\n", " "))
    try:
        d = json.loads(body)
    except Exception:
        continue
    langs = {s["Language"]: s["Id"] for s in d.get("Skus", [])}
    print("languages:", len(langs), list(langs)[:6])
    sku = langs.get("English") or langs.get("English (United States)") or next(iter(langs.values()), None)
    if sku is None:
        continue
    url = ("https://www.microsoft.com/software-download-connector/api/GetProductDownloadLinksBySku"
           f"?profile={PROFILE}&productEditionId=undefined&SKU={sku}&friendlyFileName=undefined&Locale=en-US&sessionID={sid}")
    st, body = get(url, referer=f"https://www.microsoft.com/software-download/{page}")
    print("links", st, body[:900].replace("\n", " "))
