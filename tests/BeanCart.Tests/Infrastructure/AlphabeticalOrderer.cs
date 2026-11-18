using Xunit.Abstractions;
using Xunit.Sdk;

namespace BeanCart.Tests.Infrastructure;

// xUnit's default order is stable but arbitrary. Isolation tests that must run
// "Y before X, then after X" use this to run in display-name order instead.
public sealed class AlphabeticalOrderer : ITestCaseOrderer
{
    public const string TypeName = "BeanCart.Tests.Infrastructure.AlphabeticalOrderer";
    public const string AssemblyName = "BeanCart.Tests";

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(t => t.DisplayName, StringComparer.Ordinal);
}
