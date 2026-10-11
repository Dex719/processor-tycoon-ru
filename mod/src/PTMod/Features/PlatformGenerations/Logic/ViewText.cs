using System;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// What the views of the window and the texts of the notifications need from the outside to speak the player's language and to name things
    /// the way the game does: the string tables, the name of a generation in the current language, the name of a company, the name of a
    /// socket. The pure logic takes it as a whole, so a test can put the real string tables of the feature behind <see cref="Text"/> and a
    /// few names behind the rest, and the game side puts <c>ModL10n</c> and the adapter there. Every member is optional: a missing one falls back
    /// to something readable (the key, the id, the technical key of the company or socket), never to an exception.
    /// </summary>
    public sealed class ViewText
    {
        /// <summary>The text of a key with its arguments (<c>ModL10n.L</c>). Without it the key itself is the text.</summary>
        public Func<string, object[], string> Text;

        /// <summary>The name of a generation in the current language (<c>ModL10n.Resolve(gen.name)</c>). Without it the id.</summary>
        public Func<GenerationDef, string> GenerationName;

        /// <summary>The name of a company by its key (<c>player</c>, <c>ai:3</c>). Without it, or when it answers nothing, the key.</summary>
        public Func<string, string> CompanyName;

        /// <summary>The name of a socket as the player knows it (<c>Package.Name</c> or <c>CustomSocket.Name</c>). Without it the name of the base package, then the key.</summary>
        public Func<SocketId, string> SocketName;

        /// <summary>
        /// A whole number of units the way the game writes pieces (<c>StringFormatter.FloatToIntString</c>, NFR-6). Without it
        /// <see cref="PlatformNumbers.Count"/> (a space between the thousands, the same on every machine).
        /// </summary>
        public Func<double, string> CountText;

        /// <summary>The text of a key; the key itself when there are no tables.</summary>
        public string L(string key, params object[] args)
        {
            if (Text == null) return key;
            string text = Text(key, args ?? new object[0]);
            return text ?? key;
        }

        /// <summary>A number of units written by <see cref="CountText"/>; by <see cref="PlatformNumbers.Count"/> when there is none or it gives nothing.</summary>
        public string Count(double units)
        {
            string text = CountText == null ? null : CountText(units);
            return string.IsNullOrEmpty(text) ? PlatformNumbers.Count(units) : text;
        }

        /// <summary>The name of the generation; the id when no name is known; empty for no generation.</summary>
        public string Gen(GenerationDef gen)
        {
            if (gen == null) return string.Empty;
            string name = GenerationName == null ? null : GenerationName(gen);
            return string.IsNullOrEmpty(name) ? gen.id : name;
        }

        /// <summary>The name of the company; the key when it has no name; empty for no key.</summary>
        public string Company(string companyKey)
        {
            if (string.IsNullOrEmpty(companyKey)) return string.Empty;
            string name = CompanyName == null ? null : CompanyName(companyKey);
            return string.IsNullOrEmpty(name) ? companyKey : name;
        }

        /// <summary>The name of the socket; the name of its base package, or the key, when nothing better is known; empty for a socket without a key.</summary>
        public string Socket(SocketId socket)
        {
            if (!socket.IsValid) return string.Empty;
            string name = SocketName == null ? null : SocketName(socket);
            if (!string.IsNullOrEmpty(name)) return name;
            return string.IsNullOrEmpty(socket.BasePackageName) ? socket.Key : socket.BasePackageName;
        }
    }
}
