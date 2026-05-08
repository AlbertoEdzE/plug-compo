using FluentAssertions;
using KSquare.ProposalOrchestrator.Models;
using KSquare.ProposalOrchestrator.Providers;
using KSquare.ProposalOrchestrator.Tests.Synthesizers;

namespace KSquare.ProposalOrchestrator.Tests;

public sealed class MockProposalOrchestratorTests
{
    [Fact]
    public async Task StartGenerationAsync_returns_completed_status_immediately()
    {
        var synth = new ProposalOrchestratorDataSynthesizer(seed: 9);
        var request = synth.Request(coverageLines: 1);

        var orchestrator = new MockProposalOrchestrator();
        var job = await orchestrator.StartGenerationAsync(request);

        job.Status.Should().Be(ProposalJobStatus.Completed);
        job.ArtifactBlobPath.Should().NotBeNullOrWhiteSpace();
    }
}

