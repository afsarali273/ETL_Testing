namespace EtlTesting.Core;

public static class SuiteHierarchy
{
    // Existing flat suites remain executable. The editor adds explicit test containers in memory.
    public static Suite ForEditing(Suite suite)
    {
        if (suite.TestCases.Count > 0 || suite.Tests.Count == 0) return suite;
        var cases = suite.Tests.Select(t => new TestCaseDefinition { Id = "case-" + t.Id, Name = t.Name, Description = t.Type == "dataComparison" ? "Source-to-target mappings and data validations" : "SQL and data-quality validations" }).ToList();
        return suite with { TestCases = cases, Tests = suite.Tests.Select(t => t with { TestCaseId = "case-" + t.Id }).ToList() };
    }

    public static Suite Duplicate(Suite suite, string name)
    {
        var copy = ForEditing(suite);
        var caseIds = copy.TestCases.ToDictionary(c => c.Id, _ => "test-" + Guid.NewGuid().ToString("N")[..12]);
        return copy with
        {
            Id = "suite-" + Guid.NewGuid().ToString("N")[..12], Name = name,
            TestCases = copy.TestCases.Select(c => c with { Id = caseIds[c.Id] }).ToList(),
            Tests = copy.Tests.Select(t => t with { Id = "check-" + Guid.NewGuid().ToString("N")[..12], TestCaseId = t.TestCaseId == null ? null : caseIds[t.TestCaseId] }).ToList()
        };
    }
}
