using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace MuxDeck.Architecture.Tests;

internal sealed class PurePathMembersRule : ICustomRule
{
    // 用户只授权下列纯字符串成员；其余 Path 成员及所有其他 IO 类型默认拒绝。
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "Combine", "Join", "GetFileName", "GetFileNameWithoutExtension", "GetExtension",
        "GetDirectoryName", "ChangeExtension", "HasExtension", "DirectorySeparatorChar", "AltDirectorySeparatorChar"
    };

    public bool MeetsRule(TypeDefinition type)
    {
        if (Forbidden(type.BaseType) || type.Interfaces.Any(item => Forbidden(item.InterfaceType)) ||
            type.Fields.Any(field => Forbidden(field.FieldType)) ||
            type.Properties.Any(property => Forbidden(property.PropertyType) ||
                property.Parameters.Any(parameter => Forbidden(parameter.ParameterType))) ||
            type.Events.Any(item => Forbidden(item.EventType)) ||
            type.GenericParameters.Any(parameter => parameter.Constraints.Any(constraint => Forbidden(constraint.ConstraintType))))
            return false;
        foreach (var method in type.Methods)
        {
            if (Forbidden(method.ReturnType) || method.Parameters.Any(parameter => Forbidden(parameter.ParameterType)) ||
                method.GenericParameters.Any(parameter => parameter.Constraints.Any(constraint => Forbidden(constraint.ConstraintType))))
                return false;
            if (!method.HasBody) continue;
            if (method.Body.Variables.Any(variable => Forbidden(variable.VariableType)) ||
                method.Body.ExceptionHandlers.Any(handler => Forbidden(handler.CatchType))) return false;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string literal &&
                    literal.Contains("System.IO.", StringComparison.Ordinal)) return false;
                if (instruction.Operand is MethodReference call &&
                    (!AllowedMember(call.DeclaringType, call.Name) || Forbidden(call.ReturnType) ||
                     call.Parameters.Any(parameter => Forbidden(parameter.ParameterType)) ||
                     call is GenericInstanceMethod generic && generic.GenericArguments.Any(Forbidden))) return false;
                if (instruction.Operand is FieldReference field &&
                    (!AllowedMember(field.DeclaringType, field.Name) || Forbidden(field.FieldType))) return false;
                if (instruction.Operand is TypeReference reference && Forbidden(reference)) return false;
                if (instruction.Operand is CallSite site &&
                    (Forbidden(site.ReturnType) || site.Parameters.Any(parameter => Forbidden(parameter.ParameterType)))) return false;
            }
        }
        // lambda 与 async 编译生成的嵌套类型也扫描，不依赖默认扫描器是否收录。
        return type.NestedTypes.All(MeetsRule);
    }

    private static bool AllowedMember(TypeReference owner, string name) =>
        !InIo(owner) ? !Forbidden(owner) : owner.FullName == "System.IO.Path" && Allowed.Contains(name);

    private static bool InIo(TypeReference type) =>
        type.Namespace == "System.IO" || type.Namespace.StartsWith("System.IO.", StringComparison.Ordinal);

    private static bool Forbidden(TypeReference? type)
    {
        if (type is null) return false;
        if (type is GenericInstanceType generic && generic.GenericArguments.Any(Forbidden)) return true;
        if (type is TypeSpecification specification) return Forbidden(specification.ElementType);
        return InIo(type) && type.FullName != "System.IO.Path";
    }
}
