using System.Reflection;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class ToolFailureContractTests
{
    public static TheoryData<string> KnownCodes => new(typeof(ErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(constant => constant.IsLiteral && constant.FieldType == typeof(string))
        .Select(constant => (string)constant.GetRawConstantValue()!));

    [Theory]
    [MemberData(nameof(KnownCodes))]
    public void Failure_keeps_the_registered_code_and_original_message_and_detail(string code)
    {
        var failure = new ToolFailureException(code, "原始错误\n第二行", "stderr\nexit=7");
        Assert.Equal(code, failure.Code);
        Assert.Equal("原始错误\n第二行", failure.Message);
        Assert.Equal("stderr\nexit=7", failure.Detail);
        Assert.IsAssignableFrom<Exception>(failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN_CODE")]
    [InlineData("MUX_TOOL_EXIT_FAILED ")]
    public void Unregistered_failure_codes_are_rejected(string? code)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolFailureException(code!, "原因"));
    }

    [Fact]
    public void Failure_detail_may_be_absent_without_losing_its_message()
    {
        var failure = new ToolFailureException(ErrorCodes.ToolOutputInvalid, "解析失败");
        Assert.Null(failure.Detail);
        Assert.Equal("解析失败", failure.Message);
    }

    [Fact]
    public void Work_file_values_are_independent_of_final_ui_artifacts()
    {
        var first = new WorkFile("work/subtitle.ass");
        var second = new WorkFile("work/subtitle.ass");
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Path = "work/other.ass" });
        var result = new FontSubsetResult(first, [new("work/font.ttf")], []);
        Assert.Equal(first, result.CorrectedSubtitle);
        Assert.Equal(new WorkFile("work/font.ttf"), Assert.Single(result.Fonts));
        Assert.False(typeof(OutputArtifact).IsAssignableFrom(typeof(WorkFile)));
    }

    [Fact]
    public void Original_exception_and_stack_are_preserved_without_changing_the_payload()
    {
        static Exception Capture()
        {
            try { throw new IOException("原始磁盘错误"); }
            catch (IOException original) { return original; }
        }
        var original = Capture();
        var failure = new ToolFailureException(ErrorCodes.WorkspaceIoFailed, "工作区写入失败", "path=work", original);
        Assert.Same(original, failure.InnerException);
        Assert.NotNull(failure.InnerException!.StackTrace);
        Assert.Contains("原始磁盘错误", failure.ToString());
        Assert.Equal(ErrorCodes.WorkspaceIoFailed, failure.Code);
        Assert.Equal("工作区写入失败", failure.Message);
        Assert.Equal("path=work", failure.Detail);
    }

    [Fact]
    public void Explicit_null_inner_exception_keeps_the_existing_constructor_behavior()
    {
        var failure = new ToolFailureException(ErrorCodes.TargetAlreadyExists, "目标已存在", null, null);
        Assert.Null(failure.InnerException);
        Assert.Null(failure.Detail);
        Assert.Equal("目标已存在", failure.Message);
    }

    [Fact]
    public void Inner_exception_constructor_still_rejects_unregistered_codes()
    {
        var original = new IOException("原因");
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolFailureException("UNKNOWN", "错误", null, original));
    }
}
