using FluentAssertions;
using VideoDownloader.Domain.Errors;

namespace VideoDownloader.Domain.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_IsSuccess_True()
    {
        var result = Result<string>.Success("value");

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be("value");
    }

    [Fact]
    public void Failure_IsFailure_True()
    {
        var error = new Error("Test.Error", "Something went wrong.");
        var result = Result<string>.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Match_OnSuccess_InvokesOnSuccessFunc()
    {
        var result = Result<int>.Success(42);

        var output = result.Match(
            onSuccess: v => $"ok:{v}",
            onFailure: e => $"err:{e.Code}");

        output.Should().Be("ok:42");
    }

    [Fact]
    public void Match_OnFailure_InvokesOnFailureFunc()
    {
        var error = new Error("Some.Error", "msg");
        var result = Result<int>.Failure(error);

        var output = result.Match(
            onSuccess: v => $"ok:{v}",
            onFailure: e => $"err:{e.Code}");

        output.Should().Be("err:Some.Error");
    }
}
