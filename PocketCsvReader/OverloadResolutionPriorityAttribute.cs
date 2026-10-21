#if NET8_0
namespace System.Runtime.CompilerServices;

// C# 13 overload priorities preserve source compatibility for untyped string lambdas.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
internal sealed class OverloadResolutionPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}
#endif
