using System.Collections;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MuxDeck.Domain;
using NetArchTest.Rules;

namespace MuxDeck.Architecture.Tests;

internal static class ContractDataRule
{
    internal static string? Failure(Type type)
    {
        if (type.IsInterface || type.IsEnum || (type.IsAbstract && type.IsSealed)) return null;
        if (type.GetMethod("<Clone>$") is null) return $"{type.FullName} 必须是不可变 record。";
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!field.IsInitOnly) return $"{type.FullName}.{field.Name} 是可写字段。";
            if (!AllowedCollection(field.FieldType)) return $"{type.FullName}.{field.Name} 集合字段必须是 EquatableArray<T>。";
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is { } setter && !setter.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
                return $"{type.FullName}.{property.Name} 是可写 setter。";
            var propertyType = property.PropertyType;
            if (!AllowedCollection(propertyType))
                return $"{type.FullName}.{property.Name} 集合属性必须是 EquatableArray<T>。";
        }
        return null;
    }
    private static bool AllowedCollection(Type type) => type == typeof(string) ||
        !typeof(IEnumerable).IsAssignableFrom(type) ||
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EquatableArray<>);
}

internal sealed class CompositionEntryRule(string? root = null) : ICustomRule
{
    private readonly string root = Path.GetFullPath(root ?? ArchitectureScope.Root);
    public string? FailureReason { get; private set; }

    public bool MeetsRule(TypeDefinition type)
    {
        while (type.DeclaringType != null) type = type.DeclaringType;
        if (!ArchitectureScope.InNamespace(type.Namespace, "MuxDeck.Desktop.Composition"))
            return Reject(type, "Infrastructure 只能在 MuxDeck.Desktop.Composition 使用。");

        try
        {
            // 读取真正的 PDB 源码归属；命名空间改名不能绕过目录边界。
            using var module = ModuleDefinition.ReadModule(type.Module.FileName, new ReaderParameters { ReadSymbols = true });
            var sourceType = module.GetType(type.FullName);
            if (sourceType is null) return Reject(type, "PDB 对应模块找不到该类型。");
            var documents = SourceDocuments(sourceType).Distinct().ToArray();
            if (documents.Length == 0) return Reject(type, "缺少 PDB 源码序列点，无法验证装配路径。");
            var desktop = Path.Combine(root, "src", "MuxDeck.Desktop");
            var program = Path.Combine(desktop, "Program.cs");
            var composition = Path.Combine(desktop, "Composition") + Path.DirectorySeparatorChar;
            foreach (var document in documents)
            {
                if (document.Replace('\\', '/').StartsWith("/_/", StringComparison.Ordinal))
                    return Reject(type, $"源码路径已映射为 /_/：{document}；无法验证实际装配目录。");
                if (!Path.IsPathFullyQualified(document))
                    return Reject(type, $"源码路径不是绝对路径：{document}。");
                var path = Path.GetFullPath(document);
                if (!(string.Equals(path, program, StringComparison.OrdinalIgnoreCase) ||
                      path.StartsWith(composition, StringComparison.OrdinalIgnoreCase)))
                    return Reject(type, $"源码路径不属于 Program / Composition：{document}。");
            }
            return true;
        }
        catch (SymbolsNotFoundException)
        {
            return Reject(type, $"缺少 PDB：{type.Module.FileName}；无法验证实际装配路径。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or BadImageFormatException or SymbolsNotMatchingException)
        {
            return Reject(type, $"无法读取 PDB / 源码路径：{error.Message}。");
        }
    }

    private bool Reject(TypeDefinition type, string reason)
    {
        FailureReason = string.IsNullOrEmpty(FailureReason) ? $"{type.FullName}：{reason}" :
            FailureReason + "；" + $"{type.FullName}：{reason}";
        return false;
    }

    private static IEnumerable<string> SourceDocuments(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        foreach (var point in method.DebugInformation.SequencePoints.Where(point => !point.IsHidden))
            yield return point.Document.Url;
        foreach (var nested in type.NestedTypes)
        foreach (var document in SourceDocuments(nested)) yield return document;
    }
}

internal sealed class NoCurrentTimeRule : ICustomRule
{
    public bool MeetsRule(TypeDefinition type)
    {
        if (type.Fields.Any(field => IsStopwatch(field.FieldType))) return false;
        foreach (var method in type.Methods)
        {
            if (IsStopwatch(method.ReturnType) || method.Parameters.Any(parameter => IsStopwatch(parameter.ParameterType)))
                return false;
            if (!method.HasBody) continue;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is TypeReference reference && IsStopwatch(reference)) return false;
                if (instruction.Operand is FieldReference field && IsStopwatch(field.DeclaringType)) return false;
                if (instruction.Operand is not MethodReference call) continue;
                var owner = call.DeclaringType.FullName;
                if (owner is "System.DateTime" or "System.DateTimeOffset" &&
                    call.Name is "get_Now" or "get_UtcNow" or "get_Today") return false;
                if (owner == "System.TimeProvider" && call.Name == "get_System") return false;
                if (owner == "System.Guid" && call.Name == "NewGuid") return false;
                if (owner == "System.Diagnostics.Stopwatch") return false;
                if (owner == "System.Environment" && call.Name is "get_TickCount" or "get_TickCount64") return false;
            }
        }
        // 编译生成的 lambda 和 async 状态机也检查，不能因 NetArchTest 默认过滤而漏掉。
        return type.NestedTypes.All(MeetsRule);
    }

    private static bool IsStopwatch(TypeReference type) =>
        type.FullName == "System.Diagnostics.Stopwatch" ||
        type is GenericInstanceType generic && generic.GenericArguments.Any(IsStopwatch) ||
        type is TypeSpecification specification && IsStopwatch(specification.ElementType);
}
