// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OsIsoDownloader.Core
{
    public sealed class VmSpec
    {
        public string Name { get; set; }
        public string IsoPath { get; set; }
        /// <summary>Folder that will hold the machine's own folder.</summary>
        public string Folder { get; set; }
        public int RamMb { get; set; } = 2048;
        public int Cpus { get; set; } = 2;
        public int DiskGb { get; set; } = 20;
        public bool Efi { get; set; }
        public bool Virtio { get; set; }
        public bool Legacy { get; set; }
        public string VboxType { get; set; } = "Other_64";

        public string VmDir => Path.Combine(Folder, SafeName(Name));
        public string DiskPath(string ext) => Path.Combine(VmDir, SafeName(Name) + ext);

        public static string SafeName(string n)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                n = n.Replace(c, '_');
            }
            n = n.Trim();
            return n.Length == 0 ? "VM" : n;
        }

        public static VmSpec From(VmHint h, string name, string iso, string folder)
        {
            h = h ?? new VmHint();
            return new VmSpec
            {
                Name = name, IsoPath = iso, Folder = folder, RamMb = h.Ram, Cpus = h.Cpus,
                DiskGb = h.Disk, Efi = h.Efi, Virtio = h.Virtio, Legacy = h.Legacy, VboxType = h.VboxType,
            };
        }
    }

    /// <summary>Builds the command lines. Pure functions, so they can be tested without VirtualBox or QEMU.</summary>
    public static class VmCommands
    {
        public static List<string[]> VBox(VmSpec s, bool start)
        {
            string n = VmSpec.SafeName(s.Name);
            string ctl = s.Legacy ? "IDE" : "SATA";
            var list = new List<string[]>
            {
                new[] { "createvm", "--name", n, "--ostype", s.VboxType, "--register", "--basefolder", s.Folder },
                new[]
                {
                    "modifyvm", n, "--memory", s.RamMb.ToString(), "--cpus", s.Cpus.ToString(), "--vram", "128",
                    "--graphicscontroller", s.Legacy ? "vboxvga" : "vmsvga", "--firmware", s.Efi ? "efi" : "bios", "--ioapic", "on",
                    "--nic1", "nat", "--mouse", "usbtablet", "--clipboard-mode", "bidirectional",
                    "--boot1", "dvd", "--boot2", "disk", "--boot3", "none", "--boot4", "none",
                },
                new[] { "createmedium", "disk", "--filename", s.DiskPath(".vdi"), "--size", (s.DiskGb * 1024).ToString(), "--format", "VDI" },
                s.Legacy
                    ? new[] { "storagectl", n, "--name", ctl, "--add", "ide", "--controller", "PIIX4" }
                    : new[] { "storagectl", n, "--name", ctl, "--add", "sata", "--controller", "IntelAhci" },
                new[] { "storageattach", n, "--storagectl", ctl, "--port", "0", "--device", "0", "--type", "hdd", "--medium", s.DiskPath(".vdi") },
                new[] { "storageattach", n, "--storagectl", ctl, "--port", s.Legacy ? "1" : "1", "--device", "0", "--type", "dvddrive", "--medium", s.IsoPath },
            };
            if (start)
            {
                list.Add(new[] { "startvm", n });
            }
            return list;
        }

        public static string[] QemuImg(VmSpec s)
        {
            return new[] { "create", "-f", "qcow2", s.DiskPath(".qcow2"), s.DiskGb + "G" };
        }

        /// <param name="ovmfCode">path to edk2-x86_64-code.fd, used only when UEFI is wanted</param>
        public static string[] QemuRun(VmSpec s, string ovmfCode)
        {
            var a = new List<string>
            {
                "-machine", s.Legacy ? "pc" : "q35", "-m", s.RamMb.ToString(), "-smp", s.Cpus.ToString(),
                "-accel", "whpx,kernel-irqchip=off", "-accel", "tcg",
                "-drive", "file=" + s.DiskPath(".qcow2") + ",format=qcow2,if=" + (s.Virtio ? "virtio" : "ide"),
                "-drive", "file=" + s.IsoPath + ",media=cdrom,readonly=on",
                "-boot", "order=dc",
                "-device", s.Virtio ? "virtio-vga" : "VGA",
                "-device", "qemu-xhci", "-device", "usb-tablet",
                "-nic", "user,model=" + (s.Virtio ? "virtio-net-pci" : s.Legacy ? "rtl8139" : "e1000"),
            };
            if (s.Efi && !string.IsNullOrEmpty(ovmfCode))
            {
                a.Add("-drive");
                a.Add("if=pflash,format=raw,readonly=on,file=" + ovmfCode);
            }
            return a.ToArray();
        }

        /// <summary>Quotes one argument for a .cmd file.</summary>
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"', '&', '(', ')', '^', ',', ';' }) < 0)
            {
                return arg;
            }
            return "\"" + arg.Replace("\"", "\"\"") + "\"";
        }

        public static string QemuScript(string qemuExe, string[] args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.Append(Quote(qemuExe));
            foreach (string a in args)
            {
                sb.Append(' ').Append(Quote(a));
            }
            sb.AppendLine();
            return sb.ToString();
        }
    }

    public sealed class RunResult
    {
        public int Code;
        public string Output;
    }

    /// <summary>Finds VirtualBox and QEMU and runs them.</summary>
    public static class VmTools
    {
        public static string FindVBoxManage()
        {
            var candidates = new List<string>();
            string msi = Environment.GetEnvironmentVariable("VBOX_MSI_INSTALL_PATH");
            if (!string.IsNullOrEmpty(msi))
            {
                candidates.Add(Path.Combine(msi, "VBoxManage.exe"));
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Oracle", "VirtualBox", "VBoxManage.exe"));
            return FirstExisting(candidates) ?? OnPath("VBoxManage.exe");
        }

        /// <summary>A folder chosen by the person, tried first.</summary>
        public static string QemuDir { get; set; }

        public static string FindQemu(string exe)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(QemuDir))
            {
                candidates.Add(Path.Combine(QemuDir, exe));
            }
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    foreach (string sub in new[] { @"SOFTWARE\QEMU", @"SOFTWARE\WOW6432Node\QEMU" })
                    {
                        using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(sub))
                        {
                            string dir = k?.GetValue("Install_Dir") as string;
                            if (!string.IsNullOrEmpty(dir))
                            {
                                candidates.Add(Path.Combine(dir, exe));
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "qemu", exe));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "qemu", exe));
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(profile, "scoop", "apps", "qemu", "current", exe));
            candidates.Add(Path.Combine(@"C:\msys64\ucrt64\bin", exe));
            candidates.Add(Path.Combine(@"C:\msys64\mingw64\bin", exe));
            return FirstExisting(candidates) ?? OnPath(exe);
        }

        public static string FindOvmf(string qemuExe)
        {
            if (qemuExe == null)
            {
                return null;
            }
            string f = Path.Combine(Path.GetDirectoryName(qemuExe), "share", "edk2-x86_64-code.fd");
            return File.Exists(f) ? f : null;
        }

        private static string FirstExisting(IEnumerable<string> list)
        {
            foreach (string p in list)
            {
                if (File.Exists(p))
                {
                    return p;
                }
            }
            return null;
        }

        private static string OnPath(string exe)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    string f = Path.Combine(dir.Trim('"'), exe);
                    if (File.Exists(f))
                    {
                        return f;
                    }
                }
                catch (ArgumentException)
                {
                }
            }
            return null;
        }

        public static async Task<RunResult> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }
            using (var p = Process.Start(psi))
            {
                Task<string> o = p.StandardOutput.ReadToEndAsync();
                Task<string> e = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync(ct).ConfigureAwait(false);
                return new RunResult { Code = p.ExitCode, Output = (await o + await e).Trim() };
            }
        }

        /// <summary>VirtualBox rejects unknown OS types, so check the list and fall back to Other_64.</summary>
        public static async Task<string> PickVBoxType(string vbm, string wanted, CancellationToken ct)
        {
            try
            {
                RunResult r = await RunAsync(vbm, new[] { "list", "ostypes" }, ct).ConfigureAwait(false);
                foreach (string line in r.Output.Split('\n'))
                {
                    string t = line.Trim();
                    if (t.StartsWith("ID:") && t.Substring(3).Trim() == wanted)
                    {
                        return wanted;
                    }
                }
            }
            catch (Exception)
            {
            }
            return "Other_64";
        }

        /// <summary>Makes the machine in VirtualBox. Returns the log; throws if a step fails.</summary>
        public static async Task<string> CreateVBoxAsync(string vbm, VmSpec spec, bool start, CancellationToken ct)
        {
            spec.VboxType = await PickVBoxType(vbm, spec.VboxType, ct).ConfigureAwait(false);
            Directory.CreateDirectory(spec.Folder);
            var log = new StringBuilder();
            foreach (string[] step in VmCommands.VBox(spec, start))
            {
                RunResult r = await RunAsync(vbm, step, ct).ConfigureAwait(false);
                log.AppendLine("> VBoxManage " + step[0] + " …").AppendLine(r.Output);
                if (r.Code != 0)
                {
                    throw new InvalidOperationException("VBoxManage " + step[0] + " (код " + r.Code + "): " + r.Output);
                }
            }
            return log.ToString();
        }

        /// <summary>Makes the disk and a run.cmd for QEMU, and optionally starts it.</summary>
        public static async Task<string> CreateQemuAsync(string qemu, string qemuImg, VmSpec spec, bool start, CancellationToken ct)
        {
            Directory.CreateDirectory(spec.VmDir);
            RunResult r = await RunAsync(qemuImg, VmCommands.QemuImg(spec), ct).ConfigureAwait(false);
            if (r.Code != 0)
            {
                throw new InvalidOperationException("qemu-img (код " + r.Code + "): " + r.Output);
            }
            string[] args = VmCommands.QemuRun(spec, FindOvmf(qemu));
            string script = Path.Combine(spec.VmDir, "run.cmd");
            File.WriteAllText(script, VmCommands.QemuScript(qemu, args), new UTF8Encoding(false));
            if (start)
            {
                var psi = new ProcessStartInfo(qemu) { UseShellExecute = false, WorkingDirectory = spec.VmDir };
                foreach (string a in args)
                {
                    psi.ArgumentList.Add(a);
                }
                Process.Start(psi);
            }
            return "Диск: " + spec.DiskPath(".qcow2") + "\nЗапуск: " + script;
        }
    }
}
