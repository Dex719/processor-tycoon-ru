using System;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The key of every table "company x socket" in the models (the installed base, the socket appearances): two strings compared ordinally,
    /// hashed without an allocation. Internal: the models do not show it.
    /// </summary>
    internal struct CompanySocketKey : IEquatable<CompanySocketKey>
    {
        public readonly string Company;
        public readonly string Socket;

        public CompanySocketKey(string company, string socket)
        {
            Company = company;
            Socket = socket;
        }

        public bool Equals(CompanySocketKey other)
        {
            return string.Equals(Company, other.Company, StringComparison.Ordinal) && string.Equals(Socket, other.Socket, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is CompanySocketKey && Equals((CompanySocketKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Company == null ? 0 : Company.GetHashCode();
                return h * 397 ^ (Socket == null ? 0 : Socket.GetHashCode());
            }
        }
    }
}
