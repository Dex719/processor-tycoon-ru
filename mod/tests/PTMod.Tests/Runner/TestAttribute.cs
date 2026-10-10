using System;

namespace PTMod.Tests
{
    /// <summary>
    /// Marks a public parameterless method as a test. The runner finds such methods by reflection
    /// in public classes of the test assembly; a fresh instance of the class is created per test.
    /// Helper classes of a test file must be nested and private, so they are never discovered
    /// and cannot clash with helpers from other test files.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class TestAttribute : Attribute
    {
    }
}
