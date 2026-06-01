// Compiler-required marker for `init` accessors and positional records.
// Present in the BCL from .NET 5 onward; supplied here so the library targets net472.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
