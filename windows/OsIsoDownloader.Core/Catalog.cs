// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsIsoDownloader.Core
{
    /// <summary>The list of systems: a tree of nodes, the leaves carry the files.</summary>
    public sealed class Catalog
    {
        public int Schema { get; set; }
        public string Updated { get; set; }
        public List<Node> Systems { get; set; } = new List<Node>();

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static Catalog Parse(string json)
        {
            Catalog c = JsonSerializer.Deserialize<Catalog>(json, Options);
            if (c == null || c.Systems == null)
            {
                throw new FormatException("The catalog is empty.");
            }
            if (c.Schema != 1)
            {
                throw new FormatException("Unsupported catalog schema " + c.Schema + ".");
            }
            return c;
        }
    }

    /// <summary>
    /// One step of the choice (system, family, version, build...). A node has either children
    /// to choose from or files to download.
    /// </summary>
    public sealed class Node
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Note { get; set; }
        /// <summary>What the next step is called, e.g. "Версія" or "Білд".</summary>
        public string ChildrenTitle { get; set; }
        public List<Node> Children { get; set; } = new List<Node>();
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
        /// <summary>Settings for a virtual machine made from files under this node.</summary>
        public VmHint Vm { get; set; }
        /// <summary>"apple": the files are fetched from Apple's catalog when the node is opened.</summary>
        public string Dynamic { get; set; }

        [JsonIgnore]
        public bool HasChildren => Children != null && Children.Count > 0;
    }

    public sealed class FileEntry
    {
        /// <summary>What the person sees, e.g. "netinst (мінімальний)".</summary>
        public string Name { get; set; }
        public string Arch { get; set; }
        public string Lang { get; set; }
        /// <summary>"iso" downloads; "archived" downloads but is marked as old; "guide" opens a page.</summary>
        public string Kind { get; set; } = "iso";
        public string Url { get; set; }
        public Resolve Resolve { get; set; }
        public Checksum Checksum { get; set; }
        public long Size { get; set; }
        /// <summary>The official page the link was taken from.</summary>
        public string Source { get; set; }
        /// <summary>True only when somebody saw this exact link on the official page.</summary>
        public bool Verified { get; set; }
        public string Note { get; set; }
        public bool Recommended { get; set; }
        /// <summary>The download is a .zip; unpack it and use the image inside.</summary>
        public bool Unzip { get; set; }

        [JsonIgnore]
        public bool IsGuide => string.Equals(Kind, "guide", StringComparison.OrdinalIgnoreCase);
        [JsonIgnore]
        public bool IsArchived => string.Equals(Kind, "archived", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class VmHint
    {
        /// <summary>VirtualBox OS type id, e.g. "Debian_64".</summary>
        public string VboxType { get; set; } = "Other_64";
        public int Ram { get; set; } = 2048;
        public int Cpus { get; set; } = 2;
        public int Disk { get; set; } = 20;
        public bool Efi { get; set; }
        /// <summary>Use virtio devices in QEMU (Linux guests).</summary>
        public bool Virtio { get; set; }
        /// <summary>Old guests (DOS, XP): IDE disk, plain VGA, no virtio.</summary>
        public bool Legacy { get; set; }
    }

    /// <summary>Finds the newest file in a directory listing, so a new release needs no new catalog.</summary>
    public sealed class Resolve
    {
        /// <summary>"index" (a directory page) or "archive" (an archive.org item).</summary>
        public string Type { get; set; } = "index";
        /// <summary>archive.org item identifier, for type "archive".</summary>
        public string Id { get; set; }
        /// <summary>The directory listing, ending with a slash.</summary>
        public string Base { get; set; }
        /// <summary>Regular expression for the file name.</summary>
        public string Pattern { get; set; }
        /// <summary>Regular expression of names to skip, optional.</summary>
        public string Exclude { get; set; }
    }

    public sealed class Checksum
    {
        /// <summary>"sha1", "sha256" or "sha512".</summary>
        public string Algo { get; set; } = "sha256";
        /// <summary>The hash itself, when it never changes.</summary>
        public string Value { get; set; }
        /// <summary>A file with the hashes. "{file}" is replaced with the file name.</summary>
        public string Url { get; set; }
    }
}
