#!/usr/bin/env python3
"""Checks the structure of catalog/catalog.json."""
import json, re, sys

KINDS = {"iso", "archived", "guide"}
errors = []

def err(path, msg):
    errors.append(f"{path}: {msg}")

def check_file(f, path):
    for k in ("name",):
        if not f.get(k):
            err(path, f"missing {k}")
    kind = f.get("kind", "iso")
    if kind not in KINDS:
        err(path, f"bad kind {kind}")
    has_url, has_res = bool(f.get("url")), bool(f.get("resolve"))
    if not (has_url or has_res):
        err(path, "needs url or resolve")
    if has_url and not f["url"].startswith("https://"):
        err(path, "url must be https")
    if has_res:
        r = f["resolve"]
        if r.get("type", "index") == "archive":
            if not r.get("id"):
                err(path, "archive resolve needs id")
        elif not r.get("base", "").startswith("https://") or not r["base"].endswith("/"):
            err(path, "resolve.base must be https and end with /")
        try:
            re.compile(r.get("pattern", ""))
            if r.get("exclude"):
                re.compile(r["exclude"])
        except re.error as e:
            err(path, f"bad regex: {e}")
    c = f.get("checksum")
    if c:
        if c.get("algo", "sha256") not in ("sha1", "sha256", "sha512"):
            err(path, "bad checksum algo")
        if not (c.get("value") or c.get("url")):
            err(path, "checksum needs value or url")
    if kind == "guide" and has_res:
        err(path, "guide cannot use resolve")
    if f.get("verified") and not f.get("source"):
        err(path, "verified entries need source")

def walk(node, path, ids):
    nid = node.get("id")
    if not nid or not node.get("name"):
        err(path, "node needs id and name")
    elif nid in ids:
        err(path, f"duplicate id {nid}")
    else:
        ids.add(nid)
    kids, files = node.get("children", []), node.get("files", [])
    if kids and files:
        err(path, "node has both children and files")
    if not kids and not files:
        err(path, "node is empty")
    for i, f in enumerate(files):
        check_file(f, f"{path}/files[{i}]")
    for k in kids:
        walk(k, f"{path}/{k.get('id')}", ids)

d = json.load(open("catalog/catalog.json", encoding="utf-8"))
if d.get("schema") != 1:
    err("root", "schema must be 1")
ids = set()
for s in d.get("systems", []):
    walk(s, s.get("id", "?"), ids)
if errors:
    print("\n".join(errors))
    sys.exit(1)
print("catalog ok:", len(ids), "nodes")
