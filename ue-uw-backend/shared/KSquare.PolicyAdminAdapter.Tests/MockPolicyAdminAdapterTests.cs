using FluentAssertions;
using KSquare.PolicyAdminAdapter.Providers.Mock;
using KSquare.PolicyAdminAdapter.Tests.Synthesizers;

namespace KSquare.PolicyAdminAdapter.Tests;

public sealed class MockPolicyAdminAdapterTests
{
    [Fact]
    public async Task SubmitBindAsync_returns_bound_job_with_policy_number()
    {
        var adapter = new MockPolicyAdminAdapter();
        var req = new PolicyAdminDataSynthesizer(seed: 12).BindRequest();

        var job = await adapter.SubmitBindAsync(req);

        job.Status.Should().Be(KSquare.PolicyAdminAdapter.Models.BindJobStatus.Bound);
        job.PolicyNumber.Should().NotBeNullOrWhiteSpace();
    }
}

