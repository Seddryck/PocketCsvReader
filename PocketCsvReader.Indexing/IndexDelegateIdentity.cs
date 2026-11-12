using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace PocketCsvReader.Indexing;

internal static class IndexDelegateIdentity
{
    internal static IEnumerable<string?> GetSettings(Delegate registration, string role)
    {
        var method = registration.Method;
        if (registration.GetInvocationList().Length != 1 || method is DynamicMethod || method.Module.Assembly.IsDynamic
            || (registration.Target is { } target && (method.IsStatic || !IsStatelessLambda(target.GetType()))))
        {
            throw new NotSupportedException($"A persisted index requires a static {role} or a non-capturing lambda.");
        }

        return [method.Module.ModuleVersionId.ToString(), method.MetadataToken.ToString(CultureInfo.InvariantCulture),
            method.DeclaringType?.AssemblyQualifiedName, registration.GetType().AssemblyQualifiedName,
            Environment.Version.ToString(), .. method.GetGenericArguments().Select(type => type.AssemblyQualifiedName)];
    }

    private static bool IsStatelessLambda(Type type)
        => type.BaseType == typeof(object) && type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0;
}
