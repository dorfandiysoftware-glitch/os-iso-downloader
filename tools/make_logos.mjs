// Generates PNG logos for the app from simple-icons (CC0 files) and devicon (MIT).
// usage: node make_logos.mjs <outDir>
import * as si from "simple-icons";
import { Resvg } from "@resvg/resvg-js";
import fs from "node:fs";
import path from "node:path";
const out = process.argv[2];
fs.mkdirSync(out, { recursive: true });
const SI = { linux: ["siLinux", "2B2B2B"], bsd: ["siFreebsd"], freebsd: ["siFreebsd"], openbsd: ["siOpenbsd", "C9A200"],
  netbsd: ["siNetbsd"], debian: ["siDebian"], ubuntu: ["siUbuntu"], "fedora-kde": ["siFedora"], mint: ["siLinuxmint"],
  "opensuse-tw": ["siOpensuse"], arch: ["siArchlinux"], alpine: ["siAlpinelinux"], macos: ["siApple"], "macos-guide": ["siApple"], "macos-installers": ["siApple"], "macos-recovery": ["siApple"], "macos-hackintosh": ["siApple"], "macos-arm": ["siApple"],
  android: ["siAndroid"], "android-x86": ["siAndroid"], "android-plain": ["siAndroid"], "macos-apple": ["siApple"],
  "kali": ["siKalilinux"],
  "mx": ["siMxlinux"],
  "proxmox": ["siProxmox"],
  "tails": ["siTails"],
  "deepin": ["siDeepin"],
  "lubuntu": ["siLubuntu"],
  "kubuntu": ["siKubuntu"],
  "xubuntu": ["siXubuntu"],
  "pop": ["siPopos"],
  "zorin": ["siZorin"],
  "elementary": ["siElementary"],
  "qubes": ["siQubesos"],
  "rocky": ["siRockylinux"],
  "almalinux": ["siAlmalinux"],
  "centos": ["siCentos"],
  "redhat": ["siRedhat"],
  "rhel": ["siRedhat"],
  "gentoo": ["siGentoo"],
  "gentoo-self": ["siGentoo"],
  "chromeos": ["siGooglechrome"],
  "chromeos-flex": ["siGooglechrome"],
  "garuda": ["siGarudalinux"],
  "endeavouros": ["siEndeavouros"],
  "manjaro": ["siManjaro"],
  "cachyos": ["siCachyos"],
  "artix": ["siArtixlinux"],
  "steamos": ["siSteam"],
  "slackware": ["siSlackware"],
  "nixos": ["siNixos"],
  "void": ["siVoidlinux"],
  "solus": ["siSolus"],
  "asahi": ["siAsahilinux"],
  "nobara": ["siNobaralinux"],
  "hurd": ["siGnu"],
  "fedora": ["siFedora"],
  "fedora-self": ["siFedora"],
  "fedora-11": ["siFedora"],
  "opensuse": ["siOpensuse"],
  "debian-self": ["siDebian"],
  "ubuntu-self": ["siUbuntu"],
  "arch-self": ["siArchlinux"],
  "alpine-self": ["siAlpinelinux"],
  "mint": ["siLinuxmint"] };
const DEV = { windows: "windows11/windows11-original.svg", win11: "windows11/windows11-original.svg",
  win10: "windows8/windows8-original.svg", win81: "windows8/windows8-original.svg" };
const render = (svg, id) => {
  const png = new Resvg(svg, { fitTo: { mode: "width", value: 128 } }).render().asPng();
  fs.writeFileSync(path.join(out, id + ".png"), png);
  console.log("ok", id, png.length);
};
for (const [id, [key, color]] of Object.entries(SI)) {
  const icon = si[key];
  if (!icon) { console.log("MISSING", id, key); continue; }
  render(icon.svg.replace("<svg ", `<svg fill="#${color || icon.hex}" `), id);
}
for (const [id, file] of Object.entries(DEV)) {
  const f = path.join("node_modules/devicon/icons", file);
  if (!fs.existsSync(f)) { console.log("MISSING", id, file); continue; }
  render(fs.readFileSync(f, "utf8"), id);
}
