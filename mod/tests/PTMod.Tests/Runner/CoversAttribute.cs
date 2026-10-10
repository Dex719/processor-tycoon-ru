using System;

namespace PTMod.Tests
{
    /// <summary>
    /// Declares which logic types a test class covers: <c>[Covers(typeof(ModJson))]</c>.
    /// <c>python mod/build.py --tests --coverage-types</c> fails if a public top-level type compiled into the
    /// test assembly (that is, every public type of Core/Logic/**) is not listed by some [Covers] on a
    /// class that has at least one [Test] method. Nested public types are covered through their declaring type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class CoversAttribute : Attribute
    {
        public Type[] Types { get; }

        public CoversAttribute(params Type[] types)
        {
            Types = types ?? new Type[0];
        }
    }
}
