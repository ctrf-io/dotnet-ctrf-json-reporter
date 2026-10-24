using Xunit;
using Xunit.Abstractions;
namespace ConformanceFixture;
public class Cases(ITestOutputHelper output)
{
    [Fact] public void Passing() { output.WriteLine("independent stdout"); Thread.Sleep(12); }
    [Fact] public void Failing() { Assert.True(false, "independent failure"); }
    [Fact(Skip="independent skip")] public void Skipped() { }
    [Theory, InlineData(1), InlineData(2)] public void Data(int value) { Assert.True(value > 0); }
}
