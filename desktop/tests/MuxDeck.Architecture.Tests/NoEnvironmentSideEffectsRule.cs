using Mono.Cecil;
using NetArchTest.Rules;

namespace MuxDeck.Architecture.Tests;

internal sealed class NoEnvironmentSideEffectsRule : ICustomRule
{
    // 经用户授权仅放宽 Environment 整类型禁令；时间成员仍由 NoCurrentTimeRule 检查。
    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        "GetEnvironmentVariable", "GetEnvironmentVariables", "SetEnvironmentVariable", "ExpandEnvironmentVariables",
        "MachineName", "UserName", "UserDomainName", "CurrentDirectory", "GetFolderPath",
        "GetCommandLineArgs", "CommandLine", "Exit", "FailFast", "ProcessId", "ProcessPath"
    };

    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods.Where(method => method.HasBody))
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is MethodReference member && member.DeclaringType.FullName == "System.Environment")
                {
                    var name = member.Name;
                    if (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal))
                        name = name[4..];
                    if (Forbidden.Contains(name)) return false;
                }
        // 同样检查编译生成的 lambda、async 和 yield 嵌套类型，避免成员调用被隐藏。
        return type.NestedTypes.All(MeetsRule);
    }
}
