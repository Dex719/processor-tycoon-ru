// Polyfill. The C# compiler emits `init` accessors and records only if the type
// System.Runtime.CompilerServices.IsExternalInit exists in the compilation. Neither the mscorlib of Unity 6 (the mod)
// nor .NET Framework 4.8 (the test runner) defines it: without this file `init` and `record` fail with CS0518.
// The compiler only looks the type up by name, so an internal empty class is enough; each assembly gets its own copy.
//
// Verified in spike 0003 (.kiro/decisions/0003-build-toolchain.md, Evidence): `init`, `record`, `record struct` and
// `with` compile in both environments with this file and in neither without it.
//
// Do not add polyfills for System.Index / System.Range: Unity's mscorlib already has them, a second copy conflicts
// (CS0436) and could change behavior at runtime. Core/Logic does not use `^` and `..` because net48 lacks them.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
