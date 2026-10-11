using System;
using System.Globalization;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The identity of a CPU's socket as the models see it (design D4): a standard <c>Package</c> is a common standard, a <c>CustomSocket</c> is the
    /// closed platform of one company. Built by <see cref="SocketIdentity"/> from plain values the adapter reads off the CPU.
    /// </summary>
    public struct SocketId
    {
        /// <summary>The stable key: <c>pkg:PGA 1995</c> for a standard package, <c>custom:5120</c> for a custom socket (its <c>CustomHardwareID</c>).</summary>
        public string Key;

        /// <summary>The name of the base package: the package itself for a standard socket, <c>CustomSocket.BasePackage.Name</c> for a custom one. Null = unknown, the CPU is outside the generations.</summary>
        public string BasePackageName;

        /// <summary>True for a standard package (<c>!IPackage.IsCustomized</c>).</summary>
        public bool IsStandard;

        /// <summary>The socket has a key (it was built by <see cref="SocketIdentity"/>).</summary>
        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(Key); }
        }

        public override string ToString()
        {
            return Key ?? string.Empty;
        }
    }

    /// <summary>Makes socket keys and answers whether two sockets are compatible (FR-6 item 3). Pure logic.</summary>
    public static class SocketIdentity
    {
        /// <summary>The key prefix of a standard package.</summary>
        public const string StandardPrefix = "pkg:";

        /// <summary>The key prefix of a custom socket.</summary>
        public const string CustomPrefix = "custom:";

        /// <summary>A standard socket: the key is <c>pkg:&lt;package name&gt;</c> and the package is its own base. A null or empty name gives an invalid id (no key).</summary>
        public static SocketId Standard(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return new SocketId { IsStandard = true };
            return new SocketId { Key = StandardPrefix + packageName, BasePackageName = packageName, IsStandard = true };
        }

        /// <summary>
        /// A custom socket: the key is <c>custom:&lt;CustomHardwareID&gt;</c>. <paramref name="basePackageName"/> may be null when the base cannot
        /// be read (the socket then belongs to no generation and is compatible only with itself).
        /// </summary>
        public static SocketId Custom(int customHardwareId, string basePackageName)
        {
            return new SocketId
            {
                Key = CustomPrefix + customHardwareId.ToString(CultureInfo.InvariantCulture),
                BasePackageName = string.IsNullOrEmpty(basePackageName) ? null : basePackageName,
                IsStandard = false,
            };
        }

        /// <summary>
        /// Two sockets are compatible (FR-6 item 3) when they are the same socket (the same key), or have the same base package (a standard
        /// package and a custom socket built on it, or two custom sockets on one base) while <c>customOnSameBaseIsCompatible</c> is set, or when
        /// the pair of their base packages is in <c>compatibleSockets</c> of some generation. A socket without a key or without a base is
        /// compatible with nothing but itself. <paramref name="catalog"/> and <paramref name="balance"/> may be null (no declared pairs; the
        /// switch counts as set).
        /// </summary>
        public static bool Compatible(SocketId a, SocketId b, GenerationCatalog catalog, PlatformBalance balance)
        {
            if (!a.IsValid || !b.IsValid) return false;
            if (string.Equals(a.Key, b.Key, StringComparison.Ordinal)) return true;
            if (string.IsNullOrEmpty(a.BasePackageName) || string.IsNullOrEmpty(b.BasePackageName)) return false;

            if (string.Equals(a.BasePackageName, b.BasePackageName, StringComparison.Ordinal))
                return balance == null || balance.Rating.CustomOnSameBaseIsCompatible;

            return catalog != null && catalog.IsDeclaredCompatible(a.BasePackageName, b.BasePackageName);
        }
    }
}
