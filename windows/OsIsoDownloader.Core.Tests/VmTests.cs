// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using OsIsoDownloader.Core;
using Xunit;

namespace OsIsoDownloader.Core.Tests
{
    public class VmTests
    {
        private static VmSpec Spec(bool efi = false, bool virtio = false) => new VmSpec
        {
            Name = "Debian 13", IsoPath = @"C:\ISO\d.iso", Folder = @"C:\VMs", RamMb = 4096, Cpus = 4,
            DiskGb = 30, Efi = efi, Virtio = virtio, VboxType = "Debian_64",
        };

        [Fact]
        public void VBoxStepsInOrder()
        {
            var steps = VmCommands.VBox(Spec(), false);
            Assert.Equal(new[] { "createvm", "modifyvm", "createmedium", "storagectl", "storageattach", "storageattach" },
                steps.Select(x => x[0]).ToArray());
            Assert.Contains("4096", steps[1]);
            Assert.Contains("bios", steps[1]);
            Assert.Contains((30 * 1024).ToString(), steps[2]);
            Assert.Contains(@"C:\ISO\d.iso", steps[5]);
        }

        [Fact]
        public void VBoxEfiAndStart()
        {
            var steps = VmCommands.VBox(Spec(efi: true), true);
            Assert.Contains("efi", steps[1]);
            Assert.Equal("startvm", steps.Last()[0]);
        }

        [Fact]
        public void QemuVirtioSwitch()
        {
            string[] a = VmCommands.QemuRun(Spec(virtio: true), null);
            Assert.Contains("virtio-vga", a);
            string[] b = VmCommands.QemuRun(Spec(), null);
            Assert.Contains("VGA", b);
            Assert.Contains("file=C:\\VMs\\Debian 13\\Debian 13.qcow2,format=qcow2,if=ide", b);
        }

        [Fact]
        public void QemuUefiOnlyWithFirmwareFile()
        {
            Assert.DoesNotContain(VmCommands.QemuRun(Spec(efi: true), null), x => x.Contains("pflash"));
            Assert.Contains(VmCommands.QemuRun(Spec(efi: true), @"C:\q\share\e.fd"), x => x.Contains("pflash"));
        }

        [Fact]
        public void ScriptQuotesSpaces()
        {
            string s = VmCommands.QemuScript(@"C:\Program Files\qemu\q.exe", new[] { "-m", "1024", "-drive", "file=C:\\a b\\x.img" });
            Assert.Contains("\"C:\\Program Files\\qemu\\q.exe\"", s);
            Assert.Contains("\"file=C:\\a b\\x.img\"", s);
            Assert.Contains(" -m 1024 ", s);
        }

        [Fact]
        public void NameIsMadeSafe()
        {
            Assert.Equal("a_b", VmSpec.SafeName("a:b"));
            Assert.Equal("VM", VmSpec.SafeName("  "));
        }
    }
}
